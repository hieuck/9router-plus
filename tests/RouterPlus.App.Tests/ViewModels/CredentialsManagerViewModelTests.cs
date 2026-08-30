using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using RouterPlus.App.ViewModels;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// Behavior tests for the Credentials Manager vault and per-row login workflows.
/// </summary>
public sealed class CredentialsManagerViewModelTests : IAsyncLifetime
{
    private readonly string _rootDirectory;
    private readonly GoogleAccountVaultPaths _vaultPaths;
    private readonly GoogleAccountVaultStore _googleVaultStore;
    private readonly ProviderConnectionVaultStore _providerVaultStore;
    private readonly ChromeProfile _profile;
    private readonly MainViewModel _mainViewModel;
    private readonly List<CredentialsManagerViewModel> _viewModels = new();

    public CredentialsManagerViewModelTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"RouterPlus-CredentialsTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);

        _vaultPaths = new GoogleAccountVaultPaths(_rootDirectory);
        _googleVaultStore = new GoogleAccountVaultStore(_vaultPaths);
        _providerVaultStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, "provider-connections.vault"));
        _profile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Default"),
            "Test Profile",
            "Default",
            _rootDirectory,
            true);
        _mainViewModel = new MainViewModel(
            googleLoginVaultPaths: _vaultPaths,
            harnessProfiles: new[] { _profile });
    }

    public async Task InitializeAsync()
    {
        await _mainViewModel.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var viewModel in _viewModels)
        {
            await viewModel.DisposeAsync();
        }

        _providerVaultStore.Dispose();
        _googleVaultStore.Dispose();

        if (Directory.Exists(_rootDirectory))
        {
            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for files still held by the test process.
            }
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Starts_locked_when_vault_has_no_remembered_key()
    {
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        Assert.True(viewModel.IsVaultLocked);
        Assert.Contains("locked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.GoogleAccounts[0].HasCredentials);
    }

    [Fact]
    public async Task UnlockVaultAsync_creates_new_vault_and_loads_profile_rows()
    {
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        Assert.False(viewModel.IsVaultLocked);
        Assert.True(File.Exists(_vaultPaths.VaultPath));
        Assert.Single(viewModel.GoogleAccounts);
        Assert.Contains("Vault unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockVaultAsync_does_not_unlock_with_wrong_password()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("wrong-password", remember: false);

        Assert.True(viewModel.IsVaultLocked);
        Assert.Contains("Unable to unlock vault", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlockVaultAsync_rejects_blank_password_without_creating_vault()
    {
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("  ", remember: false);

        Assert.True(viewModel.IsVaultLocked);
        Assert.False(File.Exists(_vaultPaths.VaultPath));
        Assert.Contains("password is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockVaultAsync_remember_true_writes_remembered_key()
    {
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: true);

        Assert.False(viewModel.IsVaultLocked);
        Assert.True(File.Exists(_vaultPaths.RememberedKeyPath));
    }

    [Fact]
    public async Task UnlockVaultAsync_loads_existing_credentials()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var row = Assert.Single(viewModel.GoogleAccounts);
        Assert.False(viewModel.IsVaultLocked);
        Assert.True(row.HasCredentials);
        Assert.Equal("user@example.test", row.Email);
        Assert.Equal("synthetic-login-password", row.Password);
        Assert.Equal("NONE", row.TotpSecret);
    }

    [Fact]
    public async Task LoginRowCommand_invokes_automation_with_row_credentials()
    {
        GoogleLoginCredential? receivedCredential = null;
        ChromeProfile? receivedProfile = null;
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((profile, credential, _) =>
        {
            receivedProfile = profile;
            receivedCredential = credential;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Login successful", StringComparison.OrdinalIgnoreCase));

        Assert.Same(_profile, receivedProfile);
        Assert.NotNull(receivedCredential);
        Assert.Equal("user@example.test", receivedCredential!.Email);
        Assert.Equal("synthetic-login-password", receivedCredential.Password);
        Assert.Equal("NONE", receivedCredential.TotpSecret);
    }

    [Fact]
    public async Task LoginRowCommand_reports_locked_vault_without_invoking_automation()
    {
        var invoked = false;
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.HasCredentials = true;
        row.Email = "user@example.test";
        row.Password = "synthetic-login-password";

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Vault not unlocked", StringComparison.OrdinalIgnoreCase));

        Assert.False(invoked);
    }

    [Fact]
    public async Task LoginRowCommand_reports_automation_failure_without_throwing()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, _, _) =>
            Task.FromResult(GoogleLoginResult.InvalidCredentials()));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Invalid email", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Test Profile", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginRowCommand_reports_automation_exception_without_throwing()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, _, _) =>
            throw new InvalidOperationException("synthetic automation failure"));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("synthetic automation failure", StringComparison.Ordinal));

        Assert.Contains("Test Profile", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-login-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginRowCommand_converts_blank_totp_to_NONE_placeholder()
    {
        GoogleLoginCredential? receivedCredential = null;
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, _) =>
        {
            receivedCredential = credential;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.TotpSecret = "   ";

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => receivedCredential is not null);

        Assert.Equal("NONE", receivedCredential!.TotpSecret);
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_after_manual_unlock_keeps_vault_unlocked()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        await viewModel.RemoveGoogleAccountAsync("Test Profile");

        Assert.False(viewModel.IsVaultLocked);
        Assert.False(Assert.Single(viewModel.GoogleAccounts).HasCredentials);
        Assert.Contains("Removed Google account", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_removes_only_the_requested_profile_when_emails_match()
    {
        await CreateVaultAsync("synthetic-password");
        await using (var session = await _googleVaultStore.OpenAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None))
        {
            session.Replace(new GoogleAccountVault(new[]
            {
                new GoogleLoginCredential("Test Profile", "shared@example.test", "test-password", "NONE"),
                new GoogleLoginCredential("Other Profile", "shared@example.test", "other-password", "NONE")
            }));
            await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        }

        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        await viewModel.RemoveGoogleAccountAsync("Test Profile");

        await using var reopened = await _googleVaultStore.OpenAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None);
        var remaining = Assert.Single(reopened.Vault.Records);
        Assert.Equal("Other Profile", remaining.ProfileId);
        Assert.Equal("other-password", remaining.Password);
    }

    private CredentialsManagerViewModel CreateViewModel(
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? automation = null)
    {
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            _googleVaultStore,
            _providerVaultStore,
            _vaultPaths,
            automation ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())));
        _viewModels.Add(viewModel);
        return viewModel;
    }

    private async Task CreateVaultAsync(string password, GoogleLoginCredential? credential = null)
    {
        await using var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            password,
            CancellationToken.None);
        if (credential is not null)
        {
            session.Replace(new GoogleAccountVault(new[] { credential }));
        }

        await _googleVaultStore.SaveAsync(session, CancellationToken.None);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.True(predicate(), "The expected asynchronous state was not reached within five seconds.");
    }
}
