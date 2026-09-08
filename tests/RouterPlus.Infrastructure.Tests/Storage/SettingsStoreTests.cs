using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Infrastructure.Tests.Storage;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_light_theme_defaults_when_file_is_missing()
    {
        using var fixture = new SettingsFileFixture();

        var settings = await fixture.Store.LoadAsync();

        Assert.True(settings.UseLightTheme);
    }

    [Fact]
    public void Load_returns_light_theme_defaults_when_file_is_missing()
    {
        using var fixture = new SettingsFileFixture();

        var settings = fixture.Store.Load();

        Assert.True(settings.UseLightTheme);
    }

    [Fact]
    public async Task LoadAsync_returns_empty_defaults_when_json_is_corrupt()
    {
        using var fixture = new SettingsFileFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        await File.WriteAllTextAsync(fixture.FilePath, "{ invalid json");

        var settings = await fixture.Store.LoadAsync();

        Assert.Equal(new RouterSettings(), settings);
    }

    [Fact]
    public void Load_returns_empty_defaults_when_json_is_corrupt()
    {
        using var fixture = new SettingsFileFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        File.WriteAllText(fixture.FilePath, "{ invalid json");

        var settings = fixture.Store.Load();

        Assert.Equal(new RouterSettings(), settings);
    }

    [Fact]
    public async Task SaveAsync_round_trips_settings_and_creates_parent_directory()
    {
        using var fixture = new SettingsFileFixture();
        var settings = new RouterSettings(
            DashboardBaseUrl: "http://localhost:20129",
            DashboardAuthPassword: "synthetic-password",
            ChromeUserDataDirectory: "C:\\Chrome\\User Data",
            FontScale: 1.25,
            UseLightTheme: false,
            ManagedProfiles: [new("Work", "Profile 1", "C:\\Chrome\\User Data")],
            RecentProfiles: [new("work", "Work", "C:\\Chrome\\User Data", DateTime.UtcNow, 3, true)]);

        await fixture.Store.SaveAsync(settings);
        var loaded = await fixture.Store.LoadAsync();

        Assert.Equal(settings.DashboardBaseUrl, loaded.DashboardBaseUrl);
        Assert.Equal(settings.DashboardAuthPassword, loaded.DashboardAuthPassword);
        Assert.Equal(settings.ChromeUserDataDirectory, loaded.ChromeUserDataDirectory);
        Assert.Equal(settings.FontScale, loaded.FontScale);
        Assert.Equal(settings.UseLightTheme, loaded.UseLightTheme);
        Assert.Equal(settings.ManagedProfiles, loaded.ManagedProfiles);
        Assert.Equal(settings.RecentProfiles, loaded.RecentProfiles);
        Assert.True(File.Exists(fixture.FilePath));
    }

    [Fact]
    public async Task UpdateQuotaAutoDisableMarkersAsync_preserves_existing_settings_and_replaces_markers()
    {
        using var fixture = new SettingsFileFixture();
        var existing = new RouterSettings(
            DashboardBaseUrl: "http://localhost:20129",
            ManagedProfiles: [new("Work", "Profile 1", "C:\\Chrome\\User Data")],
            QuotaAutoDisableMarkers: [new("old", ProviderKind.Kiro, "Old", null)]);
        await fixture.Store.SaveAsync(existing);
        var resetAt = DateTimeOffset.UtcNow.AddMinutes(30);

        await fixture.Store.UpdateQuotaAutoDisableMarkersAsync([
            new("new", ProviderKind.Codex, "Work", resetAt)
        ]);
        var loaded = await fixture.Store.LoadAsync();

        Assert.Equal(existing.DashboardBaseUrl, loaded.DashboardBaseUrl);
        Assert.Equal(existing.ManagedProfiles, loaded.ManagedProfiles);
        Assert.Equal([new QuotaAutoDisableMarker("new", ProviderKind.Codex, "Work", resetAt)], loaded.QuotaAutoDisableMarkers);
    }

    [Fact]
    public async Task SaveAsync_throws_for_null_settings()
    {
        using var fixture = new SettingsFileFixture();

        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Store.SaveAsync(null!));
    }

    [Fact]
    public async Task UpdateQuotaAutoDisableMarkersAsync_throws_for_null_markers()
    {
        using var fixture = new SettingsFileFixture();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            fixture.Store.UpdateQuotaAutoDisableMarkersAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_honors_cancellation_before_writing()
    {
        using var fixture = new SettingsFileFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Store.SaveAsync(new RouterSettings(), cancellation.Token));
        Assert.False(File.Exists(fixture.FilePath));
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
