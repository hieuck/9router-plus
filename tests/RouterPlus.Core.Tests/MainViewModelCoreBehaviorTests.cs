using RouterPlus.App;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests;

public sealed class MainViewModelCoreBehaviorTests
{
    [Fact]
    public void ClearProviderFilter_resets_provider_and_unassigned_filters()
    {
        var connected = Profile("Connected");
        var unassigned = Profile("Unassigned");
        var viewModel = new MainViewModel();
        viewModel.Profiles.Add(connected);
        viewModel.Profiles.Add(unassigned);
        var connectedRow = new ProfileRowViewModel(connected, viewModel.Providers);
        connectedRow.UpdateConnections([new ProviderConnection("codex-1", ProviderKind.Codex, "Connected", 1, true)]);
        var unassignedRow = new ProfileRowViewModel(unassigned, viewModel.Providers);
        unassignedRow.UpdateConnections([]);
        viewModel.ProfileRows.Add(connectedRow);
        viewModel.ProfileRows.Add(unassignedRow);

        viewModel.ToggleProvider(ProviderKind.Codex);
        viewModel.ClearProviderFilter();

        Assert.False(viewModel.IsProviderFilterActive);
        Assert.False(viewModel.IsUnassignedProfileFilterActive);
        Assert.Empty(viewModel.SelectedProviderKinds);
        Assert.Empty(viewModel.ProviderFilterStates);
        Assert.Equal(new[] { connected, unassigned }, viewModel.FilteredProfiles);
    }

    [Fact]
    public void Exiting_multi_select_mode_clears_each_row_selection()
    {
        var viewModel = new MainViewModel(harnessProfiles: [Profile("Personal"), Profile("Work")]);
        viewModel.RefreshProfiles();
        viewModel.IsMultiSelectMode = true;
        foreach (var row in viewModel.ProfileRows)
        {
            row.IsSelected = true;
        }

        viewModel.IsMultiSelectMode = false;

        Assert.All(viewModel.ProfileRows, row => Assert.False(row.IsSelected));
        Assert.False(viewModel.HasSelectedProfiles);
        Assert.Equal("0 profiles đã chọn", viewModel.SelectedProfilesText);
    }

    [Fact]
    public void BatchProgressSummary_counts_completed_successes_and_failures()
    {
        var viewModel = new MainViewModel();
        var profiles = new[] { Profile("One"), Profile("Two"), Profile("Three"), Profile("Four") };
        viewModel.BatchProgressRows.Add(new BatchLoginProgressRow(profiles[0]) { State = BatchLoginState.Success });
        viewModel.BatchProgressRows.Add(new BatchLoginProgressRow(profiles[1]) { State = BatchLoginState.Failed });
        viewModel.BatchProgressRows.Add(new BatchLoginProgressRow(profiles[2]) { State = BatchLoginState.Skipped });
        viewModel.BatchProgressRows.Add(new BatchLoginProgressRow(profiles[3]) { State = BatchLoginState.InProgress });

        Assert.Equal("3/4 · 1 thành công · 1 lỗi", viewModel.BatchProgressSummary);
    }

    [Fact]
    public async Task SaveWindowPlacement_rejects_non_positive_dimensions_without_writing()
    {
        var directory = CreateTempDirectory();
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            var viewModel = new MainViewModel(new SettingsStore(settingsPath));

            await viewModel.SaveWindowPlacementAsync(10, 20, 0, 800);

            Assert.Null(viewModel.SavedWindowPlacement);
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task InitializeAsync_in_harness_mode_loads_synthetic_profiles_without_provider_sync()
    {
        var directory = CreateTempDirectory();
        try
        {
            var profiles = new[] { Profile("Personal"), Profile("Work") };
            var viewModel = new MainViewModel(
                new SettingsStore(Path.Combine(directory, "settings.json")),
                googleLoginVaultPaths: new GoogleAccountVaultPaths(directory),
                harnessProfiles: profiles);

            await viewModel.InitializeAsync();

            Assert.True(viewModel.IsInitialized);
            Assert.Equal(profiles, viewModel.Profiles);
            Assert.Same(profiles[0], viewModel.SelectedProfile);
            Assert.Equal("Harness mode: provider sync disabled.", viewModel.ConnectionStatusText);
            Assert.Contains("Đã đọc 2 Chrome profile.", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task Public_help_security_and_release_commands_use_fixed_links()
    {
        var launcher = new RecordingLinkLauncher();
        var viewModel = new MainViewModel(linkLauncher: launcher);

        await viewModel.OpenHelpAsync();
        await viewModel.OpenSecurityAsync();
        await viewModel.OpenReleasePageAsync();

        Assert.Equal(3, launcher.OpenedUris.Count);
        Assert.Equal(ApplicationLinks.HelpUri, launcher.OpenedUris[0]);
        Assert.Equal(ApplicationLinks.SecurityUri, launcher.OpenedUris[1]);
        Assert.Equal(ApplicationLinks.ReleaseUri, launcher.OpenedUris[2]);
    }

    private static ChromeProfile Profile(string name) => new(
        ChromeProfile.CreateId(@"C:\Chrome\User Data", name),
        name,
        name,
        @"C:\Chrome\User Data",
        false);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "RouterPlusMainViewModelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingLinkLauncher : IExternalLinkLauncher
    {
        public List<Uri> OpenedUris { get; } = [];

        public void Open(Uri uri) => OpenedUris.Add(uri);
    }
}
