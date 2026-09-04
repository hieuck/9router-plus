using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;

namespace RouterPlus.Core.Tests.Chrome;

public class ProfileHealthChecker_CredentialsTests
{
    [Fact]
    public void CheckCredentialsHealth_NoVault_ReturnsInfo()
    {
        var profile = new ChromeProfile("profile-123", "Test", "Profile 1", @"C:\UserData", false);
        var checker = new ProfileHealthChecker();

        var issues = checker.CheckCredentialsHealth(profile, vault: null);

        var issue = Assert.Single(issues);
        Assert.Equal(IssueSeverity.Info, issue.Severity);
        Assert.Contains("vault not loaded", issue.Description);
    }

    [Fact]
    public void CheckCredentialsHealth_NoCredentialForProfile_ReturnsWarning()
    {
        var profile = new ChromeProfile("profile-123", "Test", "Profile 1", @"C:\UserData", false);
        var vault = new GoogleAccountVault();
        var checker = new ProfileHealthChecker();

        var issues = checker.CheckCredentialsHealth(profile, vault);

        var issue = Assert.Single(issues);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("No Google account", issue.Description);
    }

    [Fact]
    public void CheckCredentialsHealth_CredentialExists_NoIssues()
    {
        var profile = new ChromeProfile("profile-123", "Test", "Profile 1", @"C:\UserData", false);
        var credential = new GoogleLoginCredential("profile-123", "test@gmail.com", "pass123", "TOTP123");
        var vault = new GoogleAccountVault(new[] { credential });
        var checker = new ProfileHealthChecker();

        var issues = checker.CheckCredentialsHealth(profile, vault);

        Assert.Empty(issues);
    }
}
