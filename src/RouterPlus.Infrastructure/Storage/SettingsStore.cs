using System.Collections.Concurrent;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Storage;

public sealed class SettingsStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus",
            "settings.json");
    }

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

    public async Task SaveAsync(RouterSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await WithSaveLockAsync(settings, cancellationToken);
    }

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
