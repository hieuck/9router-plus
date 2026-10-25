using System.Reflection;
using RouterPlus.App.Services;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class MainViewModelProfileDeleteOrderingTests
{
    [Fact]
    public async Task DeleteSelectedProfile_save_failure_does_not_delete_and_restores_managed()
    {
        var directory = CreateTempDirectory();
        try
        {
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
                ManagedProfiles: new[] { new ManagedChromeProfile("Work", "Profile 1", userDataDirectory) }));

            var viewModel = new MainViewModel(new SettingsStore(settingsPath));
            await viewModel.InitializeAsync();
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Name == "Work");

            // Force subsequent save to fail.
            File.Delete(settingsPath);
            Directory.CreateDirectory(settingsPath);

            await viewModel.DeleteSelectedProfileAsync();

            Assert.True(Directory.Exists(Path.Combine(userDataDirectory, "Profile 1")));

            var manager = typeof(MainViewModel)
                .GetField("_profileManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(viewModel) as IProfileManager;
            Assert.NotNull(manager);
            Assert.Single(manager!.ManagedProfiles);
        }
        finally
        {
            var sp = Path.Combine(directory, "settings.json");
            if (Directory.Exists(sp)) Directory.Delete(sp, recursive: true);
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteSelectedProfile_success_deletes_after_save()
    {
        var directory = CreateTempDirectory();
        try
        {
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
                ManagedProfiles: new[] { new ManagedChromeProfile("Work", "Profile 1", userDataDirectory) }));

            var viewModel = new MainViewModel(new SettingsStore(settingsPath));
            await viewModel.InitializeAsync();
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Name == "Work");

            await viewModel.DeleteSelectedProfileAsync();

            Assert.False(Directory.Exists(Path.Combine(userDataDirectory, "Profile 1")));
            var settings = await new SettingsStore(settingsPath).LoadAsync();
            Assert.Empty(settings.ManagedProfiles!);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusDeleteOrderingTests", Guid.NewGuid().ToString("N"));
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