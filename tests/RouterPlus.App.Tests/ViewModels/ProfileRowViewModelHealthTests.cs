using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class ProfileRowViewModelHealthTests
{
    [Fact]
    public void HealthStatus_InitiallyNull()
    {
        var profile = new ChromeProfile("id", "Test", "Profile 1", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, Array.Empty<ProviderDefinition>());

        Assert.Null(viewModel.HealthStatus);
    }

    [Fact]
    public void HealthStatus_SetHealthy_UpdatesProperties()
    {
        var profile = new ChromeProfile("id", "Test", "Profile 1", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, Array.Empty<ProviderDefinition>());

        var status = ProfileHealthStatus.Healthy("All good");
        viewModel.HealthStatus = status;

        Assert.Equal(status, viewModel.HealthStatus);
        Assert.Equal("✓", viewModel.HealthStatusIcon);
        Assert.Equal("All good", viewModel.HealthStatusText);
        Assert.False(viewModel.HasHealthIssues);
    }

    [Fact]
    public void HealthStatus_SetWarning_UpdatesProperties()
    {
        var profile = new ChromeProfile("id", "Test", "Profile 1", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, Array.Empty<ProviderDefinition>());

        var issues = new[] { HealthIssue.Warning(HealthCategory.Filesystem, "Warning") };
        var status = ProfileHealthStatus.FromIssues(issues);
        viewModel.HealthStatus = status;

        Assert.Equal("⚠", viewModel.HealthStatusIcon);
        Assert.Contains("warning", viewModel.HealthStatusText);
        Assert.True(viewModel.HasHealthIssues);
    }

    [Fact]
    public void HealthStatus_SetError_UpdatesProperties()
    {
        var profile = new ChromeProfile("id", "Test", "Profile 1", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, Array.Empty<ProviderDefinition>());

        var issues = new[] { HealthIssue.Error(HealthCategory.Filesystem, "Error") };
        var status = ProfileHealthStatus.FromIssues(issues);
        viewModel.HealthStatus = status;

        Assert.Equal("✗", viewModel.HealthStatusIcon);
        Assert.Contains("error", viewModel.HealthStatusText);
        Assert.True(viewModel.HasHealthIssues);
    }

    [Fact]
    public void HealthStatus_NullStatus_ReturnsUnknownIcon()
    {
        var profile = new ChromeProfile("id", "Test", "Profile 1", "C:\\UserData", false);
        var viewModel = new ProfileRowViewModel(profile, Array.Empty<ProviderDefinition>());

        Assert.Equal("?", viewModel.HealthStatusIcon);
        Assert.Equal("Unknown", viewModel.HealthStatusText);
        Assert.False(viewModel.HasHealthIssues);
    }
}
