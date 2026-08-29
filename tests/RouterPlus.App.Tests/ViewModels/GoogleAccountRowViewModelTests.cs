using RouterPlus.App.ViewModels;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// Tests for GoogleAccountRowViewModel - Phase 5 Step 5.2
/// Simple ViewModel without complex dependencies
/// </summary>
public sealed class GoogleAccountRowViewModelTests
{
    [Fact]
    public void Properties_default_to_expected_values()
    {
        // Arrange & Act
        var viewModel = new GoogleAccountRowViewModel();

        // Assert
        Assert.Equal(string.Empty, viewModel.ProfileName);
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(string.Empty, viewModel.TotpSecret);
        Assert.False(viewModel.IsSelected);
        Assert.False(viewModel.IsEditing);
        Assert.False(viewModel.HasCredentials);
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
}
