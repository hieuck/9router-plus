using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleAutoLoginViewModelTests
{
    [Fact]
    public async Task New_record_defaults_email_to_profile_name()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);

        Assert.Equal("test.user@example.com", viewModel.Email);
    }

    [Fact]
    public async Task Invalid_email_blocks_both_save_and_auto_login()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);

        await Assert.ThrowsAsync<FormatException>(() =>
            viewModel.SaveInformationAsync("not-an-email", "password", "JBSWY3DPEHPK3PXP", CancellationToken.None));

        await Assert.ThrowsAsync<FormatException>(() =>
            viewModel.AutoLoginAsync("not-an-email", "password", "JBSWY3DPEHPK3PXP", CancellationToken.None));
    }

    [Fact]
    public async Task SaveInformationAsync_persists_email_password_totp()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        await viewModel.SaveInformationAsync("user@example.com", "secret-password", "JBSWY3DPEHPK3PXP", CancellationToken.None);

        var savedCredential = vaultStore.SavedVault?.Find("profile-1");
        Assert.NotNull(savedCredential);
        Assert.Equal("user@example.com", savedCredential.Email);
        Assert.Equal("secret-password", savedCredential.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", savedCredential.TotpSecret);
    }

    [Fact]
    public async Task AutoLoginAsync_persists_changed_email_but_not_password_totp()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // First save
        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        await viewModel.SaveInformationAsync("old@example.com", "old-password", "OLDSECRET", CancellationToken.None);

        // Auto login with different credentials
        await viewModel.AutoLoginAsync("new@example.com", "new-password", "NEWSECRET", CancellationToken.None);

        var savedCredential = vaultStore.SavedVault?.Find("profile-1");
        Assert.NotNull(savedCredential);
        Assert.Equal("new@example.com", savedCredential.Email); // Email updated
        Assert.Equal("old-password", savedCredential.Password); // Password NOT updated
        Assert.Equal("OLDSECRET", savedCredential.TotpSecret); // TOTP NOT updated
    }

    [Fact]
    public async Task AutoLoginAsync_calls_automation_delegate_with_current_fields()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        GoogleLoginCredential? receivedCredential = null;
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, (p, cred, ct) =>
        {
            receivedCredential = cred;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        await viewModel.AutoLoginAsync("user@example.com", "test-password", "TESTSECRET", CancellationToken.None);

        Assert.NotNull(receivedCredential);
        Assert.Equal("user@example.com", receivedCredential.Email);
        Assert.Equal("test-password", receivedCredential.Password);
        Assert.Equal("TESTSECRET", receivedCredential.TotpSecret);
    }

    [Fact]
    public async Task Wrong_vault_password_returns_safe_status()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore { ThrowOnWrongPassword = true };
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            viewModel.UnlockVaultAsync("wrong-password", false, CancellationToken.None));

        Assert.False(viewModel.IsVaultUnlocked);
    }

    [Fact]
    public async Task Remember_on_device_is_passed_to_store()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", true, CancellationToken.None);

        Assert.True(vaultStore.RememberCalled);
    }

    [Fact]
    public async Task LockVaultAsync_clears_IsVaultUnlocked()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        Assert.True(viewModel.IsVaultUnlocked);

        await viewModel.LockVaultAsync(CancellationToken.None);
        Assert.False(viewModel.IsVaultUnlocked);
    }

    [Fact]
    public async Task StatusText_never_contains_password_or_totp()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        await viewModel.SaveInformationAsync("user@example.com", "secret-password-123", "SECRETTOTP", CancellationToken.None);

        Assert.DoesNotContain("secret-password-123", viewModel.StatusText);
        Assert.DoesNotContain("SECRETTOTP", viewModel.StatusText);
    }

    [Fact]
    public async Task ImportAsync_calls_replacement_operation()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        await viewModel.ImportAsync(@"C:\import.gvault", "import-password", CancellationToken.None);

        Assert.True(vaultStore.ImportCalled);
        Assert.Equal(@"C:\import.gvault", vaultStore.ImportSourcePath);
        Assert.Equal("import-password", vaultStore.ImportSourcePassword);
    }

    [Fact]
    public async Task ExportAsync_uses_selected_destination()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);
        await viewModel.ExportAsync(@"C:\export.gvault", "export-password", CancellationToken.None);

        Assert.True(vaultStore.ExportCalled);
        Assert.Equal(@"C:\export.gvault", vaultStore.ExportDestinationPath);
        Assert.Equal("export-password", vaultStore.ExportPassword);
    }

    [Fact]
    public async Task AutoLoginAsync_new_profile_does_not_persist_password_totp()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // No existing record - new profile
        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);

        // Auto login with credentials
        await viewModel.AutoLoginAsync("user@example.com", "new-password", "NEWSECRET", CancellationToken.None);

        // Should NOT persist any credentials for new profile
        var savedCredential = vaultStore.SavedVault?.Find("profile-1");
        Assert.Null(savedCredential);
    }

    private static Task<GoogleLoginResult> FakeAutomation(ChromeProfile profile, GoogleLoginCredential credential, CancellationToken ct)
    {
        return Task.FromResult(GoogleLoginResult.Success());
    }

    private sealed class FakeVaultStore : IGoogleLoginVaultStore
    {
        private FakeSession? _currentSession;
        public GoogleLoginVault? SavedVault { get; private set; }
        public bool ThrowOnWrongPassword { get; set; }
        public bool RememberCalled { get; private set; }
        public bool ImportCalled { get; private set; }
        public string? ImportSourcePath { get; private set; }
        public string? ImportSourcePassword { get; private set; }
        public bool ExportCalled { get; private set; }
        public string? ExportDestinationPath { get; private set; }
        public string? ExportPassword { get; private set; }

        public Task<GoogleLoginVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrongPassword)
                throw new System.Security.Cryptography.CryptographicException("Invalid password");

            _currentSession = new FakeSession(this, new GoogleLoginVault());
            return Task.FromResult<GoogleLoginVaultSession>(_currentSession);
        }

        public Task<GoogleLoginVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrongPassword)
                throw new System.Security.Cryptography.CryptographicException("Invalid password");

            _currentSession = new FakeSession(this, new GoogleLoginVault());
            return Task.FromResult<GoogleLoginVaultSession>(_currentSession);
        }

        public Task<GoogleLoginVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<GoogleLoginVaultSession?>(null);
        }

        public Task SaveAsync(GoogleLoginVaultSession session, CancellationToken cancellationToken = default)
        {
            SavedVault = session.Vault;
            return Task.CompletedTask;
        }

        public Task ExportAsync(GoogleLoginVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default)
        {
            ExportCalled = true;
            ExportDestinationPath = destinationPath;
            ExportPassword = exportPassword;
            return Task.CompletedTask;
        }

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default)
        {
            ImportCalled = true;
            ImportSourcePath = sourcePath;
            ImportSourcePassword = sourcePassword;
            return Task.CompletedTask;
        }

        private sealed class FakeSession : GoogleLoginVaultSession
        {
            private readonly FakeVaultStore _store;
            private GoogleLoginVault _vault;

            public FakeSession(FakeVaultStore store, GoogleLoginVault vault)
            {
                _store = store;
                _vault = vault;
            }

            public string VaultId => "test-vault-id";
            public GoogleLoginVault Vault => _vault;

            public void Replace(GoogleLoginVault vault)
            {
                _vault = vault;
            }

            public Task RememberAsync(CancellationToken cancellationToken = default)
            {
                _store.RememberCalled = true;
                return Task.CompletedTask;
            }

            public Task RemoveRememberedAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
