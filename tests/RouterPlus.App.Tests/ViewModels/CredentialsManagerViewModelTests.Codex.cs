using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Security;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.App.ViewModels;
using RouterPlus.App.Services;

namespace RouterPlus.App.Tests.ViewModels;

public sealed partial class CredentialsManagerViewModelTests
{
    [Fact]
    public async Task BatchLoginCommand_logs_in_selected_codex_direct_connection()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password");
        CodexLoginCredential? receivedCredential = null;
        var viewModel = CreateViewModel(codexAuthentication: (_, credential, _) =>
        {
            receivedCredential = credential;
            return Task.FromResult(CodexLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-codex-password";
        row.TotpSecret = " synthetic-codex-totp ";
        row.HasCredentials = true;
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.NotNull(receivedCredential);
        Assert.Equal(_profile.Id, receivedCredential!.ProfileId);
        Assert.Equal("codex@example.test", receivedCredential.Email);
        Assert.Equal("synthetic-codex-password", receivedCredential.Password);
        Assert.Equal("synthetic-codex-totp", receivedCredential.TotpSecret);
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_rejects_codex_oauth_until_linked_google_account_is_healthy()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "oauth@example.test", "synthetic-password", "NONE"));
        var invoked = false;
        var viewModel = CreateViewModel(codexAuthentication: (_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(CodexLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var googleRow = Assert.Single(viewModel.GoogleAccounts);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.LinkedGoogleAccount = googleRow.Email;
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.False(invoked);
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_logs_in_codex_oauth_with_totp_after_google_health_check()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "oauth@example.test", "synthetic-password", "NONE"));
        CodexLoginCredential? receivedCredential = null;
        var viewModel = CreateViewModel(codexAuthentication: (_, credential, _) =>
        {
            receivedCredential = credential;
            return Task.FromResult(CodexLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var googleRow = Assert.Single(viewModel.GoogleAccounts);
        googleRow.UpdateHealthStatus(CredentialHealthCheckResult.Healthy("synthetic health"));
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.LinkedGoogleAccount = googleRow.Email;
        codexRow.TotpSecret = " synthetic-codex-totp ";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.NotNull(receivedCredential);
        Assert.Equal(CodexLoginCredential.FromGoogleOAuthWithTotp(
            _profile.Id, googleRow.Email, "synthetic-codex-totp").TotpSecret, receivedCredential!.TotpSecret);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task SaveCodexRowCommand_rejects_direct_login_without_email()
    {
        var viewModel = CreateSyntheticViewModel(new GoogleLoginCredential(
            _profile.Id,
            "synthetic@example.test",
            "synthetic-password",
            "NONE"));

        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Password = "synthetic-codex-password";

        viewModel.SaveCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Email is required for Direct login", StringComparison.Ordinal));

        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveAndRemoveCodexDirectConnection_persists_synthetic_credentials_only_in_test_vault()
    {
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-codex-password";
        row.TotpSecret = "synthetic-totp";

        viewModel.SaveCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Codex credentials", StringComparison.OrdinalIgnoreCase));

        var saved = await _providerVaultStore.GetConnectionAsync(
            _profile.Name,
            ProviderKind.Codex,
            CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(AuthMethod.Direct, saved!.PreferredMethod);
        Assert.Equal("codex@example.test", saved.DirectCredential!.Email);
        Assert.Equal("synthetic-codex-password", saved.DirectCredential.Password);

        viewModel.SelectedCodexConnection = row;
        viewModel.RemoveCodexConnectionCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Removed Codex credentials", StringComparison.OrdinalIgnoreCase));

        Assert.False(row.HasCredentials);
        Assert.Null(await _providerVaultStore.GetConnectionAsync(
            _profile.Name,
            ProviderKind.Codex,
            CancellationToken.None));
    }
    [Fact]
    public async Task LoginCodexRowCommand_direct_method_passes_row_credential_to_fake_runner()
    {
        CodexLoginCredential? receivedCredential = null;
        ChromeProfile? receivedProfile = null;
        var viewModel = CreateViewModel(
            codexAuthentication: (profile, credential, _) =>
            {
                receivedProfile = profile;
                receivedCredential = credential;
                return Task.FromResult(CodexLoginResult.Success());
            });

        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-codex-password";
        row.TotpSecret = "synthetic-totp";
        row.HasCredentials = true;

        viewModel.LoginCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Codex login successful", StringComparison.OrdinalIgnoreCase));

        Assert.Same(_profile, receivedProfile);
        Assert.NotNull(receivedCredential);
        Assert.Equal(_profile.Id, receivedCredential!.ProfileId);
        Assert.Equal("codex@example.test", receivedCredential.Email);
        Assert.Equal("synthetic-codex-password", receivedCredential.Password);
        Assert.Equal("synthetic-totp", receivedCredential.TotpSecret);
    }
    [Fact]
    public async Task SaveCodexRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.SaveCodexRowCommand.Execute(null!);
        await Task.Delay(100);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }
    [Fact]
    public async Task SaveCodexRowCommand_is_disabled_while_batch_running()
    {
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var automationCalls = 0;
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            Interlocked.Increment(ref automationCalls);
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);

        // Start a batch with a Google row
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var vm2 = CreateViewModel(async (_, _, _) =>
        {
            Interlocked.Increment(ref automationCalls);
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });
        await WaitForAsync(() => vm2.GoogleAccounts.Count == 1);
        await vm2.UnlockVaultAsync("synthetic-password", remember: false);
        var googleRow = Assert.Single(vm2.GoogleAccounts);
        googleRow.IsSelected = true;

        vm2.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var codexRow = new CodexConnectionRowViewModel
        {
            ProfileName = _profile.Name,
            AuthMethod = AuthMethod.Direct,
            Email = "codex@example.test",
            Password = "synthetic-password"
        };

        try
        {
            // Real command behavior: the save button is disabled while a batch
            // runs, so executing it is skipped and the batch is undisturbed.
            Assert.False(vm2.SaveCodexRowCommand.CanExecute(codexRow));

            vm2.SaveCodexRowCommand.Execute(codexRow);
            await Task.Delay(50);

            Assert.True(vm2.IsBatchLoginRunning);
            Assert.Equal(1, automationCalls);
        }
        finally
        {
            // Always release the runner: without this, a failed assert would
            // leave the batch task pending and DisposeAsync would hang forever.
            runnerRelease.TrySetResult(true);
        }

        await WaitForAsync(() => !vm2.IsBatchLoginRunning);
    }
    [Fact]
    public async Task SaveCodexRowCommand_enters_edit_mode_for_existing_credentialed_row()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-password";

        // First save
        viewModel.SaveCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Codex credentials", StringComparison.OrdinalIgnoreCase));
        Assert.True(row.HasCredentials);
        Assert.False(row.IsEditing);

        // Second save should enter edit mode
        viewModel.SaveCodexRowCommand.Execute(row);

        Assert.True(row.IsEditing);
        Assert.Contains("Editing Codex credentials", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task SaveCodexRowCommand_rejects_oauth_without_linked_google_account()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = string.Empty;

        viewModel.SaveCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Google account is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveCodexRowCommand_rejects_oauth_when_linked_account_not_in_vault()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = "nonexistent@example.test";

        viewModel.SaveCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("not found in vault", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveCodexRowCommand_rejects_direct_without_password()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";

        viewModel.SaveCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Password is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveCodexRowCommand_saves_oauth_with_totp_when_google_account_exists()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "oauth@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = "oauth@example.test";
        row.TotpSecret = " synthetic-codex-totp ";

        viewModel.SaveCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Codex credentials", StringComparison.OrdinalIgnoreCase));

        var saved = await _providerVaultStore.GetConnectionAsync(
            _profile.Name, ProviderKind.Codex, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(AuthMethod.GoogleOAuth, saved!.PreferredMethod);
        Assert.Equal("oauth@example.test", saved.LinkedGoogleAccount);
        Assert.NotNull(saved.DirectCredential);
        Assert.Equal("synthetic-codex-totp", saved.DirectCredential.TotpSecret);
    }
    [Fact]
    public async Task LoginCodexRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.LoginCodexRowCommand.Execute(null!);
        await Task.Delay(100);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }
    [Fact]
    public async Task LoginCodexRowCommand_is_disabled_while_batch_running()
    {
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var automationCalls = 0;
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            Interlocked.Increment(ref automationCalls);
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var googleRow = Assert.Single(viewModel.GoogleAccounts);
        googleRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var codexRow = new CodexConnectionRowViewModel
        {
            ProfileName = _profile.Name,
            HasCredentials = true,
            ProfileId = _profile.Id,
            AuthMethod = AuthMethod.Direct,
            Email = "codex@example.test",
            Password = "synthetic-password"
        };
        await CreateVaultAsync("synthetic-password");
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        try
        {
            // Real command behavior: the row button is disabled while a batch
            // runs, so executing it is skipped and the batch is undisturbed.
            Assert.False(viewModel.LoginCodexRowCommand.CanExecute(codexRow));

            viewModel.LoginCodexRowCommand.Execute(codexRow);
            await Task.Delay(50);

            Assert.True(viewModel.IsBatchLoginRunning);
            Assert.Equal(1, automationCalls);
        }
        finally
        {
            // Always release the runner: without this, a failed assert would
            // leave the batch task pending and DisposeAsync would hang forever.
            runnerRelease.TrySetResult(true);
        }

        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }
    [Fact]
    public async Task LoginCodexRowCommand_reports_blank_profile_id()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = string.Empty;

        viewModel.LoginCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Profile ID not resolved", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoginCodexRowCommand_reports_profile_not_found()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = "nonexistent-id";

        viewModel.LoginCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Profile not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoginCodexRowCommand_oauth_rejects_missing_linked_google_account()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = string.Empty;
        row.HasCredentials = true;
        row.ProfileId = _profile.Id;

        viewModel.LoginCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("No linked Google account", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoginCodexRowCommand_oauth_rejects_unhealthy_google_account()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "oauth@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var googleRow = Assert.Single(viewModel.GoogleAccounts);
        // Leave health status as null (not healthy)

        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.GoogleOAuth;
        codexRow.LinkedGoogleAccount = googleRow.Email;
        codexRow.HasCredentials = true;

        viewModel.LoginCodexRowCommand.Execute(codexRow);
        await Task.Delay(50);

        Assert.Contains("must be logged in first", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoginCodexRowCommand_direct_rejects_empty_email_or_password()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = string.Empty;
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = _profile.Id;

        viewModel.LoginCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Email and password required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoginCodexRowCommand_reports_manual_intervention()
    {
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => Task.FromResult(
                CodexLoginResult.ManualInterventionRequired("synthetic manual step")));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = _profile.Id;

        viewModel.LoginCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Manual intervention required", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("synthetic manual step", viewModel.StatusMessage, StringComparison.Ordinal);
    }
    [Fact]
    public async Task LoginCodexRowCommand_reports_failure_result()
    {
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => Task.FromResult(
                CodexLoginResult.Failed("synthetic login failure")));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = _profile.Id;

        viewModel.LoginCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("synthetic login failure", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public async Task LoginCodexRowCommand_reports_exception_without_throwing()
    {
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => throw new InvalidOperationException("synthetic codex failure"));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "codex@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = _profile.Id;

        viewModel.LoginCodexRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("synthetic codex failure", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain("synthetic-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }
    [Fact]
    public async Task RemoveCodexConnectionCommand_does_nothing_when_nothing_selected()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        viewModel.SelectedCodexConnection = null;

        viewModel.RemoveCodexConnectionCommand.Execute(null);
        await Task.Delay(50);

        // No crash
        Assert.Single(viewModel.CodexConnections);
    }
    [Fact]
    public async Task RemoveCodexConnection_reports_error_on_exception()
    {
        var providerStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, $"provider-{Guid.NewGuid():N}.vault"));
        await providerStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "codex@example.test", Password = "synthetic-password" }
        });
        var session = new SyntheticVaultSession(new GoogleAccountVault());
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            new SyntheticGoogleVaultStore(session),
            providerStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(viewModel);
        _syntheticProviderStores.Add(providerStore);
        _syntheticSessions.Add(session);

        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.HasCredentials = true;

        // Dispose the store to cause an exception on remove
        providerStore.Dispose();

        viewModel.SelectedCodexConnection = row;
        viewModel.RemoveCodexConnectionCommand.Execute(null);
        await Task.Delay(200);

        Assert.Contains("Error removing", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task ToggleSelectAllCodexCommand_selects_only_configured_accounts_and_toggles_back()
    {
        var secondProfile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Profile 2"),
            "Test Profile 2",
            "Profile 2",
            _rootDirectory,
            true);
        _mainViewModel.Profiles.Add(secondProfile);
        _mainViewModel.FilteredProfiles.Add(secondProfile);

        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 2);

        // Save credentials for first profile only
        var firstRow = viewModel.CodexConnections.Single(c => c.ProfileId == _profile.Id);
        firstRow.AuthMethod = AuthMethod.Direct;
        firstRow.Email = "codex@example.test";
        firstRow.Password = "synthetic-password";
        viewModel.SaveCodexRowCommand.Execute(firstRow);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Codex credentials", StringComparison.OrdinalIgnoreCase));

        viewModel.ToggleSelectAllCodexCommand.Execute(null);

        Assert.True(firstRow.IsSelected);
        Assert.Equal(1, viewModel.CodexSelectedCount);
        Assert.True(viewModel.IsAllCodexSelected);
        Assert.False(viewModel.IsCodexSelectionIndeterminate);

        viewModel.ToggleSelectAllCodexCommand.Execute(null);

        Assert.False(firstRow.IsSelected);
        Assert.Equal(0, viewModel.CodexSelectedCount);
        Assert.False(viewModel.IsAllCodexSelected);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_direct_missing_email_fails_that_row()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "direct@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = string.Empty;
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded, 1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_oauth_fails_when_google_account_not_found()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "oauth@example.test", "synthetic-password", "NONE"));
        var invoked = false;
        var viewModel = CreateViewModel(codexAuthentication: (_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(CodexLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.LinkedGoogleAccount = "nonexistent@example.test";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.False(invoked);
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_empty_profile_id_fails_that_row()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = "codex@example.test";
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.ProfileId = string.Empty;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_runner_exception_fails_that_row()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => throw new IOException("synthetic codex error"));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = "codex@example.test";
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_manual_intervention_fails_that_row()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => Task.FromResult(
                CodexLoginResult.ManualInterventionRequired("synthetic manual")));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = "codex@example.test";
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_cancelled_stops_batch()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel(
            codexAuthentication: (_, _, _) => Task.FromResult(CodexLoginResult.Cancelled()));
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = "codex@example.test";
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_codex_oauth_google_not_healthy_fails()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "oauth@example.test", "synthetic-password", "NONE"));
        var invoked = false;
        var viewModel = CreateViewModel(codexAuthentication: (_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(CodexLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var googleRow = Assert.Single(viewModel.GoogleAccounts);
        // Do NOT set health to Healthy
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.LinkedGoogleAccount = googleRow.Email;
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.False(invoked);
        Assert.Contains("0 succeeded, 1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void SelectedCodexConnection_change_raises_property_changed()
    {
        var viewModel = CreateViewModel();
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.SelectedCodexConnection))
                changed = true;
        };

        var row = new CodexConnectionRowViewModel { ProfileName = "test" };
        viewModel.SelectedCodexConnection = row;

        Assert.Same(row, viewModel.SelectedCodexConnection);
        Assert.True(changed);
    }
    [Fact]
    public async Task BatchLoginCommand_can_execute_with_selected_codex_credentials()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = "codex@example.test";
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        Assert.True(viewModel.BatchLoginCommand.CanExecute(null));
    }
    [Fact]
    public async Task LoginCodexRow_oauth_with_google_account_not_found()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var row = Assert.Single(viewModel.CodexConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = "nonexistent@example.test";
        row.HasCredentials = true;
        row.ProfileId = _profile.Id;

        viewModel.LoginCodexRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void CodexConnectionRow_reset_sensitive_visibility_when_editing_ends()
    {
        var row = new CodexConnectionRowViewModel
        {
            HasCredentials = true,
            IsEditing = true
        };
        row.IsPasswordVisible = true;
        row.IsTotpSecretVisible = true;

        row.IsEditing = false;

        Assert.False(row.IsPasswordVisible);
        Assert.False(row.IsTotpSecretVisible);
    }
    [Fact]
    public void CodexConnectionRow_action_button_text_depends_on_state()
    {
        var row = new CodexConnectionRowViewModel();
        Assert.Equal("💾 Save", row.ActionButtonText);

        row.HasCredentials = true;
        Assert.Equal("✏ Edit", row.ActionButtonText);

        row.IsEditing = true;
        Assert.Equal("💾 Save", row.ActionButtonText);
    }
    [Fact]
    public void CodexConnectionRow_auth_method_properties_reflect_current_method()
    {
        var row = new CodexConnectionRowViewModel();

        row.AuthMethod = AuthMethod.GoogleOAuth;
        Assert.True(row.IsGoogleOAuth);
        Assert.False(row.IsDirect);
        Assert.Equal("Google", row.AuthMethodDisplay);

        row.AuthMethod = AuthMethod.Direct;
        Assert.False(row.IsGoogleOAuth);
        Assert.True(row.IsDirect);
        Assert.Equal("Direct", row.AuthMethodDisplay);
    }
    [Fact]
    public void CodexConnectionRow_is_editable_depends_on_has_credentials_and_is_editing()
    {
        var row = new CodexConnectionRowViewModel();

        Assert.True(row.IsEditable); // No credentials, always editable

        row.HasCredentials = true;
        Assert.False(row.IsEditable); // Has credentials, not editing

        row.IsEditing = true;
        Assert.True(row.IsEditable); // Has credentials, editing
    }
    [Fact]
    public void CodexConnectionRow_health_status_display_emoji()
    {
        var row = new CodexConnectionRowViewModel();

        Assert.Equal(string.Empty, row.HealthStatusDisplay);
        Assert.Equal(string.Empty, row.HealthStatusEmoji);

        row.UpdateHealthStatus(CredentialHealthCheckResult.Healthy("ok"));
        Assert.NotEmpty(row.HealthStatusDisplay);
        Assert.NotEmpty(row.HealthStatusEmoji);
    }
}
