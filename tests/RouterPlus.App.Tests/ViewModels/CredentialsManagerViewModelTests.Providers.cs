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
    public async Task LoadProviderConnectionsAsync_populates_each_provider_row_from_synthetic_vault()
    {
        await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "codex@example.test"
        });
        await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.Kiro,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential
            {
                Email = "kiro@example.test",
                Password = "synthetic-kiro-password",
                TotpSecret = "synthetic-kiro-totp"
            }
        });
        await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.GitHub,
            PreferredMethod = AuthMethod.GoogleOAuth,
            LinkedGoogleAccount = "github@example.test"
        });
        await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.OpenRouter,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential
            {
                Email = "openrouter@example.test",
                Password = "synthetic-openrouter-password"
            }
        });

        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);

        var codex = Assert.Single(viewModel.CodexConnections);
        Assert.Equal(AuthMethod.GoogleOAuth, codex.AuthMethod);
        Assert.Equal("codex@example.test", codex.LinkedGoogleAccount);
        Assert.True(codex.HasCredentials);
        var kiro = Assert.Single(viewModel.KiroConnections);
        Assert.Equal("kiro@example.test", kiro.Email);
        Assert.Equal("synthetic-kiro-password", kiro.Password);
        var github = Assert.Single(viewModel.GitHubConnections);
        Assert.Equal("github@example.test", github.LinkedGoogleAccount);
        var openRouter = Assert.Single(viewModel.OpenRouterConnections);
        Assert.Equal("openrouter@example.test", openRouter.Email);
        Assert.Equal("synthetic-openrouter-password", openRouter.Password);
    }
    [Fact]
    public async Task SaveProviderRowCommand_persists_direct_credentials_for_the_row_provider()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "  kiro@example.test  ";
        row.Password = "synthetic-kiro-password";
        row.TotpSecret = "  synthetic-kiro-totp  ";

        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Kiro credentials", StringComparison.Ordinal));

        var saved = await _providerVaultStore.GetConnectionAsync(_profile.Name, ProviderKind.Kiro);
        Assert.NotNull(saved);
        Assert.Equal("kiro@example.test", saved!.DirectCredential!.Email);
        Assert.Equal("synthetic-kiro-totp", saved.DirectCredential.TotpSecret);
    }
    [Fact]
    public async Task Provider_login_commands_delegate_to_provider_login_service()
    {
        foreach (var provider in new[] { ProviderKind.Kiro, ProviderKind.GitHub, ProviderKind.OpenRouter })
        {
            await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = _profile.Name,
                Provider = provider,
                PreferredMethod = AuthMethod.Direct,
                DirectCredential = new ProviderCredential
                {
                    Email = $"{provider}@example.test",
                    Password = "synthetic-provider-password"
                }
            });
        }

        var fakeService = new FakeCredentialsProviderLoginService((_, _, _, _, _) =>
            Task.FromResult(new CredentialsProviderLoginResult(true, "Login completed successfully.")));
        var viewModel = CreateViewModel(credentialsProviderLoginService: fakeService);
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);

        foreach (var (command, row) in new[]
        {
            (viewModel.LoginKiroRowCommand, Assert.Single(viewModel.KiroConnections)),
            (viewModel.LoginGitHubRowCommand, Assert.Single(viewModel.GitHubConnections)),
            (viewModel.LoginOpenRouterRowCommand, Assert.Single(viewModel.OpenRouterConnections))
        })
        {
            command.Execute(row);
            await WaitForAsync(() => viewModel.StatusMessage.Contains("Login completed successfully.", StringComparison.Ordinal));
        }

        Assert.Equal(3, fakeService.CallCount);
    }
    [Fact]
    public async Task Provider_login_commands_report_service_failure()
    {
        await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.GitHub,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential
            {
                Email = "github@example.test",
                Password = "synthetic-provider-password"
            }
        });

        var fakeService = new FakeCredentialsProviderLoginService((_, _, _, _, _) =>
            Task.FromResult(new CredentialsProviderLoginResult(false, "boom")));
        var viewModel = CreateViewModel(credentialsProviderLoginService: fakeService);
        await WaitForAsync(() => viewModel.GitHubConnections.Count == 1);

        var row = Assert.Single(viewModel.GitHubConnections);
        viewModel.LoginGitHubRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("boom", StringComparison.Ordinal));

        Assert.Equal(1, fakeService.CallCount);
    }
    [Fact]
    public async Task Provider_login_commands_pass_a_real_cancellation_token()
    {
        await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.GitHub,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential
            {
                Email = "github@example.test",
                Password = "synthetic-provider-password"
            }
        });

        CancellationToken captured = default;
        var fakeService = new FakeCredentialsProviderLoginService((_, _, _, _, ct) =>
        {
            captured = ct;
            return Task.FromResult(new CredentialsProviderLoginResult(true, "Login completed successfully."));
        });
        var viewModel = CreateViewModel(credentialsProviderLoginService: fakeService);
        await WaitForAsync(() => viewModel.GitHubConnections.Count == 1);

        var row = Assert.Single(viewModel.GitHubConnections);
        viewModel.LoginGitHubRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Login completed successfully.", StringComparison.Ordinal));

        Assert.NotEqual(CancellationToken.None, captured);
    }
    [Fact]
    public async Task RemoveProviderCommands_remove_the_selected_connection_for_each_provider()
    {
        foreach (var provider in new[] { ProviderKind.Kiro, ProviderKind.GitHub, ProviderKind.OpenRouter })
        {
            await _providerVaultStore.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = _profile.Name,
                Provider = provider,
                PreferredMethod = AuthMethod.Direct,
                DirectCredential = new ProviderCredential { Email = $"{provider}@example.test", Password = "synthetic-password" }
            });
        }

        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        viewModel.SelectedKiroConnection = Assert.Single(viewModel.KiroConnections);
        viewModel.RemoveKiroConnectionCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Removed Kiro credentials", StringComparison.Ordinal));
        viewModel.SelectedGitHubConnection = Assert.Single(viewModel.GitHubConnections);
        viewModel.RemoveGitHubConnectionCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Removed GitHub credentials", StringComparison.Ordinal));
        viewModel.SelectedOpenRouterConnection = Assert.Single(viewModel.OpenRouterConnections);
        viewModel.RemoveOpenRouterConnectionCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Removed OpenRouter credentials", StringComparison.Ordinal));
    }
    [Fact]
    public async Task SaveLoginAndRemoveProviderConnection_uses_provider_specific_collection()
    {
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "kiro@example.test";
        row.Password = "synthetic-kiro-password";

        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Kiro credentials", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(await _providerVaultStore.GetConnectionAsync(
            _profile.Name,
            ProviderKind.Kiro,
            CancellationToken.None));

        viewModel.LoginKiroRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Chrome installation not configured", StringComparison.OrdinalIgnoreCase));

        viewModel.SelectedKiroConnection = row;
        viewModel.RemoveKiroConnectionCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Removed Kiro credentials", StringComparison.OrdinalIgnoreCase));

        Assert.False(row.HasCredentials);
        Assert.Null(await _providerVaultStore.GetConnectionAsync(
            _profile.Name,
            ProviderKind.Kiro,
            CancellationToken.None));
    }
    [Fact]
    public async Task SaveProviderRowCommand_maps_each_provider_collection_to_its_provider_kind()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;
        var rows = new[]
        {
            (Row: Assert.Single(viewModel.KiroConnections), Provider: ProviderKind.Kiro, Command: viewModel.SaveProviderRowCommand),
            (Row: Assert.Single(viewModel.GitHubConnections), Provider: ProviderKind.GitHub, Command: viewModel.SaveProviderRowCommand),
            (Row: Assert.Single(viewModel.OpenRouterConnections), Provider: ProviderKind.OpenRouter, Command: viewModel.SaveProviderRowCommand)
        };

        foreach (var (row, provider, command) in rows)
        {
            row.AuthMethod = AuthMethod.Direct;
            row.Email = $"{provider.ToString().ToLowerInvariant()}@example.test";
            row.Password = "synthetic-password";
            row.TotpSecret = " synthetic-totp ";

            // Act
            command.Execute(row);
            await WaitForAsync(() => viewModel.StatusMessage.Contains(
                $"Saved {provider} credentials", StringComparison.Ordinal));

            // Assert
            var connection = await _providerVaultStore.GetConnectionAsync(
                _profile.Name,
                provider,
                CancellationToken.None);
            Assert.NotNull(connection);
            Assert.Equal(provider, connection!.Provider);
            Assert.Equal(AuthMethod.Direct, connection.PreferredMethod);
            Assert.Null(connection.LinkedGoogleAccount);
            Assert.NotNull(connection.DirectCredential);
            Assert.Equal(row.Email, connection.DirectCredential!.Email);
            Assert.Equal(row.Password, connection.DirectCredential.Password);
            Assert.Equal("synthetic-totp", connection.DirectCredential.TotpSecret);
        }
    }
    [Fact]
    public async Task SaveProviderRowCommand_requires_linked_google_account_for_oauth()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;

        // Act
        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Google account is required for OAuth method", StringComparison.Ordinal));

        // Assert
        Assert.False(row.HasCredentials);
        Assert.Null(await _providerVaultStore.GetConnectionAsync(
            _profile.Name,
            ProviderKind.Kiro,
            CancellationToken.None));
    }
    [Fact]
    public async Task SaveProviderRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.SaveProviderRowCommand.Execute(null!);
        await Task.Delay(100);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }
    [Fact]
    public async Task SaveProviderRowCommand_enters_edit_mode_for_existing_credentialed_row()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "kiro@example.test";
        row.Password = "synthetic-kiro-password";

        // First save
        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Kiro credentials", StringComparison.OrdinalIgnoreCase));
        Assert.True(row.HasCredentials);

        // Second save enters edit mode
        viewModel.SaveProviderRowCommand.Execute(row);

        Assert.True(row.IsEditing);
        Assert.Contains("Editing provider credentials", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task SaveProviderRowCommand_rejects_direct_without_email()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Password = "synthetic-password";

        viewModel.SaveProviderRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Email is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveProviderRowCommand_rejects_direct_without_password()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "kiro@example.test";

        viewModel.SaveProviderRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Password is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveProviderRowCommand_rejects_oauth_without_linked_account()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GitHubConnections.Count == 1);
        var row = Assert.Single(viewModel.GitHubConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = string.Empty;

        viewModel.SaveProviderRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Google account is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task SaveProviderRowCommand_rejects_oauth_when_linked_account_not_in_vault()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.OpenRouterConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.OpenRouterConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = "nonexistent@example.test";

        viewModel.SaveProviderRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("not found in vault", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.HasCredentials);
    }
    [Fact]
    public async Task SaveProviderRowCommand_saves_github_direct_credentials()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GitHubConnections.Count == 1);
        var row = Assert.Single(viewModel.GitHubConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "github@example.test";
        row.Password = "synthetic-github-password";
        row.TotpSecret = " synthetic-github-totp ";

        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved GitHub credentials", StringComparison.OrdinalIgnoreCase));

        var saved = await _providerVaultStore.GetConnectionAsync(
            _profile.Name, ProviderKind.GitHub, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("github@example.test", saved!.DirectCredential!.Email);
        Assert.Equal("synthetic-github-totp", saved.DirectCredential.TotpSecret);
    }
    [Fact]
    public async Task SaveProviderRowCommand_saves_openrouter_direct_credentials()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.OpenRouterConnections.Count == 1);
        var row = Assert.Single(viewModel.OpenRouterConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "openrouter@example.test";
        row.Password = "synthetic-openrouter-password";

        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved OpenRouter credentials", StringComparison.OrdinalIgnoreCase));

        var saved = await _providerVaultStore.GetConnectionAsync(
            _profile.Name, ProviderKind.OpenRouter, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("openrouter@example.test", saved!.DirectCredential!.Email);
    }
    [Fact]
    public async Task SaveProviderRowCommand_saves_oauth_with_google_account()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "oauth@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.GoogleOAuth;
        row.LinkedGoogleAccount = "oauth@example.test";

        viewModel.SaveProviderRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Saved Kiro credentials", StringComparison.OrdinalIgnoreCase));

        var saved = await _providerVaultStore.GetConnectionAsync(
            _profile.Name, ProviderKind.Kiro, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(AuthMethod.GoogleOAuth, saved!.PreferredMethod);
        Assert.Equal("oauth@example.test", saved.LinkedGoogleAccount);
        Assert.Null(saved.DirectCredential);
    }
    [Fact]
    public async Task LoginKiroRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        await CreateVaultAsync("synthetic-password");
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.LoginKiroRowCommand.Execute(null!);
        await Task.Delay(50);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }
    [Fact]
    public async Task LoginGitHubRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        await CreateVaultAsync("synthetic-password");
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.LoginGitHubRowCommand.Execute(null!);
        await Task.Delay(50);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }
    [Fact]
    public async Task LoginOpenRouterRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        await CreateVaultAsync("synthetic-password");
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.LoginOpenRouterRowCommand.Execute(null!);
        await Task.Delay(50);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }
    [Fact]
    public async Task LoginKiroRowCommand_is_disabled_while_batch_running()
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

        var kiroRow = new ProviderConnectionRowViewModel
        {
            ProfileName = _profile.Name,
            ProfileId = _profile.Id,
            HasCredentials = true,
            AuthMethod = AuthMethod.Direct,
            Email = "kiro@example.test",
            Password = "synthetic-password"
        };

        try
        {
            // Real command behavior: the row button is disabled while a batch
            // runs, so executing it is skipped and the batch is undisturbed.
            Assert.False(viewModel.LoginKiroRowCommand.CanExecute(kiroRow));

            viewModel.LoginKiroRowCommand.Execute(kiroRow);
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
    public async Task LoginKiroRowCommand_reports_blank_profile_id()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "kiro@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = string.Empty;

        viewModel.LoginKiroRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Profile ID not resolved", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task LoginKiroRowCommand_reports_profile_not_found()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.AuthMethod = AuthMethod.Direct;
        row.Email = "kiro@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.ProfileId = "nonexistent-id";

        viewModel.LoginKiroRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Profile not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task RemoveKiroConnection_reports_error_on_exception()
    {
        var providerStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, $"provider-{Guid.NewGuid():N}.vault"));
        await providerStore.SaveConnectionAsync(new ProviderAuthConnection
        {
            ProfileName = _profile.Name,
            Provider = ProviderKind.Kiro,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "kiro@example.test", Password = "synthetic-password" }
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

        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);
        var row = Assert.Single(viewModel.KiroConnections);
        row.HasCredentials = true;

        // Dispose the store to cause an exception on remove
        providerStore.Dispose();

        viewModel.SelectedKiroConnection = row;
        viewModel.RemoveKiroConnectionCommand.Execute(null);
        await Task.Delay(200);

        Assert.Contains("Error removing", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void SelectedGitHubConnection_change_raises_property_changed()
    {
        var viewModel = CreateViewModel();
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.SelectedGitHubConnection))
                changed = true;
        };

        var row = new ProviderConnectionRowViewModel { ProfileName = "test" };
        viewModel.SelectedGitHubConnection = row;

        Assert.Same(row, viewModel.SelectedGitHubConnection);
        Assert.True(changed);
    }
    [Fact]
    public void SelectedOpenRouterConnection_change_raises_property_changed()
    {
        var viewModel = CreateViewModel();
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.SelectedOpenRouterConnection))
                changed = true;
        };

        var row = new ProviderConnectionRowViewModel { ProfileName = "test" };
        viewModel.SelectedOpenRouterConnection = row;

        Assert.Same(row, viewModel.SelectedOpenRouterConnection);
        Assert.True(changed);
    }
    [Fact]
    public void ProviderConnectionRow_reset_sensitive_visibility_when_editing_ends()
    {
        var row = new ProviderConnectionRowViewModel
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
    public void ProviderConnectionRow_action_button_text_depends_on_state()
    {
        var row = new ProviderConnectionRowViewModel();
        Assert.Equal("💾 Save", row.ActionButtonText);

        row.HasCredentials = true;
        Assert.Equal("✏ Edit", row.ActionButtonText);

        row.IsEditing = true;
        Assert.Equal("💾 Save", row.ActionButtonText);
    }
    [Fact]
    public void ProviderConnectionRow_auth_method_properties_reflect_current_method()
    {
        var row = new ProviderConnectionRowViewModel();

        row.AuthMethod = AuthMethod.GoogleOAuth;
        Assert.True(row.IsGoogleOAuth);
        Assert.False(row.IsDirect);
        Assert.Equal("Google OAuth", row.PreferredMethodText);

        row.AuthMethod = AuthMethod.Direct;
        Assert.False(row.IsGoogleOAuth);
        Assert.True(row.IsDirect);
        Assert.Equal("Direct Login", row.PreferredMethodText);
    }
    [Fact]
    public void ProviderConnectionRow_health_status_display_emoji()
    {
        var row = new ProviderConnectionRowViewModel();

        Assert.Equal(string.Empty, row.HealthStatusDisplay);
        Assert.Equal(string.Empty, row.HealthStatusEmoji);

        row.UpdateHealthStatus(CredentialHealthCheckResult.Error("failed"));
        Assert.NotEmpty(row.HealthStatusDisplay);
        Assert.NotEmpty(row.HealthStatusEmoji);
    }
}
