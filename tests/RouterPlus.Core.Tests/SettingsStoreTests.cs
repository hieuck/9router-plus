using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public async Task Missing_settings_default_to_light_theme()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var settings = await new SettingsStore(filePath).LoadAsync();

            Assert.True(settings.UseLightTheme);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Router_settings_default_to_light_theme()
    {
        Assert.True(new RouterSettings().UseLightTheme);
    }

    [Fact]
    public async Task SaveAndLoad_preserves_window_placement()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var settings = new RouterSettings(
                WindowLeft: 120d,
                WindowTop: 80d,
                WindowWidth: 1320d,
                WindowHeight: 840d);

            var store = new SettingsStore(filePath);
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Equal(settings.WindowLeft, loaded.WindowLeft);
            Assert.Equal(settings.WindowTop, loaded.WindowTop);
            Assert.Equal(settings.WindowWidth, loaded.WindowWidth);
            Assert.Equal(settings.WindowHeight, loaded.WindowHeight);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAndLoad_preserves_managed_profile_metadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var settings = new RouterSettings(
                ChromeUserDataDirectory: "C:\\Chrome\\User Data",
                ManagedProfiles:
                [
                    new("Personal", "Default", "C:\\Chrome\\User Data"),
                    new("Work", "Profile 1", "C:\\Chrome\\User Data")
                ]);

            var store = new SettingsStore(filePath);
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Equal(settings.ManagedProfiles, loaded.ManagedProfiles);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateQuotaAutoDisableMarkers_preserves_existing_settings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            var store = new SettingsStore(filePath);
            var existingSettings = new RouterSettings(
                DashboardBaseUrl: "http://localhost:20129",
                ManagedProfiles: [new("Work", "Profile 1", "C:\\Chrome\\User Data")]);
            await store.SaveAsync(existingSettings);
            var resetAt = DateTimeOffset.UtcNow.AddHours(1);

            await store.UpdateQuotaAutoDisableMarkersAsync([
                new("connection-1", RouterPlus.Core.Providers.ProviderKind.Codex, "Work", resetAt)
            ]);

            var loaded = await store.LoadAsync();
            var marker = Assert.Single(loaded.QuotaAutoDisableMarkers!);
            Assert.Equal("http://localhost:20129", loaded.DashboardBaseUrl);
            Assert.Equal(existingSettings.ManagedProfiles, loaded.ManagedProfiles);
            Assert.Equal("connection-1", marker.ConnectionId);
            Assert.Equal(resetAt, marker.ResetAt);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Legacy_keyboard_shortcut_fields_are_ignored_and_not_written_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(filePath, """
                {
                  "dashboardBaseUrl": "http://legacy.example:20128",
                  "enableKeyboardShortcuts": true,
                  "keyboardShortcuts": { "OpenProviderCodex": "Ctrl+Alt+1" }
                }
                """);

            var store = new SettingsStore(filePath);
            var loaded = await store.LoadAsync();
            await store.SaveAsync(loaded);
            var savedJson = await File.ReadAllTextAsync(filePath);

            Assert.Equal("http://legacy.example:20128", loaded.DashboardBaseUrl);
            Assert.DoesNotContain("enableKeyboardShortcuts", savedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("keyboardShortcuts", savedJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
