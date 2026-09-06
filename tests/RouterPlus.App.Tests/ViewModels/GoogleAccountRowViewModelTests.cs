using RouterPlus.App.ViewModels;
using RouterPlus.Core.Security;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for GoogleAccountRowViewModel
/// Simple ViewModel for credentials management UI
/// </summary>
public sealed class GoogleAccountRowViewModelTests
{
    [Fact]
    public void Properties_default_to_expected_values()
    {
        // Arrange & Act
        var viewModel = new GoogleAccountRowViewModel();

        // Assert
        Assert.Equal(string.Empty, viewModel.ProfileId);
        Assert.Equal(string.Empty, viewModel.ProfileName);
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.TotpSecret);
        Assert.False(viewModel.IsSelected);
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.HasCredentials);
        Assert.False(viewModel.IsVaultUnlocked);
        Assert.Null(viewModel.HealthStatus);
    }

    [Fact]
    public void ProfileId_property_updates_and_raises_PropertyChanged()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();
        var propertyChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GoogleAccountRowViewModel.ProfileId))
                propertyChanged = true;
        };

        // Act
        viewModel.ProfileId = "test-id";

        // Assert
        Assert.Equal("test-id", viewModel.ProfileId);
        Assert.True(propertyChanged);
    }

    [Fact]
    public void Email_property_updates_correctly()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();

        // Act
        viewModel.Email = "test@example.com";

        // Assert
        Assert.Equal("test@example.com", viewModel.Email);
    }

    [Fact]
    public void TotpSecret_property_updates_correctly()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();

        // Act
        viewModel.TotpSecret = "JBSWY3DPEHPK3PXP";

        // Assert
        Assert.Equal("JBSWY3DPEHPK3PXP", viewModel.TotpSecret);
    }

    [Fact]
    public void TotpSecret_updates_TotpIndicator()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();
        var totpIndicatorChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GoogleAccountRowViewModel.TotpIndicator))
                totpIndicatorChanged = true;
        };

        // Act
        viewModel.TotpSecret = "JBSWY3DPEHPK3PXP";

        // Assert
        Assert.True(totpIndicatorChanged);
        Assert.Equal("✓", viewModel.TotpIndicator);
    }

    [Fact]
    public void TotpIndicator_returns_checkmark_when_totp_present()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            TotpSecret = "JBSWY3DPEHPK3PXP"
        };

        // Act
        var indicator = viewModel.TotpIndicator;

        // Assert
        Assert.Equal("✓", indicator);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("JBSWY3DPEHPK3PXP", "✓")]
    [InlineData("ABCD1234", "✓")]
    public void TotpIndicator_scenarios(string totpSecret, string expectedIndicator)
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            TotpSecret = totpSecret
        };

        // Act
        var indicator = viewModel.TotpIndicator;

        // Assert
        Assert.Equal(expectedIndicator, indicator);
    }

    [Fact]
    public void IsSelected_updates_and_raises_PropertyChanged()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();
        var propertyChanged = false;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GoogleAccountRowViewModel.IsSelected))
                propertyChanged = true;
        };

        // Act
        viewModel.IsSelected = true;

        // Assert
        Assert.True(viewModel.IsSelected);
        Assert.True(propertyChanged);
    }

    [Theory]
    [InlineData(false, false, false, false)] // Vault locked, no credentials, not editing
    [InlineData(true, false, false, true)]   // Vault unlocked, no credentials, not editing
    [InlineData(true, true, false, false)]   // Vault unlocked, has credentials, not editing
    [InlineData(true, true, true, true)]     // Vault unlocked, has credentials, editing
    [InlineData(false, true, true, false)]   // Vault locked, has credentials, editing
    public void IsEditable_scenarios(bool isVaultUnlocked, bool hasCredentials, bool isEditing, bool expectedEditable)
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            IsVaultUnlocked = isVaultUnlocked,
            HasCredentials = hasCredentials,
            IsEditing = isEditing
        };

        // Act
        var isEditable = viewModel.IsEditable;

        // Assert
        Assert.Equal(expectedEditable, isEditable);
    }

    [Fact]
    public void ActionButtonText_returns_Edit_when_has_credentials_and_not_editing()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            HasCredentials = true,
            IsEditing = false
        };

        // Act
        var buttonText = viewModel.ActionButtonText;

        // Assert
        Assert.Equal("Edit", buttonText);
    }

    [Fact]
    public void ActionButtonText_returns_Save_when_editing()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            HasCredentials = true,
            IsEditing = true
        };

        // Act
        var buttonText = viewModel.ActionButtonText;

        // Assert
        Assert.Equal("Save", buttonText);
    }

    [Fact]
    public void ActionButtonText_returns_Save_when_no_credentials()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            HasCredentials = false
        };

        // Act
        var buttonText = viewModel.ActionButtonText;

        // Assert
        Assert.Equal("Save", buttonText);
    }

    [Fact]
    public void UpdateHealthStatus_updates_HealthStatus_property()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();
        var result = CredentialHealthCheckResult.Healthy("Test message");

        // Act
        viewModel.UpdateHealthStatus(result);

        // Assert
        Assert.Equal(result, viewModel.HealthStatus);
        Assert.Equal("✓ Healthy", viewModel.HealthStatusDisplay);
        Assert.Equal("✓", viewModel.HealthStatusEmoji);
    }

    [Fact]
    public void HealthStatusDisplay_returns_empty_when_null()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();

        // Act
        var display = viewModel.HealthStatusDisplay;

        // Assert
        Assert.Equal(string.Empty, display);
    }

    [Fact]
    public void HealthStatusEmoji_returns_empty_when_null()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel();

        // Act
        var emoji = viewModel.HealthStatusEmoji;

        // Assert
        Assert.Equal(string.Empty, emoji);
    }

    [Fact]
    public void TogglePasswordVisibility_when_editable_toggles_state()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            IsVaultUnlocked = true,
            IsEditing = true
        };
        var initialVisible = viewModel.IsPasswordVisible;

        // Act
        viewModel.TogglePasswordVisibility();

        // Assert
        Assert.NotEqual(initialVisible, viewModel.IsPasswordVisible);
    }

    [Fact]
    public void ResetSensitiveVisibility_hides_sensitive_fields()
    {
        // Arrange
        var viewModel = new GoogleAccountRowViewModel
        {
            IsVaultUnlocked = true,
            IsEditing = true
        };
        viewModel.TogglePasswordVisibility();
        viewModel.ToggleTotpSecretVisibility();

        // Act
        viewModel.ResetSensitiveVisibility();

        // Assert
        Assert.False(viewModel.IsPasswordVisible);
        Assert.False(viewModel.IsTotpSecretVisible);
    }
}
