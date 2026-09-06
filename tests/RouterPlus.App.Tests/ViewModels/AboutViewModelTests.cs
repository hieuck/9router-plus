using RouterPlus.App.ViewModels;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for AboutViewModel
/// Simple ViewModel with static product information
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.NotNull(viewModel.ProductName);
        Assert.NotNull(viewModel.Version);
        Assert.NotNull(viewModel.LicenseName);
        Assert.NotNull(viewModel.RepositoryUri);
        Assert.NotNull(viewModel.HelpUri);
        Assert.NotNull(viewModel.SecurityUri);
        Assert.NotNull(viewModel.ReleaseUri);
    }

    [Fact]
    public void ProductName_HasExpectedValue()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.Equal("9Router Profile Tool", viewModel.ProductName);
    }

    [Fact]
    public void Version_IsNotEmpty()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Version));
    }

    [Fact]
    public void LicenseName_HasExpectedValue()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.Equal("MIT License", viewModel.LicenseName);
    }

    [Fact]
    public void RepositoryUri_IsValid()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.True(viewModel.RepositoryUri.IsAbsoluteUri);
        Assert.Contains("github.com", viewModel.RepositoryUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelpUri_IsValid()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.True(viewModel.HelpUri.IsAbsoluteUri);
    }

    [Fact]
    public void SecurityUri_IsValid()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.True(viewModel.SecurityUri.IsAbsoluteUri);
    }

    [Fact]
    public void ReleaseUri_IsValid()
    {
        // Arrange & Act
        var viewModel = new AboutViewModel();

        // Assert
        Assert.True(viewModel.ReleaseUri.IsAbsoluteUri);
    }

    [Fact]
    public void Properties_AreReadOnly()
    {
        // Arrange
        var viewModel = new AboutViewModel();
        var type = viewModel.GetType();

        // Act & Assert - Verify properties don't have setters
        Assert.Null(type.GetProperty(nameof(AboutViewModel.ProductName))?.SetMethod);
        Assert.Null(type.GetProperty(nameof(AboutViewModel.Version))?.SetMethod);
        Assert.Null(type.GetProperty(nameof(AboutViewModel.LicenseName))?.SetMethod);
        Assert.Null(type.GetProperty(nameof(AboutViewModel.RepositoryUri))?.SetMethod);
        Assert.Null(type.GetProperty(nameof(AboutViewModel.HelpUri))?.SetMethod);
        Assert.Null(type.GetProperty(nameof(AboutViewModel.SecurityUri))?.SetMethod);
        Assert.Null(type.GetProperty(nameof(AboutViewModel.ReleaseUri))?.SetMethod);
    }
}
