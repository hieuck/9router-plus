using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Core.Providers;
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
        Assert.False(viewModel.GoogleAccounts[0].IsEditable);
    }

    [Fact]
    public async Task Loads_profile_rows_after_main_view_model_becomes_ready()
    {
        var viewModel = CreateViewModel();

        await _mainViewModel.InitializeAsync();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);

        Assert.Single(viewModel.GoogleAccounts);
        Assert.Equal(_profile.Name, Assert.Single(viewModel.GoogleAccounts).ProfileName);
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
    public async Task BatchLoginCommand_uses_the_shared_google_authentication_runner_for_each_selected_row()
    {
        var receivedProfiles = new List<ChromeProfile>();
        GoogleLoginCredential? receivedCredential = null;
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((profile, credential, _) =>
        {
            receivedProfiles.Add(profile);
            receivedCredential = credential;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { _profile }, receivedProfiles);
        Assert.NotNull(receivedCredential);
        Assert.Equal(_profile.Id, receivedCredential!.ProfileId);
        Assert.Equal("user@example.test", receivedCredential.Email);
        Assert.Equal("synthetic-login-password", receivedCredential.Password);
        Assert.Equal("NONE", receivedCredential.TotpSecret);
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchLoginCommand_runs_selected_rows_sequentially_and_continues_after_failure()
    {
        var secondProfile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Profile 2"),
            "Test Profile 2",
            "Profile 2",
            _rootDirectory,
            true);
        _mainViewModel.Profiles.Add(secondProfile);
        _mainViewModel.FilteredProfiles.Add(secondProfile);

        var receivedProfiles = new List<ChromeProfile>();
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id,
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((profile, credential, _) =>
        {
            receivedProfiles.Add(profile);
            receivedEmails.Add(credential.Email);
            return Task.FromResult(
                credential.Email.StartsWith("first", StringComparison.Ordinal)
                    ? GoogleLoginResult.InvalidCredentials()
                    : GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 2);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = viewModel.GoogleAccounts.Single(row => row.ProfileId == _profile.Id);
        firstRow.IsSelected = true;
        var secondRow = viewModel.GoogleAccounts.Single(row => row.ProfileId == secondProfile.Id);
        secondRow.Email = "second@example.test";
        secondRow.Password = "synthetic-login-password";
        secondRow.TotpSecret = "NONE";
        secondRow.HasCredentials = true;
        secondRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { _profile, secondProfile }, receivedProfiles);
        Assert.Equal(
            new[] { "first@example.test", "second@example.test" },
            receivedEmails);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginRowCommand_does_not_rebind_a_stale_profile_id_by_display_name()
    {
        var invoked = false;
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id,
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.ProfileId = "stale-profile-id";

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Profile not found", StringComparison.OrdinalIgnoreCase));

        Assert.False(invoked);
    }

    [Fact]
    public async Task BatchLoginCommand_continues_when_a_selected_row_has_invalid_credentials()
    {
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, _) =>
        {
            receivedEmails.Add(credential.Email);
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = Assert.Single(viewModel.GoogleAccounts);
        firstRow.IsSelected = true;
        firstRow.Email = "not-an-email";
        firstRow.Password = "synthetic-login-password";

        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "second@example.test",
            Password = "synthetic-login-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsSelected = true,
            IsVaultUnlocked = true
        });

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { "second@example.test" }, receivedEmails);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StopBatchLoginCommand_wins_when_runner_returns_after_cancellation()
    {
        var runnerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StopBatchLoginCommand.Execute(null);
        runnerRelease.SetResult(true);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login cancelled", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
    }

    [Fact]
    public async Task StopBatchLoginCommand_cancels_active_runner_and_does_not_start_next_row()
    {
        var runnerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerCancelled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, cancellationToken) =>
        {
            receivedEmails.Add(credential.Email);
            runnerStarted.TrySetResult(true);
            return WaitForCancellationAsync(cancellationToken, runnerCancelled);
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = Assert.Single(viewModel.GoogleAccounts);
        firstRow.IsSelected = true;

        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "second@example.test",
            Password = "synthetic-login-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsSelected = true,
            IsVaultUnlocked = true
        });

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StopBatchLoginCommand.Execute(null);
        await runnerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);

        Assert.Equal(new[] { "first@example.test" }, receivedEmails);
        Assert.Contains("cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchLoginCommand_waits_for_current_runner_before_starting_next_row()
    {
        var firstRunnerCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRunnerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, _) =>
        {
            receivedEmails.Add(credential.Email);
            if (credential.Email.StartsWith("first", StringComparison.Ordinal))
            {
                return firstRunnerCompleted.Task.ContinueWith(
                    _ => GoogleLoginResult.Success(),
                    TaskScheduler.Default);
            }

            secondRunnerStarted.TrySetResult(true);
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = Assert.Single(viewModel.GoogleAccounts);
        firstRow.IsSelected = true;
        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "second@example.test",
            Password = "synthetic-login-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsSelected = true,
            IsVaultUnlocked = true
        });

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => receivedEmails.Count == 1);
        Assert.False(secondRunnerStarted.Task.IsCompleted);

        firstRunnerCompleted.SetResult(true);
        await secondRunnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            new[] { "first@example.test", "second@example.test" },
            receivedEmails);
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
        Assert.Contains("Removed credentials", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GoogleAccountRow_masks_sensitive_values_by_default()
    {
        var row = new GoogleAccountRowViewModel
        {
            Password = "synthetic-password",
            TotpSecret = "synthetic-totp"
        };

        Assert.False(row.IsPasswordVisible);
        Assert.False(row.IsTotpSecretVisible);
    }

    [Fact]
    public void GoogleAccountRow_toggles_password_and_totp_visibility_independently()
    {
        var row = new GoogleAccountRowViewModel
        {
            IsVaultUnlocked = true
        };

        row.TogglePasswordVisibility();
        Assert.True(row.IsPasswordVisible);
        Assert.False(row.IsTotpSecretVisible);

        row.ToggleTotpSecretVisibility();
        Assert.True(row.IsPasswordVisible);
        Assert.True(row.IsTotpSecretVisible);

        row.TogglePasswordVisibility();
        Assert.False(row.IsPasswordVisible);
        Assert.True(row.IsTotpSecretVisible);
    }

    [Fact]
    public void GoogleAccountRow_resets_sensitive_visibility_when_editing_ends_or_credentials_clear()
    {
        var row = new GoogleAccountRowViewModel
        {
            HasCredentials = true,
            IsEditing = true,
            IsVaultUnlocked = true
        };
        row.TogglePasswordVisibility();
        row.ToggleTotpSecretVisibility();

        row.IsEditing = false;

        Assert.False(row.IsPasswordVisible);
        Assert.False(row.IsTotpSecretVisible);

        row.IsEditing = true;
        row.TogglePasswordVisibility();
        row.ToggleTotpSecretVisibility();
        row.HasCredentials = false;

        Assert.False(row.IsPasswordVisible);
        Assert.False(row.IsTotpSecretVisible);
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_resets_sensitive_visibility_for_removed_row()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "synthetic-totp"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsEditing = true;
        row.TogglePasswordVisibility();
        row.ToggleTotpSecretVisibility();

        await viewModel.RemoveGoogleAccountAsync("Test Profile");

        Assert.False(row.IsPasswordVisible);
        Assert.False(row.IsTotpSecretVisible);
    }

    [Fact]
    public async Task LoadProfileRowsAsync_adopts_legacy_name_keyed_record_when_name_is_unique()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "legacy@example.test",
            "legacy-password",
            "NONE"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var row = Assert.Single(viewModel.GoogleAccounts);
        Assert.Equal(_profile.Id, row.ProfileId);
        Assert.True(row.HasCredentials);
        Assert.Equal("legacy@example.test", row.Email);
        Assert.Equal("legacy-password", row.Password);
    }

    [Fact]
    public async Task LoadProfileRowsAsync_finds_stable_profile_id_keyed_record()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id,
            "stable@example.test",
            "stable-password",
            "NONE"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var row = Assert.Single(viewModel.GoogleAccounts);
        Assert.Equal(_profile.Id, row.ProfileId);
        Assert.True(row.HasCredentials);
        Assert.Equal("stable@example.test", row.Email);
    }

    [Fact]
    public async Task SaveRowCommand_persists_record_under_stable_profile_id()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Email = "saved@example.test";
        row.Password = "saved-password";
        row.TotpSecret = "NONE";

        viewModel.SaveRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved credentials", StringComparison.OrdinalIgnoreCase));

        await using var reopened = await _googleVaultStore.OpenAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None);
        var record = Assert.Single(reopened.Vault.Records);
        Assert.Equal(_profile.Id, record.ProfileId);
        Assert.NotEqual("Test Profile", record.ProfileId);
        Assert.Equal("saved@example.test", record.Email);
    }

    [Fact]
    public async Task Shared_name_record_is_neither_shown_when_ambiguous_nor_deleted_on_remove()
    {
        // Two sibling profiles with distinct Ids but the same display name.
        var siblingRoot = Path.GetTempPath() + $"RouterPlus-Ambiguous-{Guid.NewGuid():N}";
        Directory.CreateDirectory(siblingRoot);
        var first = new ChromeProfile(
            ChromeProfile.CreateId(siblingRoot, "Alpha"),
            "Shared Display",
            "Alpha",
            siblingRoot,
            true);
        var second = new ChromeProfile(
            ChromeProfile.CreateId(siblingRoot, "Beta"),
            "Shared Display",
            "Beta",
            siblingRoot,
            true);
        Assert.Equal(first.Name, second.Name);
        Assert.NotEqual(first.Id, second.Id);

        var mainViewModel = new MainViewModel(
            googleLoginVaultPaths: _vaultPaths,
            harnessProfiles: new[] { first, second });
        await mainViewModel.InitializeAsync();

        await using (var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None))
        {
            // A single legacy record keyed by the shared display name.
            session.Replace(new GoogleAccountVault(new[]
            {
                new GoogleLoginCredential("Shared Display", "shared@example.test", "shared-password", "NONE")
            }));
            await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        }

        var viewModel = new CredentialsManagerViewModel(
            mainViewModel,
            _googleVaultStore,
            _providerVaultStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(viewModel);

        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        // Neither row may adopt the ambiguous shared-name record (no silent merge).
        Assert.Equal(2, viewModel.GoogleAccounts.Count);
        Assert.All(viewModel.GoogleAccounts, row => Assert.False(row.HasCredentials));

        // Removing one profile must not delete the shared-name record.
        await viewModel.RemoveGoogleAccountAsync("Shared Display");

        await using var reopened = await _googleVaultStore.OpenAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None);
        var remaining = Assert.Single(reopened.Vault.Records);
        Assert.Equal("Shared Display", remaining.ProfileId);

        await mainViewModel.DisposeGoogleLoginSessionsAsync();
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_targets_the_selected_stable_profile_id()
    {
        var siblingRoot = Path.GetTempPath() + $"RouterPlus-Removal-{Guid.NewGuid():N}";
        Directory.CreateDirectory(siblingRoot);
        var first = new ChromeProfile(
            ChromeProfile.CreateId(siblingRoot, "Alpha"),
            "Shared Display",
            "Alpha",
            siblingRoot,
            true);
        var second = new ChromeProfile(
            ChromeProfile.CreateId(siblingRoot, "Beta"),
            "Shared Display",
            "Beta",
            siblingRoot,
            true);
        var mainViewModel = new MainViewModel(
            googleLoginVaultPaths: _vaultPaths,
            harnessProfiles: new[] { first, second });
        await mainViewModel.InitializeAsync();

        await using (var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None))
        {
            session.Replace(new GoogleAccountVault(new[]
            {
                new GoogleLoginCredential(first.Id, "first@example.test", "first-password", "NONE"),
                new GoogleLoginCredential(second.Id, "second@example.test", "second-password", "NONE")
            }));
            await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        }

        var viewModel = new CredentialsManagerViewModel(
            mainViewModel,
            _googleVaultStore,
            _providerVaultStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(viewModel);

        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var selectedRow = viewModel.GoogleAccounts.Single(row => row.ProfileId == second.Id);

        await viewModel.RemoveGoogleAccountAsync(selectedRow);

        await using var reopened = await _googleVaultStore.OpenAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None);
        var remaining = Assert.Single(reopened.Vault.Records);
        Assert.Equal(first.Id, remaining.ProfileId);
        Assert.Equal("first@example.test", remaining.Email);
        Assert.False(selectedRow.HasCredentials);
    }

    [Fact]
    public async Task Legacy_name_keyed_record_is_not_adopted_when_duplicate_is_hidden_by_filter()
    {
        var siblingRoot = Path.GetTempPath() + $"RouterPlus-Filtered-{Guid.NewGuid():N}";
        Directory.CreateDirectory(siblingRoot);
        var first = new ChromeProfile(
            ChromeProfile.CreateId(siblingRoot, "Alpha"),
            "Shared Display",
            "Alpha",
            siblingRoot,
            true);
        var second = new ChromeProfile(
            ChromeProfile.CreateId(siblingRoot, "Beta"),
            "Shared Display",
            "Beta",
            siblingRoot,
            true);
        var mainViewModel = new MainViewModel(
            googleLoginVaultPaths: _vaultPaths,
            harnessProfiles: new[] { first, second });
        await mainViewModel.InitializeAsync();
        mainViewModel.FilteredProfiles.Remove(second);

        await using (var session = await _googleVaultStore.CreateAsync(
            _vaultPaths.VaultPath,
            "synthetic-password",
            CancellationToken.None))
        {
            session.Replace(new GoogleAccountVault(new[]
            {
                new GoogleLoginCredential("Shared Display", "shared@example.test", "shared-password", "NONE")
            }));
            await _googleVaultStore.SaveAsync(session, CancellationToken.None);
        }

        var viewModel = new CredentialsManagerViewModel(
            mainViewModel,
            _googleVaultStore,
            _providerVaultStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(viewModel);

        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var row = Assert.Single(viewModel.GoogleAccounts);
        Assert.False(row.HasCredentials);
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
            automation ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
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

    private static async Task<GoogleLoginResult> WaitForCancellationAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource<bool> cancelled)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled.TrySetResult(true);
            return GoogleLoginResult.Cancelled();
        }

        return GoogleLoginResult.Success();
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
