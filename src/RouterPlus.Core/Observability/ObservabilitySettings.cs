using System;
using System.IO;
using System.Text.Json;

namespace RouterPlus.Core.Observability;

/// <summary>
/// User-configurable settings for observability system.
/// </summary>
public sealed class ObservabilitySettings
{
    public bool EnableLogging { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public bool EnableSnapshots { get; set; } = true;
    public int RetentionDays { get; set; } = 7;
    public int MaxSessionSizeMB { get; set; } = 100;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RouterPlus", "Observability", "settings.json");

    /// <summary>
    /// Loads settings from disk, or returns defaults if file doesn't exist.
    /// </summary>
    public static ObservabilitySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<ObservabilitySettings>(json) ?? new ObservabilitySettings();
            }
        }
        catch
        {
            // Return defaults on error
        }

        return new ObservabilitySettings();
    }

    /// <summary>
    /// Saves current settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Fail silently - settings are non-critical
        }
    }
}
