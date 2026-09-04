using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests.Chrome;

public sealed class HealthIssueTests
{
    [Fact]
    public void Info_CreatesInfoIssue()
    {
        var issue = HealthIssue.Info(HealthCategory.Filesystem, "Test info");

        Assert.Equal(HealthCategory.Filesystem, issue.Category);
        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Equal("Test info", issue.Description);
        Assert.Null(issue.Recommendation);
    }

    [Fact]
    public void Warning_CreatesWarningIssueWithRecommendation()
    {
        var issue = HealthIssue.Warning(
            HealthCategory.Vault,
            "Test warning",
            "Fix it");

        Assert.Equal(HealthCategory.Vault, issue.Category);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Equal("Test warning", issue.Description);
        Assert.Equal("Fix it", issue.Recommendation);
    }

    [Fact]
    public void Error_CreatesErrorIssue()
    {
        var issue = HealthIssue.Error(
            HealthCategory.Credentials,
            "Test error",
            "Recover");

        Assert.Equal(HealthCategory.Credentials, issue.Category);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal("Test error", issue.Description);
        Assert.Equal("Recover", issue.Recommendation);
    }
}
