using System.Text.Json;

namespace RouterPlus.App.E2E;

public sealed class TestEnvironment : IAsyncDisposable
{
    private TestEnvironment(string rootPath)
    {
        RootPath = rootPath;
        SettingsPath = Path.Combine(rootPath, "settings.json");
        HarnessManifestPath = Path.Combine(rootPath, "harness-manifest.json");
        ArtifactPath = Path.Combine(rootPath, "artifacts");
    }

    public string RootPath { get; }
    public string SettingsPath { get; }
    public string HarnessManifestPath { get; }
    public string ArtifactPath { get; }

    public static async Task<TestEnvironment> CreateAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "RouterPlusHarness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "artifacts"));

        var environment = new TestEnvironment(rootPath);
        var manifest = new
        {
            Profiles = new[]
            {
                new { Name = "Harness Alpha", DirectoryName = "Default" },
                new { Name = "Harness Beta", DirectoryName = "Profile 1" }
            }
        };
        await File.WriteAllTextAsync(
            environment.HarnessManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return environment;
    }

    public ValueTask DisposeAsync()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("ROUTERPLUS_HARNESS_KEEP_ARTIFACTS"), "1", StringComparison.Ordinal))
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Preserve test result if cleanup is delayed by Windows file handles.
        }
        return ValueTask.CompletedTask;
    }
}
