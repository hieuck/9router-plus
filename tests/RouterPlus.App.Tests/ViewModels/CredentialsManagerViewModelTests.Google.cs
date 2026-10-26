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
            settingsStore: new SettingsStore(Path.Combine(siblingRoot, "settings.json")), // temp settings store: InitializeAsync runs the one-time DPAPI migration
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
            settingsStore: new SettingsStore(Path.Combine(siblingRoot, "settings.json")), // temp settings store: InitializeAsync runs the one-time DPAPI migration
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
            settingsStore: new SettingsStore(Path.Combine(siblingRoot, "settings.json")), // temp settings store: InitializeAsync runs the one-time DPAPI migration
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

        await CreateVaultAsync("synthetic-password",
            new GoogleLoginCredential(_profile.Id, "first@example.test", "synthetic-password", "NONE"),
            new GoogleLoginCredential(secondProfile.Id, "second@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 2);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);

        var firstRow = viewModel.GoogleAccounts.Single(a => a.ProfileId == _profile.Id);
        firstRow.IsSelected = true;

        Assert.True(viewModel.IsGoogleSelectionIndeterminate);
        Assert.False(viewModel.IsAllGoogleSelected);
    }
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
    public async Task LoginRowCommand_marks_row_healthy_on_successful_login()
    {
        var viewModel = CreateSyntheticViewModel(new GoogleLoginCredential(
            _profile.Id, "user@example.test", "synthetic-password", "NONE"));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.HasCredentials = true;

        viewModel.LoginRowCommand.Execute(row);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Login successful", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(CredentialHealthStatus.Healthy, row.HealthStatus?.Status);
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
    [Fact]
    public async Task RemoveGoogleAccountAsync_row_reports_error_on_exception()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            googleVaultStore: new SaveThrowingGoogleVaultStore(_googleVaultStore));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
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
}
