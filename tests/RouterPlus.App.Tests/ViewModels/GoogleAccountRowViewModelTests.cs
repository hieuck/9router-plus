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
    public void Constructor_WithEmail_SetsProperties()
    {
        // Arrange & Act
        var viewModel = new GoogleAccountRowViewModel
        {
            Email = "test@example.com",
            HasTotpSecret = true
        };

        // Assert
        Assert.Equal("test@example.com", viewModel.Email);
        Assert.True(viewModel.HasTotpSecret);
    }

    [Fact]
    public void Constructor_WithoutTotpSecret_HasTotpSecretIsFalse()
    {
        // Arrange & Act
        var viewModel = new GoogleAccountRowViewModel
        {
            Email = "test@example.com",
            HasTotpSecret = false
        };

        // Assert
        Assert.Equal("test@example.com", viewModel.Email);
        Assert.False(viewModel.HasTotpSecret);
    }

    [Theory]
    [InlineData("user@gmail.com", true)]
    [InlineData("admin@example.com", false)]
    [InlineData("test@domain.org", true)]
    public void Properties_CanBeSetAndRead(string email, bool hasTotp)
    {
        // Arrange & Act
        var viewModel = new GoogleAccountRowViewModel
        {
            Email = email,
            HasTotpSecret = hasTotp
        };

        // Assert
        Assert.Equal(email, viewModel.Email);
        Assert.Equal(hasTotp, viewModel.HasTotpSecret);
    }
}
