using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests.Chrome;

public sealed class ProfileHealthStatusTests
{
    [Fact]
    public void Healthy_CreatesHealthyStatus()
    {
        var status = ProfileHealthStatus.Healthy("All good");

        Assert.Equal(HealthLevel.Healthy, status.Level);
        Assert.Equal("All good", status.Message);
        Assert.Empty(status.Issues);
        Assert.InRange(status.LastChecked, DateTime.UtcNow.AddSeconds(-1), DateTime.UtcNow);
    }

    [Fact]
    public void FromIssues_EmptyList_CreatesHealthyStatus()
    {
        var status = ProfileHealthStatus.FromIssues(Array.Empty<HealthIssue>());

        Assert.Equal(HealthLevel.Healthy, status.Level);
        Assert.Equal("Profile healthy", status.Message);
        Assert.Empty(status.Issues);
    }

    [Fact]
    public void FromIssues_InfoOnly_CreatesHealthyStatus()
    {
        var issues = new[]
        {
            HealthIssue.Info(HealthCategory.Filesystem, "Info only")
        };

        var status = ProfileHealthStatus.FromIssues(issues);

        Assert.Equal(HealthLevel.Healthy, status.Level);
        Assert.Single(status.Issues);
    }

    [Fact]
    public void FromIssues_WarningPresent_CreatesWarningStatus()
    {
        var issues = new[]
        {
            HealthIssue.Info(HealthCategory.Filesystem, "Info"),
            HealthIssue.Warning(HealthCategory.Vault, "Warning")
        };

        var status = ProfileHealthStatus.FromIssues(issues);

        Assert.Equal(HealthLevel.Warning, status.Level);
        Assert.Equal("2 warning(s) detected", status.Message);
        Assert.Equal(2, status.Issues.Count);
    }

    [Fact]
    public void FromIssues_ErrorPresent_CreatesErrorStatus()
    {
        var issues = new[]
        {
            HealthIssue.Warning(HealthCategory.Vault, "Warning"),
            HealthIssue.Error(HealthCategory.Filesystem, "Error")
        };

        var status = ProfileHealthStatus.FromIssues(issues);

        Assert.Equal(HealthLevel.Error, status.Level);
        Assert.Equal("1 error(s) detected", status.Message);
        Assert.Equal(2, status.Issues.Count);
    }

    [Fact]
    public void FromIssues_MultipleErrors_CountsOnlyErrors()
    {
        var issues = new[]
        {
            HealthIssue.Error(HealthCategory.Filesystem, "Error 1"),
            HealthIssue.Warning(HealthCategory.Vault, "Warning"),
            HealthIssue.Error(HealthCategory.Credentials, "Error 2")
        };

        var status = ProfileHealthStatus.FromIssues(issues);

        Assert.Equal(HealthLevel.Error, status.Level);
        Assert.Equal("2 error(s) detected", status.Message);
    }
}
