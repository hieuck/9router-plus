using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelBehaviorTests
{
    [Fact]
    public void RefreshProfiles_in_harness_mode_rebuilds_rows_and_restores_selected_profile()
    {
        var profiles = new[]
        {
            CreateProfile("Personal", "Default"),
            CreateProfile("Work", "Profile 1")
        };
        var viewModel = CreateViewModel(profiles);

        viewModel.RefreshProfiles();
        viewModel.SelectedProfile = profiles[1];
        viewModel.ProfileSearchText = "Work";
        viewModel.RefreshProfiles();

        Assert.Equal(profiles, viewModel.Profiles);
        Assert.Equal(new[] { profiles[1] }, viewModel.FilteredProfiles);
        Assert.Equal(profiles[1], viewModel.SelectedProfile);
        Assert.Equal(profiles[1], viewModel.SelectedProfileRow?.Profile);
        Assert.Equal(1, viewModel.SelectedProfileRow?.DisplayIndex);
        Assert.Equal(2, viewModel.ProfileRows.Count);
    }

    [Fact]
    public async Task InitializeAsync_in_harness_mode_loads_synthetic_profiles_without_provider_sync()
    {
        var directory = CreateTempDirectory();
        try
        {
            var profile = CreateProfile("Synthetic", "Default", directory);
            var settingsStore = new SettingsStore(Path.Combine(directory, "settings.json"));
            await settingsStore.SaveAsync(new RouterSettings(
                DashboardBaseUrl: "http://synthetic-router",
                FontScale: 1.25d,
                UseLightTheme: false,
                UseOriginalProfileForAutoLogin: true));
            var viewModel = new MainViewModel(
                settingsStore,
                googleLoginVaultStore: new FakeVaultStore(null),
                googleLoginVaultPaths: new GoogleAccountVaultPaths(directory),
                harnessProfiles: new[] { profile },
                secretVault: new NoOpSecretVault());

            await viewModel.InitializeAsync();

            Assert.Single(viewModel.Profiles);
            Assert.Equal(profile, viewModel.SelectedProfile);
            Assert.Equal("http://synthetic-router", viewModel.DashboardBaseUrl);
            Assert.Equal(1.25d, viewModel.FontScale);
            Assert.False(viewModel.UseLightTheme);
            Assert.True(viewModel.UseOriginalProfileForAutoLogin);
            Assert.Equal("Harness mode: provider sync disabled.", viewModel.ConnectionStatusText);
            Assert.Contains("Chrome profile", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CheckProfileHealthCommand_maps_successful_login_to_healthy_status()
    {
        var directory = CreateTempDirectory();
        try
        {
            var profile = CreateProfile("Work", "Default", directory);
            Directory.CreateDirectory(profile.ProfilePath);
            File.WriteAllText(Path.Combine(directory, "Local State"), "synthetic");
            File.WriteAllText(Path.Combine(profile.ProfilePath, "Preferences"), "synthetic");
            var credential = new GoogleLoginCredential(profile.Id, "synthetic@example.test", "password", "totp");
            var vaultStore = new FakeVaultStore(new GoogleAccountVault(new[] { credential }));
            var automationCalls = 0;
            var viewModel = new MainViewModel(
                googleLoginVaultStore: vaultStore,
                googleLoginAutomation: (_, actual, _) =>
                {
                    Assert.Equal(credential, actual);
                    automationCalls++;
                    return Task.FromResult(GoogleLoginResult.Success());
                },
                googleLoginVaultPaths: new GoogleAccountVaultPaths(directory),
                harnessProfiles: new[] { profile },
                secretVault: new NoOpSecretVault());
            await viewModel.InitializeAsync();
            var row = Assert.Single(viewModel.ProfileRows);

            viewModel.CheckProfileHealthCommand.Execute(row);
            await WaitForAsync(() => row.HealthStatus is not null);

            Assert.Equal(HealthLevel.Healthy, row.HealthStatus?.Level);
            Assert.False(row.IsCheckingHealth);
            Assert.Equal(1, automationCalls);
            Assert.Contains("successful", row.HealthStatus?.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task SaveWindowPlacementAsync_ignores_invalid_dimensions_after_valid_save()
    {
        var directory = CreateTempDirectory();
        try
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            var store = new SettingsStore(settingsPath);
            var viewModel = new MainViewModel(
                store,
                harnessProfiles: Array.Empty<ChromeProfile>(),
                secretVault: new NoOpSecretVault());

            await viewModel.SaveWindowPlacementAsync(240d, 130d, 1320d, 840d);
            var saved = new MainViewModel.WindowPlacement(240d, 130d, 1320d, 840d);
            await viewModel.SaveWindowPlacementAsync(double.NaN, 0d, -1d, 0d);

            Assert.Equal(saved, viewModel.SavedWindowPlacement);
            var persisted = await store.LoadAsync();
            Assert.Equal(240d, persisted.WindowLeft);
            Assert.Equal(130d, persisted.WindowTop);
            Assert.Equal(1320d, persisted.WindowWidth);
            Assert.Equal(840d, persisted.WindowHeight);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task CheckProfileHealthCommand_reports_missing_vault_without_running_automation()
    {
        var directory = CreateTempDirectory();
        try
        {
            var profile = CreateProfile("Work", "Default", directory);
            var automationCalls = 0;
            var viewModel = new MainViewModel(
                googleLoginVaultStore: new FakeVaultStore(null),
                googleLoginAutomation: (_, _, _) =>
                {
                    automationCalls++;
                    return Task.FromResult(GoogleLoginResult.Success());
                },
                googleLoginVaultPaths: new GoogleAccountVaultPaths(directory),
                harnessProfiles: new[] { profile },
                secretVault: new NoOpSecretVault());
            await viewModel.InitializeAsync();
            var row = Assert.Single(viewModel.ProfileRows);

            viewModel.CheckProfileHealthCommand.Execute(row);
            await WaitForAsync(() => !row.IsCheckingHealth && viewModel.StatusText.Contains("Vault not unlocked", StringComparison.Ordinal));

            Assert.Equal(0, automationCalls);
            Assert.Null(row.HealthStatus);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static MainViewModel CreateViewModel(IReadOnlyList<ChromeProfile> profiles) =>
        new(harnessProfiles: profiles, secretVault: new NoOpSecretVault());

    [Fact]
    public async Task ConnectOpenRouterOAuthAsync_reports_flow_failure_and_resets_workflow_state()
    {
        var profile = CreateProfile("Work", "Default");
        var viewModel = new MainViewModel(harnessProfiles: new[] { profile }, secretVault: new NoOpSecretVault());
        viewModel.RefreshProfiles();
        viewModel.OpenRouterPkceFlow = _ => Task.FromResult(OpenRouterPkceResult.Failed("synthetic OAuth failure"));

        var connected = await viewModel.ConnectOpenRouterOAuthAsync();

        Assert.False(connected);
        Assert.False(viewModel.IsWorkflowInProgress);
        Assert.True(viewModel.ConnectOpenRouterOAuthCommand.CanExecute(null));
        Assert.Contains("synthetic OAuth failure", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoGetKeyAsync_reports_missing_vault_credential_without_running_key_flow()
    {
        var profile = CreateProfile("Work", "Default");
        var viewModel = new MainViewModel(harnessProfiles: new[] { profile }, secretVault: new NoOpSecretVault());
        viewModel.RefreshProfiles();
        var flowCalled = false;
        viewModel.AutoGetKeyCredentials = (_, _) => Task.FromResult<RouterPlus.Core.Security.GoogleLoginCredential?>(null);
        viewModel.OpenRouterKeyFlow = (_, _, _) =>
        {
            flowCalled = true;
            return Task.FromResult(new OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult(true, "synthetic-key", null));
        };

        var added = await viewModel.AutoGetKeyAsync(RouterPlus.Core.Providers.ProviderKind.OpenRouter);

        Assert.False(added);
        Assert.False(flowCalled);
        Assert.Contains("Không có thông tin Google", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToggleSelectAllCommand_enables_multi_select_and_toggles_all_rows()
    {
        var profiles = new[]
        {
            CreateProfile("Personal", "Default"),
            CreateProfile("Work", "Profile 1")
        };
        var viewModel = CreateViewModel(profiles);
        viewModel.RefreshProfiles();

        viewModel.ToggleSelectAllCommand.Execute(null);

        Assert.True(viewModel.IsMultiSelectMode);
        Assert.True(viewModel.HasSelectedProfiles);
        Assert.True(viewModel.AreAllProfilesSelected);
        Assert.Equal("2 profiles đã chọn", viewModel.SelectedProfilesText);

        viewModel.IsMultiSelectMode = false;

        Assert.False(viewModel.HasSelectedProfiles);
        Assert.False(viewModel.AreAllProfilesSelected);
        Assert.All(viewModel.ProfileRows, row => Assert.False(row.IsSelected));
        Assert.Equal("0 profiles đã chọn", viewModel.SelectedProfilesText);
    }

    [Theory]
    [InlineData(0.1, 0.9, "90%")]
    [InlineData(9.0, 1.4, "140%")]
    public void FontScale_clamps_to_supported_range(double requested, double expected, string expectedLabel)
    {
        var viewModel = new MainViewModel(secretVault: new NoOpSecretVault());

        viewModel.FontScale = requested;

        Assert.Equal(expected, viewModel.FontScale);
        Assert.Equal(expectedLabel, viewModel.FontScaleLabel);
    }

    private static ChromeProfile CreateProfile(string name, string directoryName, string? userDataDirectory = null) =>
        new(
            ChromeProfile.CreateId(userDataDirectory ?? @"C:\Chrome\User Data", directoryName),
            name,
            directoryName,
            userDataDirectory ?? @"C:\Chrome\User Data",
            directoryName == "Default");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusMainViewModelTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoOpSecretVault : ISecretVault
    {
        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeVaultStore(GoogleAccountVault? vault) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new FakeSession(vault ?? new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new FakeSession(vault ?? new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession?>(vault is null ? null : new FakeSession(vault));

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private sealed class FakeSession(GoogleAccountVault vault) : GoogleAccountVaultSession
        {
            public string VaultId => "synthetic-vault";
            public GoogleAccountVault Vault => vault;
            public void Replace(GoogleAccountVault replacement) { }
            public Task RememberAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveRememberedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
