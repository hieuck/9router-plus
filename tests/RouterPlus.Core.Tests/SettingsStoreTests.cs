using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class SettingsStoreTests
{
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
