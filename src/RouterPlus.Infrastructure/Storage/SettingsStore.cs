using System.Text.Json;

namespace RouterPlus.Infrastructure.Storage;

public sealed class SettingsStore
{
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
            return new RouterSettings();
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
            return new RouterSettings();
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
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _filePath, true);
    }
}
