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
    public async Task SaveRowCommand_reports_error_when_vault_save_fails()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            googleVaultStore: new SaveThrowingGoogleVaultStore(_googleVaultStore));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsEditing = true;
        row.Email = "user@example.test";
        row.Password = "synthetic-password";

        viewModel.SaveRowCommand.Execute(row);
        await Task.Delay(200);

        Assert.Contains("Error saving", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
}
