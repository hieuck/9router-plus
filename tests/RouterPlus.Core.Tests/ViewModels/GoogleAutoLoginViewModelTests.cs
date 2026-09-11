using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using Xunit;

namespace RouterPlus.Core.Tests.ViewModels;

public sealed class GoogleAutoLoginViewModelTests
{
    [Fact]
    public void Remembered_vault_loads_credentials_by_profile_id()
    {
        // Arrange
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var rememberedCredential = new GoogleLoginCredential("profile-1", "remembered@example.com", "remembered-password", "REMEMBEREDTOTP");
        var vaultStore = new FakeVaultStore
        {
            RememberedVault = new GoogleAccountVault(new[] { rememberedCredential })
        };

        // Act
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // Assert
        Assert.True(vaultStore.RememberedUnlockCalled);
        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal("Vault unlocked from remembered device", viewModel.StatusText);
        Assert.Equal("remembered@example.com", viewModel.Email);
        Assert.Equal("remembered-password", viewModel.Password);
        Assert.Equal("REMEMBEREDTOTP", viewModel.TotpSecret);
    }

    [Fact]
    public void Remembered_vault_loads_legacy_credentials_by_profile_name()
    {
        // Arrange
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var rememberedCredential = new GoogleLoginCredential("test.user@example.com", "legacy@example.com", "legacy-password", "LEGACYTOTP");
        var vaultStore = new FakeVaultStore
        {
            RememberedVault = new GoogleAccountVault(new[] { rememberedCredential })
        };

        // Act
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // Assert
        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal("legacy@example.com", viewModel.Email);
        Assert.Equal("legacy-password", viewModel.Password);
        Assert.Equal("LEGACYTOTP", viewModel.TotpSecret);
    }

    [Fact]
    public void Remembered_vault_without_profile_credentials_stays_unlocked_without_loading_fields()
    {
        // Arrange
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var otherCredential = new GoogleLoginCredential("other-profile", "other@example.com", "other-password", "OTHERTOTP");
        var vaultStore = new FakeVaultStore
        {
            RememberedVault = new GoogleAccountVault(new[] { otherCredential })
        };

        // Act
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // Assert
        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal("Vault unlocked from remembered device", viewModel.StatusText);
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.TotpSecret);
    }

    [Fact]
    public void Missing_remembered_vault_leaves_view_model_locked()
    {
        // Arrange
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();

        // Act
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // Assert
        Assert.True(vaultStore.RememberedUnlockCalled);
        Assert.False(viewModel.IsVaultUnlocked);
        Assert.Equal(string.Empty, viewModel.StatusText);
    }

    [Fact]
    public void Remembered_vault_error_is_suppressed_and_leaves_view_model_locked()
    {
        // Arrange
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore
        {
            RememberedUnlockException = new System.Security.Cryptography.CryptographicException("remembered key is invalid")
        };

        // Act
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // Assert
        Assert.True(vaultStore.RememberedUnlockCalled);
        Assert.False(viewModel.IsVaultUnlocked);
        Assert.Equal(string.Empty, viewModel.StatusText);
    }

    [Fact]
    public async Task Vault_unlock_error_reports_safe_status_and_resets_busy_state()
    {
        // Arrange
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore { ThrowOnWrongPassword = true };
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        // Act
        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
            viewModel.UnlockVaultAsync("wrong-password", false, CancellationToken.None));

        // Assert
        Assert.False(viewModel.IsVaultUnlocked);
        Assert.False(viewModel.IsBusy);
        Assert.Equal("Failed to unlock vault: Cryptographic operation failed", viewModel.StatusText);
    }

    [Fact]
    public async Task Constructor_auto_unlocks_remembered_vault_and_loads_profile_credentials()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore
        {
            RememberedVault = new GoogleAccountVault(new[]
            {
                new GoogleLoginCredential("profile-1", "remembered@example.com", "synthetic-password", "SYNTHETIC-TOTP")
            })
        };

        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);
        await WaitForAsync(() => viewModel.IsVaultUnlocked);

        Assert.Equal(1, vaultStore.TryOpenRememberedCallCount);
        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal("Vault unlocked from remembered device", viewModel.StatusText);
        Assert.Equal("remembered@example.com", viewModel.Email);
        Assert.Equal("synthetic-password", viewModel.Password);
        Assert.Equal("SYNTHETIC-TOTP", viewModel.TotpSecret);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_auto_unlock_uses_profile_name_fallback_for_legacy_credentials()
    {
        var profile = new ChromeProfile("profile-1", "legacy-profile", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore
        {
            RememberedVault = new GoogleAccountVault(new[]
            {
                new GoogleLoginCredential("legacy-profile", "legacy@example.com", "synthetic-password", "SYNTHETIC-TOTP")
            })
        };

        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);
        await WaitForAsync(() => viewModel.IsVaultUnlocked);

        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal("legacy@example.com", viewModel.Email);
        Assert.Equal("synthetic-password", viewModel.Password);
        Assert.Equal("SYNTHETIC-TOTP", viewModel.TotpSecret);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_auto_unlocks_remembered_empty_vault_without_credentials()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore
        {
            RememberedVault = new GoogleAccountVault()
        };

        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);
        await WaitForAsync(() => viewModel.IsVaultUnlocked);

        Assert.True(viewModel.IsVaultUnlocked);
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.TotpSecret);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_stays_locked_when_no_remembered_vault_is_available()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();

        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        Assert.Equal(1, vaultStore.TryOpenRememberedCallCount);
        Assert.False(viewModel.IsVaultUnlocked);
        Assert.Equal(string.Empty, viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task Constructor_silently_ignores_remembered_unlock_failure()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore
        {
            RememberedUnlockException = new System.Security.Cryptography.CryptographicException("synthetic unlock failure")
        };

        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        Assert.False(viewModel.IsVaultUnlocked);
        Assert.Equal(string.Empty, viewModel.StatusText);

        await viewModel.DisposeAsync();
    }

    [Fact]
    public async Task New_record_leaves_email_empty_until_saved()
    {
        var profile = new ChromeProfile("profile-1", "test.user@example.com", "Default", @"C:\Users\Test\AppData\Local\Google\Chrome\User Data", true);
        var vaultStore = new FakeVaultStore();
        var viewModel = new GoogleAutoLoginViewModel(profile, vaultStore, FakeAutomation);

        await viewModel.UnlockVaultAsync("vault-password", false, CancellationToken.None);

        Assert.Equal(string.Empty, viewModel.Email);
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

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var timeout = DateTime.UtcNow.AddSeconds(1);
        while (!predicate() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(predicate());
    }

    private sealed class FakeVaultStore : IGoogleAccountVaultStore
    {
        private FakeSession? _currentSession;
        public GoogleAccountVault? SavedVault { get; private set; }
        public bool ThrowOnWrongPassword { get; set; }
        public GoogleAccountVault? RememberedVault { get; set; }
        public Exception? RememberedUnlockException { get; set; }
        public bool RememberedUnlockCalled { get; private set; }
        public int TryOpenRememberedCallCount { get; private set; }
        public bool RememberCalled { get; private set; }
        public bool ImportCalled { get; private set; }
        public string? ImportSourcePath { get; private set; }
        public string? ImportSourcePassword { get; private set; }
        public bool ExportCalled { get; private set; }
        public string? ExportDestinationPath { get; private set; }
        public string? ExportPassword { get; private set; }

        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrongPassword)
                throw new System.Security.Cryptography.CryptographicException("Invalid password");

            _currentSession = new FakeSession(this, new GoogleAccountVault());
            return Task.FromResult<GoogleAccountVaultSession>(_currentSession);
        }

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrongPassword)
                throw new System.Security.Cryptography.CryptographicException("Invalid password");

            _currentSession = new FakeSession(this, new GoogleAccountVault());
            return Task.FromResult<GoogleAccountVaultSession>(_currentSession);
        }

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default)
        {
            RememberedUnlockCalled = true;
            TryOpenRememberedCallCount++;
            if (RememberedUnlockException != null)
                return Task.FromException<GoogleAccountVaultSession?>(RememberedUnlockException);

            if (RememberedVault == null)
                return Task.FromResult<GoogleAccountVaultSession?>(null);

            _currentSession = new FakeSession(this, RememberedVault);
            return Task.FromResult<GoogleAccountVaultSession?>(_currentSession);
        }

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default)
        {
            SavedVault = session.Vault;
            return Task.CompletedTask;
        }

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default)
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

        private sealed class FakeSession : GoogleAccountVaultSession
        {
            private readonly FakeVaultStore _store;
            private GoogleAccountVault _vault;

            public FakeSession(FakeVaultStore store, GoogleAccountVault vault)
            {
                _store = store;
                _vault = vault;
            }

            public string VaultId => "test-vault-id";
            public GoogleAccountVault Vault => _vault;

            public void Replace(GoogleAccountVault vault)
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
