using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Core.Tests;

public sealed class GoogleAutoLoginViewModelAsyncBranchTests
{
    [Fact]
    public async Task UnlockVaultAsync_loads_legacy_name_credentials_and_remembers_session()
    {
        // Arrange
        var profile = Profile();
        var legacyCredential = new GoogleLoginCredential(profile.Name, "legacy@example.com", "legacy-password", "LEGACYTOTP");
        var store = new FakeVaultStore(new GoogleAccountVault([legacyCredential]));
        var viewModel = new GoogleAutoLoginViewModel(profile, store, SuccessAutomation);

        // Act
        await viewModel.UnlockVaultAsync("synthetic-vault-password", remember: true, CancellationToken.None);

        // Assert
        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal("legacy@example.com", viewModel.Email);
        Assert.Equal("legacy-password", viewModel.Password);
        Assert.Equal("LEGACYTOTP", viewModel.TotpSecret);
        Assert.True(store.RememberCalled);
        Assert.Equal("Vault unlocked successfully", viewModel.StatusText);
    }

    [Fact]
    public async Task SaveInformationAsync_without_totp_uses_valid_placeholder()
    {
        // Arrange
        var profile = Profile();
        var store = new FakeVaultStore(null);
        var viewModel = new GoogleAutoLoginViewModel(profile, store, SuccessAutomation);
        await viewModel.UnlockVaultAsync("synthetic-vault-password", remember: false, CancellationToken.None);

        // Act
        await viewModel.SaveInformationAsync("user@example.com", "synthetic-password", string.Empty, CancellationToken.None);

        // Assert
        var saved = store.SavedVault?.Find(profile.Id);
        Assert.NotNull(saved);
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAA", saved.TotpSecret);
        Assert.Equal("Information saved successfully", viewModel.StatusText);
    }

    [Fact]
    public async Task AutoLoginAsync_maps_non_success_result_and_keeps_busy_state_clear()
    {
        // Arrange
        var profile = Profile();
        var store = new FakeVaultStore(null);
        var viewModel = new GoogleAutoLoginViewModel(profile, store, (_, _, _) =>
            Task.FromResult(GoogleLoginResult.InvalidCredentials()));
        await viewModel.UnlockVaultAsync("synthetic-vault-password", remember: false, CancellationToken.None);

        // Act
        var result = await viewModel.AutoLoginAsync("user@example.com", "synthetic-password", string.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
        Assert.Equal("Invalid credentials", viewModel.StatusText);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanAutoLogin);
    }

    [Fact]
    public async Task AutoLoginAsync_propagates_automation_failure_and_sanitizes_status()
    {
        // Arrange
        var profile = Profile();
        var store = new FakeVaultStore(null);
        var viewModel = new GoogleAutoLoginViewModel(profile, store, (_, _, _) =>
            throw new InvalidOperationException("synthetic automation failure"));
        await viewModel.UnlockVaultAsync("synthetic-vault-password", remember: false, CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.AutoLoginAsync("user@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP", CancellationToken.None));

        // Assert
        Assert.Equal("synthetic automation failure", exception.Message);
        Assert.Contains("synthetic automation failure", viewModel.StatusText, StringComparison.Ordinal);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LockVaultAsync_clears_sensitive_fields_and_remove_remembered_unlock_delegates()
    {
        // Arrange
        var profile = Profile();
        var store = new FakeVaultStore(null);
        var viewModel = new GoogleAutoLoginViewModel(profile, store, SuccessAutomation);
        await viewModel.UnlockVaultAsync("synthetic-vault-password", remember: true, CancellationToken.None);
        await viewModel.SaveInformationAsync("user@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP", CancellationToken.None);

        // Act
        await viewModel.RemoveRememberedUnlockAsync(CancellationToken.None);
        await viewModel.LockVaultAsync(CancellationToken.None);

        // Assert
        Assert.True(store.RemoveRememberedCalled);
        Assert.False(viewModel.IsVaultUnlocked);
        Assert.Equal(profile.Name, viewModel.Email);
        Assert.Equal("Vault locked", viewModel.StatusText);
    }

    private static ChromeProfile Profile() => new(
        "profile-id",
        "Work",
        "Default",
        Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N")),
        IsDefault: true);

    private static Task<GoogleLoginResult> SuccessAutomation(ChromeProfile profile, GoogleLoginCredential credential, CancellationToken cancellationToken) =>
        Task.FromResult(GoogleLoginResult.Success());

    private sealed class FakeVaultStore(GoogleAccountVault? initialVault) : IGoogleAccountVaultStore
    {
        public GoogleAccountVault? SavedVault { get; private set; }
        public bool RememberCalled { get; private set; }
        public bool RemoveRememberedCalled { get; private set; }

        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new Session(this, initialVault ?? new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession>(new Session(this, initialVault ?? new GoogleAccountVault()));

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession?>(null);

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default)
        {
            SavedVault = session.Vault;
            return Task.CompletedTask;
        }

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) => Task.CompletedTask;

        private sealed class Session(FakeVaultStore store, GoogleAccountVault initialVault) : GoogleAccountVaultSession
        {
            public string VaultId => "synthetic-vault";
            public GoogleAccountVault Vault { get; private set; } = initialVault;
            public void Replace(GoogleAccountVault vault) => Vault = vault;
            public Task RememberAsync(CancellationToken cancellationToken = default)
            {
                store.RememberCalled = true;
                return Task.CompletedTask;
            }
            public Task RemoveRememberedAsync(CancellationToken cancellationToken = default)
            {
                store.RemoveRememberedCalled = true;
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
