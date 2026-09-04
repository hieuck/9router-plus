using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests.Chrome;

public sealed class ProfileHealthCheckerTests
{
    [Fact]
    public void CheckFilesystemHealth_ProfileDirectoryMissing_ReturnsError()
    {
        var profile = new ChromeProfile(
            "test-id",
            "Test Profile",
            "NonExistentDirectory",
            Path.Combine(Path.GetTempPath(), "NonExistentUserData"),
            false);
        var checker = new ProfileHealthChecker();

        var issues = checker.CheckFilesystemHealth(profile);

        var issue = Assert.Single(issues);
        Assert.Equal(HealthCategory.Filesystem, issue.Category);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("directory not found", issue.Description);
        Assert.NotNull(issue.Recommendation);
    }

    [Fact]
    public void CheckFilesystemHealth_ProfileDirectoryExists_NoDirectoryError()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        try
        {
            var profile = new ChromeProfile(
                "test-id",
                "Test Profile",
                "Profile 1",
                tempUserData,
                false);
            var checker = new ProfileHealthChecker();

            var issues = checker.CheckFilesystemHealth(profile);

            Assert.DoesNotContain(issues, i => i.Description.Contains("directory not found"));
        }
        finally
        {
            Directory.Delete(tempUserData, true);
        }
    }

    [Fact]
    public void CheckFilesystemHealth_LocalStateMissing_ReturnsWarning()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        try
        {
            var profile = new ChromeProfile(
                "test-id",
                "Test Profile",
                "Profile 1",
                tempUserData,
                false);
            var checker = new ProfileHealthChecker();

            var issues = checker.CheckFilesystemHealth(profile);

            Assert.Contains(issues, i =>
                i.Category == HealthCategory.Filesystem &&
                i.Severity == IssueSeverity.Warning &&
                i.Description.Contains("Local State"));
        }
        finally
        {
            Directory.Delete(tempUserData, true);
        }
    }

    [Fact]
    public void CheckFilesystemHealth_PreferencesMissing_ReturnsWarning()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        // Create Local State so we get past that check
        File.WriteAllText(Path.Combine(tempUserData, "Local State"), "{}");
        try
        {
            var profile = new ChromeProfile(
                "test-id",
                "Test Profile",
                "Profile 1",
                tempUserData,
                false);
            var checker = new ProfileHealthChecker();

            var issues = checker.CheckFilesystemHealth(profile);

            Assert.Contains(issues, i =>
                i.Category == HealthCategory.Filesystem &&
                i.Severity == IssueSeverity.Warning &&
                i.Description.Contains("Preferences"));
        }
        finally
        {
            Directory.Delete(tempUserData, true);
        }
    }

    [Fact]
    public void CheckFilesystemHealth_SecurePreferencesMissing_ReturnsInfo()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        File.WriteAllText(Path.Combine(tempUserData, "Local State"), "{}");
        File.WriteAllText(Path.Combine(tempProfile, "Preferences"), "{}");
        try
        {
            var profile = new ChromeProfile(
                "test-id",
                "Test Profile",
                "Profile 1",
                tempUserData,
                false);
            var checker = new ProfileHealthChecker();

            var issues = checker.CheckFilesystemHealth(profile);

            Assert.Contains(issues, i =>
                i.Category == HealthCategory.Filesystem &&
                i.Severity == IssueSeverity.Info &&
                i.Description.Contains("Secure Preferences"));
        }
        finally
        {
            Directory.Delete(tempUserData, true);
        }
    }

    [Fact]
    public void CheckFilesystemHealth_AllFilesPresent_NoIssues()
    {
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        File.WriteAllText(Path.Combine(tempUserData, "Local State"), "{}");
        File.WriteAllText(Path.Combine(tempProfile, "Preferences"), "{}");
        File.WriteAllText(Path.Combine(tempProfile, "Secure Preferences"), "{}");
        try
        {
            var profile = new ChromeProfile(
                "test-id",
                "Test Profile",
                "Profile 1",
                tempUserData,
                false);
            var checker = new ProfileHealthChecker();

            var issues = checker.CheckFilesystemHealth(profile);

            Assert.Empty(issues);
        }
        finally
        {
            Directory.Delete(tempUserData, true);
        }
    }
}
