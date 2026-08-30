using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;

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

    public static async Task<TestEnvironment> CreateAsync(bool rememberVault = true)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "RouterPlusE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var env = new TestEnvironment(rootPath);

        // Write manifest with 2 synthetic profiles
        var manifest = new
        {
            Profiles = new[]
            {
                new { Name = "Harness Alpha", DirectoryName = "Default" },
                new { Name = "Harness Beta", DirectoryName = "Profile 1" }
            }
        };

        await File.WriteAllTextAsync(
            env.ManifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        await SeedGoogleVaultAsync(env, rememberVault);
        return env;
    }

    private static async Task SeedGoogleVaultAsync(TestEnvironment environment, bool rememberVault)
    {
        var paths = new GoogleAccountVaultPaths(Path.Combine(environment.RootPath, "Vault"));
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(
            paths.VaultPath,
            "harness-vault-password",
            CancellationToken.None);

        var vault = new GoogleAccountVault(new[]
        {
            new GoogleLoginCredential("Harness Alpha", "alpha@example.test", "alpha-password", "NONE"),
            new GoogleLoginCredential("Harness Beta", "beta@example.test", "beta-password", "JBSWY3DPEHPK3PXP")
        });
        session.Replace(vault);
        await store.SaveAsync(session, CancellationToken.None);
        if (rememberVault)
        {
            await session.RememberAsync(CancellationToken.None);
        }
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
