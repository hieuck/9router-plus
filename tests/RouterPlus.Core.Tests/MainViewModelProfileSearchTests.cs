using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
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
    public void Selecting_unassigned_filter_shows_only_profiles_without_connections()
    {
        var viewModel = new MainViewModel();
        var unassigned = CreateProfile("Unassigned");
        var assigned = CreateProfile("Assigned");
        viewModel.Profiles.Add(unassigned);
        viewModel.Profiles.Add(assigned);
        var unassignedRow = new ProfileRowViewModel(unassigned, viewModel.Providers);
        var assignedRow = new ProfileRowViewModel(assigned, viewModel.Providers);
        unassignedRow.UpdateConnections(Array.Empty<ProviderConnection>());
        assignedRow.UpdateConnections(new[]
        {
            new ProviderConnection("codex-1", ProviderKind.Codex, "Assigned", 1, true)
        });
        viewModel.ProfileRows.Add(unassignedRow);
        viewModel.ProfileRows.Add(assignedRow);

        viewModel.ToggleUnassignedProfiles();

        Assert.Equal(new[] { unassigned }, viewModel.FilteredProfiles);
        Assert.True(viewModel.IsUnassignedProfileFilterActive);
    }

    [Fact]
    public void Unassigned_filter_excludes_profiles_with_unknown_provider_status()
    {
        var viewModel = new MainViewModel();
        var unknown = CreateProfile("Unknown");
        var unassigned = CreateProfile("Unassigned");
        viewModel.Profiles.Add(unknown);
        viewModel.Profiles.Add(unassigned);
        viewModel.ProfileRows.Add(new ProfileRowViewModel(unknown, viewModel.Providers));
        var unassignedRow = new ProfileRowViewModel(unassigned, viewModel.Providers);
        unassignedRow.UpdateConnections(Array.Empty<ProviderConnection>());
        viewModel.ProfileRows.Add(unassignedRow);

        viewModel.ToggleUnassignedProfiles();

        Assert.Equal(new[] { unassigned }, viewModel.FilteredProfiles);
    }

    [Fact]
    public void Selecting_provider_filter_clears_unassigned_filter()
    {
        var viewModel = new MainViewModel();
        var unassigned = CreateProfile("Unassigned");
        var assigned = CreateProfile("Assigned");
        viewModel.Profiles.Add(unassigned);
        viewModel.Profiles.Add(assigned);
        var unassignedRow = new ProfileRowViewModel(unassigned, viewModel.Providers);
        var assignedRow = new ProfileRowViewModel(assigned, viewModel.Providers);
        unassignedRow.UpdateConnections(Array.Empty<ProviderConnection>());
        assignedRow.UpdateConnections(new[]
        {
            new ProviderConnection("codex-1", ProviderKind.Codex, "Assigned", 1, true)
        });
        viewModel.ProfileRows.Add(unassignedRow);
        viewModel.ProfileRows.Add(assignedRow);

        viewModel.ToggleUnassignedProfiles();
        viewModel.ToggleProvider(ProviderKind.Codex);

        Assert.Equal(new[] { assigned }, viewModel.FilteredProfiles);
        Assert.False(viewModel.IsUnassignedProfileFilterActive);
    }

    private static ChromeProfile CreateProfile(string name) => new(
        ChromeProfile.CreateId("C:\\Chrome\\User Data", name),
        name,
        name,
        "C:\\Chrome\\User Data",
        false);

    [Fact]
    public void Selecting_a_profile_updates_select_all_toolbar_state()
    {
        var profile = CreateProfile("Personal");
        var viewModel = new MainViewModel(harnessProfiles: new[] { profile });
        viewModel.RefreshProfiles();
        var row = Assert.Single(viewModel.ProfileRows);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        row.IsSelected = true;

        Assert.True(viewModel.AreAllProfilesSelected);
        Assert.Equal("☐  Bỏ chọn tất cả", viewModel.SelectAllButtonText);
        Assert.Contains(nameof(MainViewModel.AreAllProfilesSelected), changedProperties);
        Assert.Contains(nameof(MainViewModel.SelectAllButtonText), changedProperties);
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
            Assert.Equal("Profile New profile", profile.DirectoryName);
            Assert.Equal(profile, viewModel.SelectedProfile);
            Assert.True(Directory.Exists(profile.ProfilePath));

            var settings = await new SettingsStore(settingsPath).LoadAsync();
            var managedProfile = Assert.Single(settings.ManagedProfiles!);
            Assert.Equal("New profile", managedProfile.Name);
            Assert.Equal("Profile New profile", managedProfile.DirectoryName);
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
