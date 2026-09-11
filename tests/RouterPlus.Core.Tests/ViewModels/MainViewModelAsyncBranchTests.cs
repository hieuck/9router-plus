using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class MainViewModelAsyncBranchTests
{
    [Fact]
    public async Task InitializeAsync_in_harness_mode_loads_profiles_without_live_chrome_or_router()
    {
        // Arrange
        var profile = Profile();
        var settingsPath = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"), "settings.json");
        var viewModel = new MainViewModel(
            settingsStore: new SettingsStore(settingsPath),
            googleLoginVaultStore: new RememberedVaultStore(null),
            harnessProfiles: [profile]);

        // Act
        await viewModel.InitializeAsync();

        // Assert
        Assert.True(viewModel.IsInitialized);
        var loadedProfile = Assert.Single(viewModel.Profiles);
        Assert.Equal(profile.Id, loadedProfile.Id);
        Assert.Equal("Harness mode: provider sync disabled.", viewModel.ConnectionStatusText);
        Assert.Contains("1 Chrome profile", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunGoogleAutoLoginDirectAsync_with_remembered_credentials_reports_success()
    {
        // Arrange
        var profile = Profile();
        var credential = Credential(profile);
        var automationCalls = 0;
        var viewModel = new MainViewModel(
            googleLoginVaultStore: new RememberedVaultStore(new GoogleAccountVault([credential])),
            harnessProfiles: [profile],
            googleLoginAutomation: (actualProfile, actualCredential, _) =>
            {
                automationCalls++;
                Assert.Equal(profile.Id, actualProfile.Id);
                Assert.Equal(credential.Email, actualCredential.Email);
                return Task.FromResult(GoogleLoginResult.Success());
            });
        viewModel.SelectedProfile = profile;

        // Act
        await viewModel.RunGoogleAutoLoginDirectAsync();

        // Assert
        Assert.Equal(1, automationCalls);
        Assert.Contains("Đăng nhập Google thành công", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunGoogleAutoLoginDirectAsync_without_remembered_vault_stops_before_automation()
    {
        // Arrange
        var profile = Profile();
        var automationCalls = 0;
        var viewModel = new MainViewModel(
            googleLoginVaultStore: new RememberedVaultStore(null),
            harnessProfiles: [profile],
            googleLoginAutomation: (_, _, _) =>
            {
                automationCalls++;
                return Task.FromResult(GoogleLoginResult.Success());
            });
        viewModel.SelectedProfile = profile;

        // Act
        await viewModel.RunGoogleAutoLoginDirectAsync();

        // Assert
        Assert.Equal(0, automationCalls);
        Assert.Contains("Vault chưa được mở khóa", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunGoogleAutoLoginDirectAsync_without_matching_credential_stops_before_automation()
    {
        // Arrange
        var profile = Profile();
        var viewModel = new MainViewModel(
            googleLoginVaultStore: new RememberedVaultStore(new GoogleAccountVault()),
            harnessProfiles: [profile],
            googleLoginAutomation: (_, _, _) =>
            {
                Assert.Fail("Automation must not run without a matching credential.");
                return Task.FromResult(GoogleLoginResult.Success());
            });
        viewModel.SelectedProfile = profile;

        // Act
        await viewModel.RunGoogleAutoLoginDirectAsync();

        // Assert
        Assert.Contains("Không tìm thấy thông tin đăng nhập", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunGoogleAutoLoginDirectAsync_maps_manual_intervention_result_to_status()
    {
        // Arrange
        var profile = Profile();
        var viewModel = new MainViewModel(
            googleLoginVaultStore: new RememberedVaultStore(new GoogleAccountVault([Credential(profile)])),
            harnessProfiles: [profile],
            googleLoginAutomation: (_, _, _) =>
                Task.FromResult(GoogleLoginResult.ManualInterventionRequired("Complete the challenge")));
        viewModel.SelectedProfile = profile;

        // Act
        await viewModel.RunGoogleAutoLoginDirectAsync();

        // Assert
        Assert.Contains("Cần can thiệp thủ công", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("Complete the challenge", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunGoogleAutoLoginDirectAsync_without_selected_profile_reports_validation_status()
    {
        // Arrange
        var viewModel = new MainViewModel(harnessProfiles: []);

        // Act
        await viewModel.RunGoogleAutoLoginDirectAsync();

        // Assert
        Assert.Equal("Hãy chọn Chrome profile trước.", viewModel.StatusText);
    }

    private static ChromeProfile Profile() => new(
        "profile-id",
        "Work",
        "Default",
        Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
        IsDefault: true);

    private static GoogleLoginCredential Credential(ChromeProfile profile) =>
        new(profile.Id, "user@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP");

    private sealed class RememberedVaultStore(GoogleAccountVault? vault) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new Session(vault ?? new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new Session(vault ?? new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession?>(vault is null ? null : new Session(vault));

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private sealed class Session(GoogleAccountVault initialVault) : GoogleAccountVaultSession
        {
            public string VaultId => "synthetic-vault";
            public GoogleAccountVault Vault { get; private set; } = initialVault;
            public void Replace(GoogleAccountVault vault) => Vault = vault;
            public Task RememberAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveRememberedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
