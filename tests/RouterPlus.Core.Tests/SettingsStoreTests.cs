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
}
