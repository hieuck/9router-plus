using System.Collections.Concurrent;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Storage;

/// <summary>
/// Provides asynchronous and synchronous access to user settings stored in JSON file.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>
    /// Locks used to prevent concurrent writes to the settings file.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Path to the settings JSON file.
    /// </summary>
    private readonly string _filePath;
    /// <summary>
    /// JSON serializer options with indentation enabled.
    /// </summary>
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsStore"/> class.
    /// If <paramref name="filePath"/> is null, defaults to %LocalAppData%\9RouterPlus\settings.json.
    /// </summary>
    /// <param name="filePath">Optional custom path to the settings file.</param>
    public SettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus",
            "settings.json");
    }

    /// <summary>
    /// Asynchronously loads the settings from file. Returns defaults if file missing or corrupt.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The loaded <see cref="RouterSettings"/>.</returns>
    public async Task<RouterSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new RouterSettings { UseLightTheme = true };
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<RouterSettings>(stream, _jsonOptions, cancellationToken)
                ?? new RouterSettings();
        }
        catch (JsonException)
        {
            // Return defaults if JSON is corrupted or incompatible
            return new RouterSettings();
        }
    }

    /// <summary>
    /// Synchronously loads the settings from file. Returns defaults if file missing or corrupt.
    /// </summary>
    /// <returns>The loaded <see cref="RouterSettings"/>.</returns>
    public RouterSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new RouterSettings { UseLightTheme = true };
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<RouterSettings>(json, _jsonOptions) ?? new RouterSettings();
        }
        catch (JsonException)
        {
            // Return defaults if JSON is corrupted or incompatible
            return new RouterSettings();
        }
    }

    /// <summary>
    /// Asynchronously saves the settings to file.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SaveAsync(RouterSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await WithSaveLockAsync(settings, cancellationToken);
    }

    /// <summary>
    /// Asynchronously updates the quota auto‑disable markers in the settings file.
    /// </summary>
    /// <param name="markers">The markers to store.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task UpdateQuotaAutoDisableMarkersAsync(
        IReadOnlyList<QuotaAutoDisableMarker> markers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markers);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var saveLock = SaveLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadAsync(cancellationToken);
            await WriteAsync(current with { QuotaAutoDisableMarkers = markers.ToArray() }, cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task WithSaveLockAsync(RouterSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var saveLock = SaveLocks.GetOrAdd(_filePath, _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(settings, cancellationToken);
        }
        finally
        {
            saveLock.Release();
        }
    }

    private async Task WriteAsync(RouterSettings settings, CancellationToken cancellationToken)
    {
        var temporaryPath = _filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // Preserve the original save result; cleanup can be retried on the next save.
            }
        }
    }
}
