using System.Runtime.InteropServices;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Infrastructure.Tests.Storage;

public sealed class DashboardPasswordSettingsTests
{
    [Fact]
    public async Task SaveAsync_WritesProtectedPassword_NotPlaintext()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var fixture = new SettingsFileFixture();
        var settings = new RouterSettings(DashboardAuthPassword: "synthetic-password");

        await fixture.Store.SaveAsync(settings);
        var rawJson = await File.ReadAllTextAsync(fixture.FilePath);

        Assert.DoesNotContain("synthetic-password", rawJson, StringComparison.Ordinal);
        Assert.Contains("dpapi:v1:", rawJson, StringComparison.Ordinal);

        var loaded = await fixture.Store.LoadAsync();
        Assert.Equal("synthetic-password", loaded.DashboardAuthPassword);
    }

    [Fact]
    public async Task LegacyPlaintext_LoadsAndMigrates()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var fixture = new SettingsFileFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        await File.WriteAllTextAsync(fixture.FilePath, """
            {
              "dashboardBaseUrl": "http://localhost:20129",
              "dashboardAuthPassword": "legacy-secret"
            }
            """);

        var loaded = await fixture.Store.LoadAsync();
        Assert.Equal("legacy-secret", loaded.DashboardAuthPassword);

        await fixture.Store.MigrateDashboardPasswordAsync();
        var rawJson = await File.ReadAllTextAsync(fixture.FilePath);
        Assert.DoesNotContain("legacy-secret", rawJson, StringComparison.Ordinal);
        Assert.Contains("dpapi:v1:", rawJson, StringComparison.Ordinal);

        // idempotent
        await fixture.Store.MigrateDashboardPasswordAsync();
        var rawJson2 = await File.ReadAllTextAsync(fixture.FilePath);
        Assert.Equal(rawJson, rawJson2);

        var loaded2 = await fixture.Store.LoadAsync();
        Assert.Equal("legacy-secret", loaded2.DashboardAuthPassword);
    }

    [Fact]
    public async Task UpdateQuotaMarkers_PreservesEncryptedPassword()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var fixture = new SettingsFileFixture();
        var existing = new RouterSettings(
            DashboardBaseUrl: "http://localhost:20129",
            DashboardAuthPassword: "marker-secret");
        await fixture.Store.SaveAsync(existing);

        await fixture.Store.UpdateQuotaAutoDisableMarkersAsync([
            new("new", ProviderKind.Codex, "Work", DateTimeOffset.UtcNow.AddMinutes(30))
        ]);

        var rawJson = await File.ReadAllTextAsync(fixture.FilePath);
        Assert.DoesNotContain("marker-secret", rawJson, StringComparison.Ordinal);
        Assert.Contains("dpapi:v1:", rawJson, StringComparison.Ordinal);

        var loaded = await fixture.Store.LoadAsync();
        Assert.Equal("marker-secret", loaded.DashboardAuthPassword);
    }

    [Fact]
    public async Task CorruptProtectedPassword_LoadsNull_WithoutThrowing()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        using var fixture = new SettingsFileFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        await File.WriteAllTextAsync(fixture.FilePath, """
            {
              "dashboardBaseUrl": "http://localhost:20129",
              "dashboardAuthPassword": "dpapi:v1:AAAA"
            }
            """);

        var loaded = await fixture.Store.LoadAsync();
        Assert.Null(loaded.DashboardAuthPassword);
        Assert.Equal("http://localhost:20129", loaded.DashboardBaseUrl);
    }

    private sealed class SettingsFileFixture : IDisposable
    {
        public SettingsFileFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "RouterPlusInfrastructureTests", Guid.NewGuid().ToString("N"));
            FilePath = Path.Combine(DirectoryPath, "settings.json");
            Store = new SettingsStore(FilePath);
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }
        public SettingsStore Store { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}