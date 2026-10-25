using System.Reflection;
using RouterPlus.App.Services;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;
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

    [Fact]
    public async Task DeleteSelectedProfile_failure_after_save_restores_managed_and_allows_retry()
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

            var throwingDeleter = new ChromeProfileDeleter((_, _) => throw new InvalidOperationException("boom"));
            var viewModel = new MainViewModel(new SettingsStore(settingsPath), profileDeleter: throwingDeleter);
            await viewModel.InitializeAsync();
            viewModel.SelectedProfile = viewModel.Profiles.Single(profile => profile.Name == "Work");

            await viewModel.DeleteSelectedProfileAsync();

            Assert.True(Directory.Exists(Path.Combine(userDataDirectory, "Profile 1")));

            var manager = typeof(MainViewModel)
                .GetField("_profileManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(viewModel) as IProfileManager;
            Assert.NotNull(manager);
            Assert.Single(manager!.ManagedProfiles);

            var settings = await new SettingsStore(settingsPath).LoadAsync();
            Assert.Single(settings.ManagedProfiles!);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task DeleteSelectedProfile_stale_user_data_setting_does_not_delete_outside_discovered_root()
    {
        var directory = CreateTempDirectory();
        try
        {
            var userDataDirectory = Path.Combine(directory, "User Data");
            var otherDirectory = Path.Combine(directory, "Other Data");
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

            // Simulate a stale VM setting and no discovered installation.
            viewModel.ChromeUserDataDirectory = otherDirectory;
            var installationField = typeof(MainViewModel).GetField("_installation", BindingFlags.Instance | BindingFlags.NonPublic);
            installationField?.SetValue(viewModel, null);

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
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task AddProfileAsync_save_failure_removes_managed_and_cleans_created_directory()
    {
        var directory = CreateTempDirectory();
        try
        {
            var userDataDirectory = Path.Combine(directory, "User Data");
            var chromeExecutablePath = Path.Combine(directory, "chrome.exe");
            var settingsPath = Path.Combine(directory, "settings.json");
            Directory.CreateDirectory(userDataDirectory);
            File.WriteAllText(chromeExecutablePath, string.Empty);
            // Minimal Local State so RefreshProfiles works.
            File.WriteAllText(Path.Combine(userDataDirectory, "Local State"), """
                {
                  "profile": { "info_cache": { "Default": { "name": "Personal" } } }
                }
                """);
            Directory.CreateDirectory(Path.Combine(userDataDirectory, "Default"));

            await new SettingsStore(settingsPath).SaveAsync(new RouterSettings(
                DashboardBaseUrl: "http://127.0.0.1:1",
                ChromeExecutablePath: chromeExecutablePath,
                ChromeUserDataDirectory: userDataDirectory));

            var viewModel = new MainViewModel(new SettingsStore(settingsPath));
            await viewModel.InitializeAsync();

            // Force save failure on AddProfile.
            File.Delete(settingsPath);
            Directory.CreateDirectory(settingsPath);

            viewModel.ProfileSearchText = "NewProfile";
            await viewModel.AddProfileAsync();

            var createdPath = Path.Combine(userDataDirectory, "Profile NewProfile");
            Assert.False(Directory.Exists(createdPath));

            var manager = typeof(MainViewModel)
                .GetField("_profileManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(viewModel) as IProfileManager;
            Assert.NotNull(manager);
            Assert.Empty(manager!.ManagedProfiles);
        }
        finally
        {
            var sp = Path.Combine(directory, "settings.json");
            if (Directory.Exists(sp)) Directory.Delete(sp, recursive: true);
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