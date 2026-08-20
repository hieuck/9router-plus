using RouterPlus.App.ViewModels;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelProfileContextMenuTests
{
    [Fact]
    public async Task DeleteSelectedProfile_removes_directory_mapping_and_selects_remaining_profile()
    {
        var directory = CreateTempDirectory();
        var userDataDirectory = Path.Combine(directory, "User Data");
        var chromeExecutablePath = Path.Combine(directory, "chrome.exe");
        var settingsPath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(Path.Combine(userDataDirectory, "Default"));
        Directory.CreateDirectory(Path.Combine(userDataDirectory, "Profile 1"));
        File.WriteAllText(Path.Combine(userDataDirectory, "Local State"), """
            {
              "profile": {
                "info_cache": {
                  "Default": { "name": "Personal" },
                  "Profile 1": { "name": "Work" }
                }
              }
            }
            """);
        File.WriteAllText(chromeExecutablePath, string.Empty);
        await new SettingsStore(settingsPath).SaveAsync(new RouterSettings(
            DashboardBaseUrl: "http://127.0.0.1:1",
            ChromeExecutablePath: chromeExecutablePath,
            ChromeUserDataDirectory: userDataDirectory,
            ManagedProfiles:
            [
                new("Work", "Profile 1", userDataDirectory)
            ]));

        try
        {
            var viewModel = new MainViewModel(new SettingsStore(settingsPath));
            await viewModel.InitializeAsync();
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Name == "Work");

            await viewModel.DeleteSelectedProfileAsync();

            Assert.False(Directory.Exists(Path.Combine(userDataDirectory, "Profile 1")));
            var remainingProfile = Assert.Single(viewModel.Profiles);
            Assert.Equal("Personal", remainingProfile.Name);
            Assert.Equal(remainingProfile, viewModel.SelectedProfile);

            var settings = await new SettingsStore(settingsPath).LoadAsync();
            Assert.Empty(settings.ManagedProfiles!);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task OpenSelectedGoogleLogin_without_profile_reports_selection_status()
    {
        var viewModel = new MainViewModel();

        await viewModel.OpenSelectedGoogleLoginAsync();

        Assert.Equal("Hãy chọn Chrome profile trước.", viewModel.StatusText);
    }

    [Fact]
    public void SelectProfileForContextMenu_selects_profile_immediately()
    {
        var viewModel = new MainViewModel();
        var profile = new RouterPlus.Core.Chrome.ChromeProfile(
            RouterPlus.Core.Chrome.ChromeProfile.CreateId("C:\\Chrome\\User Data", "Profile 1"),
            "Work",
            "Profile 1",
            "C:\\Chrome\\User Data",
            false);

        viewModel.SelectProfileForContextMenu(profile);

        Assert.Equal(profile, viewModel.SelectedProfile);
    }
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
