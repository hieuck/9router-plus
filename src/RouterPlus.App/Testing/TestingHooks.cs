using System.IO;
using System.Text.Json;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;

namespace RouterPlus.App.Testing;

/// <summary>
/// Testing hooks for E2E automation to access internal state.
/// Only enabled when ENABLE_TESTING_HOOKS environment variable is set.
/// Exposes state via JSON file for cross-process access.
/// </summary>
public static class TestingHooks
{
    private static MainViewModel? _mainViewModel;
    private static string? _stateFilePath;

    /// <summary>
    /// Whether testing hooks are enabled.
    /// </summary>
    public static bool Enabled { get; private set; }

    /// <summary>
    /// Initialize testing hooks with MainViewModel instance.
    /// </summary>
    public static void Initialize(MainViewModel mainViewModel)
    {
        var enabledEnv = Environment.GetEnvironmentVariable("ENABLE_TESTING_HOOKS");

        // Debug: log what we see
        System.Diagnostics.Debug.WriteLine($"[TestingHooks] ENABLE_TESTING_HOOKS env var: '{enabledEnv}'");

        Enabled = enabledEnv == "1" || enabledEnv?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

        System.Diagnostics.Debug.WriteLine($"[TestingHooks] Enabled: {Enabled}");

        if (Enabled)
        {
            _mainViewModel = mainViewModel;
            _stateFilePath = Path.Combine(Path.GetTempPath(), $"routerplus-test-state-{Environment.ProcessId}.json");
            System.Diagnostics.Debug.WriteLine($"[TestingHooks] State file: {_stateFilePath}");
            System.Diagnostics.Debug.WriteLine($"[TestingHooks] MainViewModel registered");

            // Subscribe to property changes to update state file
            _mainViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.StatusText))
                {
                    UpdateStateFile();
                }
            };

            // Subscribe to profile row changes
            foreach (var row in _mainViewModel.ProfileRows)
            {
                row.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ProfileRowViewModel.HealthStatus) ||
                        e.PropertyName == nameof(ProfileRowViewModel.IsCheckingHealth))
                    {
                        UpdateStateFile();
                    }
                };
            }

            UpdateStateFile();
        }
    }

    private static void UpdateStateFile()
    {
        if (!Enabled || _mainViewModel == null || _stateFilePath == null)
        {
            return;
        }

        try
        {
            var state = new
            {
                ProcessId = Environment.ProcessId,
                StatusText = _mainViewModel.StatusText,
                Profiles = _mainViewModel.ProfileRows.Select(r => new
                {
                    Name = r.Name,
                    HealthLevel = r.HealthStatus?.Level.ToString() ?? "Unknown",
                    HealthMessage = r.HealthStatus?.Message,
                    IssueCount = r.HealthStatus?.Issues.Count ?? 0,
                    IsCheckingHealth = r.IsCheckingHealth
                }).ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch
        {
            // Ignore errors writing state file
        }
    }

    /// <summary>
    /// Get state file path for a given process ID.
    /// </summary>
    public static string GetStateFilePath(int processId)
    {
        return Path.Combine(Path.GetTempPath(), $"routerplus-test-state-{processId}.json");
    }

    /// <summary>
    /// Read state from file for cross-process access.
    /// </summary>
    public static string? ReadStateFile(int processId)
    {
        var path = GetStateFilePath(processId);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return null;
    }
}
