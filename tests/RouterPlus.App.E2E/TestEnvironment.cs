using System.Text.Json;

namespace RouterPlus.App.E2E;

/// <summary>
/// Creates isolated test environment with synthetic Chrome profiles.
/// </summary>
public sealed class TestEnvironment : IAsyncDisposable
{
    private TestEnvironment(string rootPath)
    {
        RootPath = rootPath;
        SettingsPath = Path.Combine(rootPath, "settings.json");
        ManifestPath = Path.Combine(rootPath, "harness-manifest.json");
    }

    public string RootPath { get; }
    public string SettingsPath { get; }
    public string ManifestPath { get; }

    public static async Task<TestEnvironment> CreateAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "RouterPlusE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var env = new TestEnvironment(rootPath);

        // Write manifest with 2 synthetic profiles
        var manifest = new
        {
            Profiles = new[]
            {
                new { Name = "Test Profile 1", DirectoryName = "Default" },
                new { Name = "Test Profile 2", DirectoryName = "Profile 1" }
            }
        };

        await File.WriteAllTextAsync(
            env.ManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        return env;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();

        if (Directory.Exists(RootPath))
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
                // Windows may hold file handles briefly
            }
        }
    }
}
