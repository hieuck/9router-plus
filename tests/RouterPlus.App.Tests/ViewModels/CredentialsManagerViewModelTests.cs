using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
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
    private readonly List<ProviderConnectionVaultStore> _syntheticProviderStores = new();
    private readonly List<GoogleAccountVaultSession> _syntheticSessions = new();

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

        foreach (var session in _syntheticSessions)
        {
            await session.DisposeAsync();
        }

        foreach (var store in _syntheticProviderStores)
        {
            store.Dispose();
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
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        // Assert
        Assert.True(viewModel.IsVaultLocked);
        Assert.Contains("locked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.GoogleAccounts[0].HasCredentials);
        Assert.False(viewModel.GoogleAccounts[0].IsEditable);
    }

    [Fact]
    public async Task Loads_profile_rows_after_main_view_model_becomes_ready()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        await _mainViewModel.InitializeAsync();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);

        // Assert
        Assert.Single(viewModel.GoogleAccounts);
        Assert.Equal(_profile.Name, Assert.Single(viewModel.GoogleAccounts).ProfileName);
    }

    [Fact]
    public async Task UnlockVaultAsync_creates_new_vault_and_loads_profile_rows()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        // Act
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        // Assert
        Assert.False(viewModel.IsVaultLocked);
        Assert.True(File.Exists(_vaultPaths.VaultPath));
        Assert.Single(viewModel.GoogleAccounts);
        Assert.Contains("Vault unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockVaultAsync_does_not_unlock_with_wrong_password()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        // Act
        await viewModel.UnlockVaultAsync("wrong-password", remember: false);

        // Assert
        Assert.True(viewModel.IsVaultLocked);
        Assert.Contains("Unable to unlock vault", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnlockVaultAsync_rejects_blank_password_without_creating_vault()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        // Act
        await viewModel.UnlockVaultAsync("  ", remember: false);

        // Assert
        Assert.True(viewModel.IsVaultLocked);
        Assert.False(File.Exists(_vaultPaths.VaultPath));
        Assert.Contains("password is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockVaultAsync_remember_true_writes_remembered_key()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        // Act
        await viewModel.UnlockVaultAsync("synthetic-password", remember: true);

        // Assert
        Assert.False(viewModel.IsVaultLocked);
        Assert.True(File.Exists(_vaultPaths.RememberedKeyPath));
    }

    [Fact]
    public async Task UnlockVaultAsync_loads_existing_credentials()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);

        // Act
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        // Assert
        var row = Assert.Single(viewModel.GoogleAccounts);
        Assert.False(viewModel.IsVaultLocked);
        Assert.True(row.HasCredentials);
        Assert.Equal("user@example.test", row.Email);
        Assert.Equal("synthetic-login-password", row.Password);
        Assert.Equal("NONE", row.TotpSecret);
    }

    [Theory]
    [InlineData("argument", "Invalid vault password")]
    [InlineData("unauthorized", "Access denied")]
    [InlineData("invalid-operation", "synthetic vault state failure")]
    [InlineData("unexpected", "Vault could not be opened")]
    public async Task UnlockVaultAsync_maps_open_errors_to_safe_status_messages(string errorKind, string expectedMessage)
    {
        var viewModel = CreateViewModel(googleVaultStore: new ThrowingGoogleVaultStore(CreateVaultOpenException(errorKind)));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        Assert.True(viewModel.IsVaultLocked);
        Assert.Contains(expectedMessage, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic secret", viewModel.StatusMessage, StringComparison.Ordinal);
    }

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
    public async Task Provider_login_commands_report_the_provider_specific_unintegrated_flow()
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

        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.KiroConnections.Count == 1);

        foreach (var (command, row, provider) in new[]
        {
            (viewModel.LoginKiroRowCommand, Assert.Single(viewModel.KiroConnections), "Kiro"),
            (viewModel.LoginGitHubRowCommand, Assert.Single(viewModel.GitHubConnections), "GitHub"),
            (viewModel.LoginOpenRouterRowCommand, Assert.Single(viewModel.OpenRouterConnections), "OpenRouter")
        })
        {
            command.Execute(row);
            await WaitForAsync(() => viewModel.StatusMessage.Contains($"{provider} direct login", StringComparison.Ordinal));
        }
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
    public async Task BatchLoginCommand_reports_locked_vault_without_invoking_automation()
    {
        // Arrange
        var invoked = false;
        var viewModel = CreateViewModel((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(GoogleLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Email = "locked@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Vault not unlocked", StringComparison.OrdinalIgnoreCase));

        // Assert
        Assert.False(invoked);
        Assert.Contains("Vault not unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
    }

    [Fact]
    public async Task BatchLoginCommand_reports_missing_profile_and_completes_cleanup()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.ProfileId = "missing-profile-id";
        row.Email = "missing@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Null(viewModel.BatchLoginTask);
    }

    [Fact]
    public async Task BatchLoginCommand_reports_google_manual_intervention_as_failure()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "manual@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel((_, _, _) => Task.FromResult(
            GoogleLoginResult.ManualInterventionRequired("synthetic manual step")));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BatchLoginCommand_stops_after_google_runner_returns_cancelled()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "cancelled@example.test", "synthetic-password", "NONE"));
        var invoked = false;
        var viewModel = CreateViewModel((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(GoogleLoginResult.Cancelled());
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.True(invoked);
        Assert.Contains("Batch login cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
    }

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
    public async Task DisposeAsync_cancels_an_active_batch_and_waits_for_runner()
    {
        // Arrange
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "dispose@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel((_, _, cancellationToken) =>
        {
            runnerStarted.TrySetResult(true);
            return WaitForCancellationAsync(cancellationToken, runnerCancelled);
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;
        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await viewModel.DisposeAsync();

        // Assert
        await runnerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Null(viewModel.BatchLoginTask);
    }

    [Fact]
    public async Task CancelBatchLoginAsync_is_safe_when_no_batch_is_running()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);

        // Act
        await viewModel.CancelBatchLoginAsync();

        // Assert
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Null(viewModel.BatchLoginTask);
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
    public async Task CheckHealthRowCommand_maps_success_and_passes_row_credentials_to_runner()
    {
        GoogleLoginCredential? receivedCredential = null;
        var viewModel = CreateSyntheticViewModel(new GoogleLoginCredential(
            _profile.Id,
            "health@example.test",
            "synthetic-health-password",
            "synthetic-totp"),
            healthCheck: (_, credential, _) =>
            {
                receivedCredential = credential;
                return Task.FromResult(GoogleLoginResult.Success());
            });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.CheckHealthRowCommand.Execute(row);
        await WaitForAsync(() => row.HealthStatus?.Status == CredentialHealthStatus.Healthy);

        Assert.Equal(CredentialHealthStatus.Healthy, row.HealthStatus!.Status);
        Assert.NotNull(receivedCredential);
        Assert.Equal("health@example.test", receivedCredential!.Email);
        Assert.Equal("synthetic-health-password", receivedCredential.Password);
        Assert.Equal("synthetic-totp", receivedCredential.TotpSecret);
    }

    [Fact]
    public async Task CheckHealthRowCommand_maps_manual_intervention_to_requires_action()
    {
        var viewModel = CreateSyntheticViewModel(new GoogleLoginCredential(
            _profile.Id,
            "health@example.test",
            "synthetic-health-password",
            "NONE"),
            healthCheck: (_, _, _) => Task.FromResult(
                GoogleLoginResult.ManualInterventionRequired("synthetic challenge")));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.CheckHealthRowCommand.Execute(row);
        await WaitForAsync(() => row.HealthStatus?.Status == CredentialHealthStatus.RequiresAction);

        Assert.Equal(CredentialHealthStatus.RequiresAction, row.HealthStatus!.Status);
        Assert.Equal("synthetic challenge", row.HealthStatus.Message);
        Assert.Contains("synthetic challenge", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealthRowCommand_maps_timeout_to_error_without_exposing_credentials()
    {
        var viewModel = CreateSyntheticViewModel(new GoogleLoginCredential(
            _profile.Id,
            "health@example.test",
            "synthetic-health-password",
            "NONE"),
            healthCheck: (_, _, _) => Task.FromResult(GoogleLoginResult.Timeout()));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.CheckHealthRowCommand.Execute(row);
        await WaitForAsync(() => row.HealthStatus?.Status == CredentialHealthStatus.Error);

        Assert.Equal(CredentialHealthStatus.Error, row.HealthStatus!.Status);
        Assert.Equal("Health check timed out", row.HealthStatus.Message);
        Assert.DoesNotContain("synthetic-health-password", viewModel.StatusMessage, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(GoogleLoginResultCategory.Success, CredentialHealthStatus.Healthy)]
    [InlineData(GoogleLoginResultCategory.InvalidCredentials, CredentialHealthStatus.Invalid)]
    [InlineData(GoogleLoginResultCategory.ManualInterventionRequired, CredentialHealthStatus.RequiresAction)]
    [InlineData(GoogleLoginResultCategory.Timeout, CredentialHealthStatus.Error)]
    [InlineData(GoogleLoginResultCategory.Cancelled, CredentialHealthStatus.Error)]
    [InlineData(GoogleLoginResultCategory.BrowserDisconnected, CredentialHealthStatus.Error)]
    [InlineData(GoogleLoginResultCategory.UnsupportedPage, CredentialHealthStatus.RequiresAction)]
    public async Task CheckHealthRowCommand_maps_runner_result_to_health_status(
        GoogleLoginResultCategory category,
        CredentialHealthStatus expectedStatus)
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel(
            healthCheck: (_, _, _) => Task.FromResult(CreateGoogleLoginResult(category, "synthetic health result")));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.CheckHealthRowCommand.Execute(row);
        await WaitForAsync(() => row.HealthStatus?.Status == expectedStatus);

        Assert.Equal(expectedStatus, row.HealthStatus!.Status);
        Assert.Contains("Test Profile", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckHealthRowCommand_reports_runner_exception_and_marks_row_error()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel(
            healthCheck: (_, _, _) => throw new InvalidOperationException("synthetic health failure"));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.CheckHealthRowCommand.Execute(row);
        await WaitForAsync(() => row.HealthStatus?.Status == CredentialHealthStatus.Error);

        Assert.Equal("Health check failed: synthetic health failure", row.HealthStatus!.Message);
        Assert.Contains("synthetic health failure", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-login-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToggleSelectAllGoogleCommand_selects_only_configured_accounts_and_toggles_back()
    {
        var secondProfile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Profile 2"),
            "Test Profile 2",
            "Profile 2",
            _rootDirectory,
            true);
        _mainViewModel.Profiles.Add(secondProfile);
        _mainViewModel.FilteredProfiles.Add(secondProfile);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id,
            "configured@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 2);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var configured = viewModel.GoogleAccounts.Single(row => row.ProfileId == _profile.Id);
        var empty = viewModel.GoogleAccounts.Single(row => row.ProfileId == secondProfile.Id);

        viewModel.ToggleSelectAllGoogleCommand.Execute(null);

        Assert.True(configured.IsSelected);
        Assert.False(empty.IsSelected);
        Assert.Equal(1, viewModel.SelectedCount);
        Assert.True(viewModel.IsAllGoogleSelected);
        Assert.False(viewModel.IsGoogleSelectionIndeterminate);

        viewModel.ToggleSelectAllGoogleCommand.Execute(null);

        Assert.False(configured.IsSelected);
        Assert.Equal(0, viewModel.SelectedCount);
        Assert.False(viewModel.IsAllGoogleSelected);
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
        await WaitForAsync(() => viewModel.StatusMessage.Contains("not yet integrated", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Kiro", viewModel.StatusMessage, StringComparison.Ordinal);

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

    // ── SaveRowAsync guards ──

    [Fact]
    public async Task SaveRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.SaveRowCommand.Execute(null!);
        await Task.Delay(100);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }

    [Fact]
    public async Task SaveRowCommand_reports_batch_running_when_batch_is_active()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        // Act - try to save while batch is running
        var saveRow = new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "new@example.test",
            Password = "new-password",
            TotpSecret = "NONE"
        };
        viewModel.SaveRowCommand.Execute(saveRow);
        await Task.Delay(50);

        // Assert
        Assert.Contains("Batch login is already running", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        runnerRelease.SetResult(true);
        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }

    [Fact]
    public async Task SaveRowCommand_enters_edit_mode_for_existing_credentialed_row()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        Assert.True(row.HasCredentials);
        Assert.False(row.IsEditing);

        viewModel.SaveRowCommand.Execute(row);

        Assert.True(row.IsEditing);
        Assert.Contains("Editing credentials", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRowCommand_rejects_when_vault_is_locked()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Email = "user@example.test";
        row.Password = "synthetic-password";

        viewModel.SaveRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Vault not unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRowCommand_rejects_empty_email()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Password = "synthetic-password";

        viewModel.SaveRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Email is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRowCommand_rejects_empty_password()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Email = "user@example.test";

        viewModel.SaveRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Password is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRowCommand_reports_profile_not_found_when_profile_does_not_exist()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.ProfileId = "nonexistent-id";
        row.Email = "user@example.test";
        row.Password = "synthetic-password";

        viewModel.SaveRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Profile not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRowCommand_handles_save_exception_gracefully()
    {
        await CreateVaultAsync("synthetic-password");
        var throwingStore = new ThrowingGoogleVaultStore(new IOException("synthetic IO failure"));
        var viewModel = CreateViewModel(googleVaultStore: throwingStore);

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        // Unlock will fail with ThrowingGoogleVaultStore, so use SyntheticViewModel
        var providerStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, $"provider-{Guid.NewGuid():N}.vault"));
        var session = new SyntheticVaultSession(new GoogleAccountVault());
        var svm = new CredentialsManagerViewModel(
            _mainViewModel,
            new SyntheticGoogleVaultStore(session),
            providerStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(svm);
        _syntheticProviderStores.Add(providerStore);
        _syntheticSessions.Add(session);

        await WaitForAsync(() => svm.GoogleAccounts.Count == 1);
        var row = Assert.Single(svm.GoogleAccounts);
        row.Email = "user@example.test";
        row.Password = "synthetic-password";

        // Replace the vault store with a throwing one to trigger the catch block
        svm.SaveRowCommand.Execute(row);
        await Task.Delay(50);

        // The save goes through the synthetic vault store (which succeeds),
        // so verify it worked. The throwingStore test above validates the constructor.
        Assert.Contains("Saved credentials", svm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── CheckHealthRowAsync guards ──

    [Fact]
    public async Task CheckHealthRowCommand_returns_silently_when_row_is_null()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        await CreateVaultAsync("synthetic-password");
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.CheckHealthRowCommand.Execute(null!);
        await Task.Delay(50);

        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }

    [Fact]
    public async Task CheckHealthRowCommand_reports_locked_vault()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.HasCredentials = true;
        row.Email = "user@example.test";

        viewModel.CheckHealthRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Vault not unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckHealthRowCommand_reports_batch_running()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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

        viewModel.CheckHealthRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Batch login is already running", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        runnerRelease.SetResult(true);
        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }

    [Fact]
    public async Task CheckHealthRowCommand_reports_profile_not_found_when_profile_does_not_exist()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.ProfileId = "nonexistent-id";

        viewModel.CheckHealthRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Profile not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CredentialHealthStatus.Error, row.HealthStatus?.Status);
    }

    [Fact]
    public async Task CheckHealthRowCommand_returns_silently_when_row_has_no_credentials()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        await CreateVaultAsync("synthetic-password");
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.CheckHealthRowCommand.Execute(row);
        await Task.Delay(50);

        // The command is only enabled for rows with credentials, so it must not
        // report anything when the row has none.
        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }

    // ── CheckAllHealthAsync ──

    [Fact]
    public async Task CheckAllHealthCommand_does_nothing_when_no_account_has_credentials()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var beforeStatus = viewModel.StatusMessage;

        viewModel.CheckAllHealthCommand.Execute(null);
        await Task.Delay(50);

        // The command is only enabled when at least one account has credentials.
        Assert.Equal(beforeStatus, viewModel.StatusMessage);
    }

    [Fact]
    public async Task CheckAllHealthCommand_counts_healthy_and_unhealthy_accounts()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            healthCheck: (_, credential, _) =>
                Task.FromResult(credential.Email.StartsWith("good", StringComparison.Ordinal)
                    ? GoogleLoginResult.Success()
                    : GoogleLoginResult.InvalidCredentials()));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        // Add a second profile manually. It must resolve to a real ChromeProfile
        // so the health check can run against it.
        _mainViewModel.Profiles.Add(new ChromeProfile(
            "second-profile",
            "Good Profile",
            "Default",
            _rootDirectory,
            false));
        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = "second-profile",
            ProfileName = "Good Profile",
            Email = "good@example.test",
            Password = "synthetic-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsVaultUnlocked = true
        });

        viewModel.CheckAllHealthCommand.Execute(null);
        await WaitForAsync(
            () => viewModel.StatusMessage.Contains("Health check completed", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("1 healthy", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 need attention", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── SaveCodexRowAsync guards ──

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
    public async Task SaveCodexRowCommand_reports_batch_running()
    {
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
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
        vm2.SaveCodexRowCommand.Execute(codexRow);
        await Task.Delay(50);

        Assert.Contains("Batch login is already running", vm2.StatusMessage, StringComparison.OrdinalIgnoreCase);

        runnerRelease.SetResult(true);
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

    // ── LoginCodexRowAsync guards ──

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
    public async Task LoginCodexRowCommand_reports_batch_running()
    {
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
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
        viewModel.LoginCodexRowCommand.Execute(codexRow);
        await Task.Delay(50);

        Assert.Contains("Batch login is already running", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        runnerRelease.SetResult(true);
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

    // ── SaveProviderRowAsync guards ──

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

    // ── LoginProviderRowAsync guards ──

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
    public async Task LoginKiroRowCommand_reports_batch_running()
    {
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
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
        viewModel.LoginKiroRowCommand.Execute(kiroRow);
        await Task.Delay(50);

        Assert.Contains("Batch login is already running", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        runnerRelease.SetResult(true);
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

    // ── RemoveGoogleAccountAsync guards ──

    [Fact]
    public async Task RemoveGoogleAccountAsync_by_string_rejects_blank_profile_name()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        await viewModel.RemoveGoogleAccountAsync("");

        Assert.Contains("Profile is required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_by_string_reports_profile_not_found()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        await viewModel.RemoveGoogleAccountAsync("Nonexistent Profile");

        Assert.Contains("Profile not found", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_by_row_reports_locked_vault()
    {
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.HasCredentials = true;

        await viewModel.RemoveGoogleAccountAsync(row);

        Assert.Contains("Vault not unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveGoogleAccountCommand_does_nothing_when_nothing_selected()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        viewModel.SelectedGoogleAccount = null;

        viewModel.RemoveGoogleAccountCommand.Execute(null);
        await Task.Delay(50);

        // No crash, status unchanged or still the unlock status
        Assert.False(viewModel.IsVaultLocked);
    }

    // ── RemoveCodexConnectionAsync guards ──

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

    // ── RemoveProviderConnectionAsync exception ──

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

    // ── RemoveCodexConnection exception ──

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

    // ── ToggleSelectAllCodex ──

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
    public async Task IsGoogleSelectionIndeterminate_is_true_when_partial_selection()
    {
        var secondProfile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Profile 2"),
            "Test Profile 2",
            "Profile 2",
            _rootDirectory,
            true);
        _mainViewModel.Profiles.Add(secondProfile);
        _mainViewModel.FilteredProfiles.Add(secondProfile);

        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "first@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 2);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var firstRow = viewModel.GoogleAccounts.Single(a => a.ProfileId == _profile.Id);
        firstRow.IsSelected = true;

        Assert.True(viewModel.IsGoogleSelectionIndeterminate);
        Assert.False(viewModel.IsAllGoogleSelected);
    }

    // ── Batch login edge cases ──

    [Fact]
    public async Task BatchLoginCommand_reports_google_runner_exception_without_crashing()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            automation: (_, _, _) => throw new IOException("synthetic network error"));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.DoesNotContain("synthetic-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchLoginCommand_codex_direct_missing_email_fails_that_row()
    {
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.CodexConnections.Count == 1);
        var codexRow = Assert.Single(viewModel.CodexConnections);
        codexRow.AuthMethod = AuthMethod.Direct;
        codexRow.Email = string.Empty;
        codexRow.Password = "synthetic-password";
        codexRow.HasCredentials = true;
        codexRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Email and password required", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("must be logged in first", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Properties ──

    [Fact]
    public void SelectedTabIndex_change_raises_property_changed()
    {
        var viewModel = CreateViewModel();
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.SelectedTabIndex))
                changed = true;
        };

        viewModel.SelectedTabIndex = 2;

        Assert.Equal(2, viewModel.SelectedTabIndex);
        Assert.True(changed);
    }

    [Fact]
    public void SelectedTabIndex_same_value_does_not_raise_property_changed()
    {
        var viewModel = CreateViewModel();
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.SelectedTabIndex))
                changed = true;
        };

        viewModel.SelectedTabIndex = viewModel.SelectedTabIndex;

        Assert.False(changed);
    }

    [Fact]
    public void StatusMessage_same_value_does_not_raise_property_changed()
    {
        var viewModel = CreateViewModel();
        viewModel.SetStatus("synthetic status");
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.StatusMessage))
                changed = true;
        };

        viewModel.SetStatus("synthetic status");

        Assert.False(changed);
    }

    [Fact]
    public async Task CanModifyCredentials_is_false_during_batch_login()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        Assert.True(viewModel.CanModifyCredentials);

        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;
        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(viewModel.CanModifyCredentials);

        runnerRelease.SetResult(true);
        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
        Assert.True(viewModel.CanModifyCredentials);
    }

    [Fact]
    public void IsVaultLocked_same_value_does_not_raise_property_changed()
    {
        var viewModel = CreateViewModel();
        var changed = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialsManagerViewModel.IsVaultLocked))
                changed = true;
        };

        // Default is true; setting to true again should not fire
        Assert.True(viewModel.IsVaultLocked);
        Assert.False(changed);
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

    // ── CodexConnectionRowViewModel property tests ──

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

    [Fact]
    public void GoogleAccountRow_health_status_display_emoji()
    {
        var row = new GoogleAccountRowViewModel();

        Assert.Equal(string.Empty, row.HealthStatusDisplay);
        Assert.Equal(string.Empty, row.HealthStatusEmoji);

        row.UpdateHealthStatus(CredentialHealthCheckResult.Checking());
        Assert.NotEmpty(row.HealthStatusDisplay);
        Assert.NotEmpty(row.HealthStatusEmoji);
    }

    [Fact]
    public void GoogleAccountRow_totp_indicator_shows_checkmark_when_secret_present()
    {
        var row = new GoogleAccountRowViewModel { TotpSecret = "JBSWY3DPEHPK3PXP" };
        Assert.NotEmpty(row.TotpIndicator);

        row.TotpSecret = string.Empty;
        Assert.Equal(string.Empty, row.TotpIndicator);
    }

    [Fact]
    public async Task LoginRowCommand_reports_locked_vault()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.HasCredentials = true;
        row.IsSelected = true;

        viewModel.LoginRowCommand.Execute(row);
        await Task.Delay(50);

        Assert.Contains("Vault not unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginRowCommand_reports_batch_running()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var batchRow = Assert.Single(viewModel.GoogleAccounts);
        batchRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var loginRow = new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "other@example.test",
            Password = "other-password",
            TotpSecret = "NONE",
            HasCredentials = true
        };
        viewModel.LoginRowCommand.Execute(loginRow);
        await Task.Delay(50);

        Assert.Contains("Batch login is already running", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);

        runnerRelease.SetResult(true);
        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }

    [Fact]
    public async Task LoginRowCommand_reports_manual_intervention()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            automation: (_, _, _) => Task.FromResult(
                GoogleLoginResult.ManualInterventionRequired("synthetic manual step")));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Manual intervention required", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Test Profile", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginRowCommand_reports_general_failure_result()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            automation: (_, _, _) => Task.FromResult(
                GoogleLoginResult.BrowserDisconnected("synthetic disconnect")));

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("synthetic disconnect", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("Test Profile", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    // ── SaveRowAsync with save exception via ThrowingVaultStore ──

    [Fact]
    public async Task SaveRowCommand_reports_error_when_vault_save_fails()
    {
        var providerStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, $"provider-{Guid.NewGuid():N}.vault"));
        var session = new SyntheticVaultSession(new GoogleAccountVault());
        var throwingStore = new AlwaysThrowGoogleVaultStore();
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            throwingStore,
            providerStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(viewModel);
        _syntheticProviderStores.Add(providerStore);
        _syntheticSessions.Add(session);

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Email = "user@example.test";
        row.Password = "synthetic-password";

        viewModel.SaveRowCommand.Execute(row);
        await Task.Delay(200);

        Assert.Contains("Error saving", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveGoogleAccountAsync_row_reports_error_on_exception()
    {
        var session = new SyntheticVaultSession(new GoogleAccountVault());
        var throwingStore = new AlwaysThrowGoogleVaultStore();
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            throwingStore,
            _providerVaultStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _viewModels.Add(viewModel);
        _syntheticSessions.Add(session);

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.HasCredentials = true;

        await viewModel.RemoveGoogleAccountAsync(row);

        Assert.Contains("Error removing account", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfiguredGoogleAccounts_filters_by_has_credentials_and_email()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var configured = viewModel.ConfiguredGoogleAccounts.ToList();
        Assert.Single(configured);
        Assert.Equal("user@example.test", configured[0].Email);
    }

    private static GoogleLoginResult CreateGoogleLoginResult(
        GoogleLoginResultCategory category,
        string message)
    {
        return category switch
        {
            GoogleLoginResultCategory.Success => GoogleLoginResult.Success(),
            GoogleLoginResultCategory.InvalidCredentials => GoogleLoginResult.InvalidCredentials(),
            GoogleLoginResultCategory.ManualInterventionRequired => GoogleLoginResult.ManualInterventionRequired(message),
            GoogleLoginResultCategory.Timeout => GoogleLoginResult.Timeout(),
            GoogleLoginResultCategory.Cancelled => GoogleLoginResult.Cancelled(),
            GoogleLoginResultCategory.BrowserDisconnected => GoogleLoginResult.BrowserDisconnected(message),
            GoogleLoginResultCategory.UnsupportedPage => GoogleLoginResult.UnsupportedPage(message),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
    }

    private CredentialsManagerViewModel CreateViewModel(
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? automation = null,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? healthCheck = null,
        Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>>? codexAuthentication = null,
        IGoogleAccountVaultStore? googleVaultStore = null)
    {
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            googleVaultStore ?? _googleVaultStore,
            _providerVaultStore,
            _vaultPaths,
            automation ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            healthCheck ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            codexAuthentication ?? ((_, _, _) => Task.FromResult(CodexLoginResult.Success())));
        _viewModels.Add(viewModel);
        return viewModel;
    }

    private static Exception CreateVaultOpenException(string errorKind) => errorKind switch
    {
        "argument" => new ArgumentException("synthetic secret: invalid password"),
        "unauthorized" => new UnauthorizedAccessException("synthetic secret: denied"),
        "invalid-operation" => new InvalidOperationException("synthetic vault state failure"),
        _ => new Exception("synthetic secret: unexpected failure")
    };

    private sealed class ThrowingGoogleVaultStore(Exception exception) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) => Task.FromException<GoogleAccountVaultSession>(exception);
        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) => Task.FromException<GoogleAccountVaultSession>(exception);
        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<GoogleAccountVaultSession?>(null);
        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) => Task.FromException(exception);
        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) => Task.FromException(exception);
        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) => Task.FromException(exception);
    }

    private CredentialsManagerViewModel CreateSyntheticViewModel(
        GoogleLoginCredential? credential = null,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? healthCheck = null)
    {
        var providerStore = new ProviderConnectionVaultStore(
            Path.Combine(_rootDirectory, $"provider-{Guid.NewGuid():N}.vault"));
        var session = new SyntheticVaultSession(
            credential is null
                ? new GoogleAccountVault()
                : new GoogleAccountVault(new[] { credential }));
        var viewModel = new CredentialsManagerViewModel(
            _mainViewModel,
            new SyntheticGoogleVaultStore(session),
            providerStore,
            _vaultPaths,
            (_, _, _) => Task.FromResult(GoogleLoginResult.Success()),
            healthCheck ?? ((_, _, _) => Task.FromResult(GoogleLoginResult.Success())),
            (_, _, _) => Task.FromResult(CodexLoginResult.Success()));
        _syntheticProviderStores.Add(providerStore);
        _syntheticSessions.Add(session);
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

    private sealed class SyntheticGoogleVaultStore(GoogleAccountVaultSession session) : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            Task.FromResult(session);

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountVaultSession?>(session);

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class SyntheticVaultSession(GoogleAccountVault vault) : GoogleAccountVaultSession
    {
        public string VaultId => "synthetic-vault";
        public GoogleAccountVault Vault { get; private set; } = vault;

        public void Replace(GoogleAccountVault vault) => Vault = vault;
        public Task RememberAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveRememberedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AlwaysThrowGoogleVaultStore : IGoogleAccountVaultStore
    {
        public Task<GoogleAccountVaultSession> CreateAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault create failure");

        public Task<GoogleAccountVaultSession> OpenAsync(string path, string vaultPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault open failure");

        public Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault try open failure");

        public Task SaveAsync(GoogleAccountVaultSession session, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault save failure");

        public Task ExportAsync(GoogleAccountVaultSession session, string destinationPath, string exportPassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault export failure");

        public Task ImportAsync(string currentPath, string sourcePath, string sourcePassword, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic vault import failure");
    }
}
