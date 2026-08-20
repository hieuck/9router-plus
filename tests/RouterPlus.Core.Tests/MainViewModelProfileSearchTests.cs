using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelProfileSearchTests
{
    [Fact]
    public void New_trimmed_query_exposes_add_action_with_exact_display_name()
    {
        var viewModel = new MainViewModel();

        viewModel.ProfileSearchText = "  New profile  ";

        Assert.True(viewModel.CanAddProfile);
        Assert.Equal("Thêm profile \"New profile\"", viewModel.ProfileAddButtonText);
        Assert.True(viewModel.AddProfileCommand.CanExecute(null));
    }

    [Fact]
    public void Blank_query_does_not_expose_add_action()
    {
        var viewModel = new MainViewModel();

        viewModel.ProfileSearchText = "   ";

        Assert.False(viewModel.CanAddProfile);
        Assert.False(viewModel.AddProfileCommand.CanExecute(null));
    }

    [Fact]
    public void Exact_case_insensitive_existing_name_does_not_expose_add_action()
    {
        var viewModel = new MainViewModel();
        viewModel.Profiles.Add(new ChromeProfile(
            ChromeProfile.CreateId("C:\\Chrome\\User Data", "Default"),
            "Personal",
            "Default",
            "C:\\Chrome\\User Data",
            true));

        viewModel.ProfileSearchText = " personal ";

        Assert.False(viewModel.CanAddProfile);
        Assert.False(viewModel.AddProfileCommand.CanExecute(null));
    }

    [Fact]
    public void Query_and_profile_changes_notify_add_action_state()
    {
        var viewModel = new MainViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        viewModel.ProfileSearchText = "New profile";
        viewModel.Profiles.Add(new ChromeProfile(
            ChromeProfile.CreateId("C:\\Chrome\\User Data", "Profile 1"),
            "New profile",
            "Profile 1",
            "C:\\Chrome\\User Data",
            false));

        Assert.Contains(nameof(MainViewModel.ProfileSearchText), changedProperties);
        Assert.Contains(nameof(MainViewModel.CanAddProfile), changedProperties);
        Assert.Contains(nameof(MainViewModel.ProfileAddButtonText), changedProperties);
        Assert.False(viewModel.CanAddProfile);
    }

    [Fact]
    public async Task Clear_search_clears_query_and_disables_clear_and_add_actions()
    {
        var viewModel = new MainViewModel
        {
            ProfileSearchText = "New profile"
        };
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        await viewModel.ClearProfileSearchAsync();

        Assert.Equal(string.Empty, viewModel.ProfileSearchText);
        Assert.False(viewModel.CanClearProfileSearch);
        Assert.False(viewModel.CanAddProfile);
        Assert.Contains(nameof(MainViewModel.ProfileSearchText), changedProperties);
        Assert.Contains(nameof(MainViewModel.CanClearProfileSearch), changedProperties);
        Assert.Contains(nameof(MainViewModel.ProfileAddButtonText), changedProperties);
    }

    [Fact]
    public async Task AddProfile_provisions_persists_reloads_and_selects_the_new_profile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        var userDataDirectory = Path.Combine(directory, "User Data");
        var chromeExecutablePath = Path.Combine(directory, "chrome.exe");
        var settingsPath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(userDataDirectory);
        File.WriteAllText(chromeExecutablePath, string.Empty);

        try
        {
            var viewModel = new MainViewModel(new SettingsStore(settingsPath))
            {
                ChromeExecutablePath = chromeExecutablePath,
                ChromeUserDataDirectory = userDataDirectory,
                ProfileSearchText = "  New profile  "
            };

            await viewModel.AddProfileAsync();

            var profile = Assert.Single(viewModel.Profiles);
            Assert.Equal("New profile", profile.Name);
            Assert.Equal("Profile 1", profile.DirectoryName);
            Assert.Equal(profile, viewModel.SelectedProfile);
            Assert.True(Directory.Exists(profile.ProfilePath));

            var settings = await new SettingsStore(settingsPath).LoadAsync();
            var managedProfile = Assert.Single(settings.ManagedProfiles!);
            Assert.Equal("New profile", managedProfile.Name);
            Assert.Equal("Profile 1", managedProfile.DirectoryName);
            Assert.Equal(userDataDirectory, managedProfile.UserDataDirectory);
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
