using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests.Chrome;

public sealed class ProfileHealthCheckerTests
{
    [Fact]
    public void CheckFilesystemHealth_NullProfile_ThrowsArgumentNullException()
    {
        // Arrange
        var checker = new ProfileHealthChecker();

        // Act
        var act = () => checker.CheckFilesystemHealth(null!);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void CheckCredentialsHealth_NullProfile_ThrowsArgumentNullException()
    {
        // Arrange
        var checker = new ProfileHealthChecker();

        // Act
        var act = () => checker.CheckCredentialsHealth(null!, vault: null);

        // Assert
        Assert.Throws<ArgumentNullException>(act);
    }

    [Fact]
    public void CheckFilesystemHealth_ProfileDirectoryMissing_ReturnsOnlyDirectoryError()
    {
        // Arrange
        using var fixture = new ProfileFixture(createProfileDirectory: false);
        var checker = new ProfileHealthChecker();

        // Act
        var issues = checker.CheckFilesystemHealth(fixture.Profile);

        // Assert
        var issue = Assert.Single(issues);
        Assert.Equal(HealthCategory.Filesystem, issue.Category);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal("Profile directory not found", issue.Description);
        Assert.NotNull(issue.Recommendation);
    }

    [Fact]
    public void CheckFilesystemHealth_ProfileDirectoryExists_NoDirectoryError()
    {
        // Arrange
        using var fixture = new ProfileFixture();
        var checker = new ProfileHealthChecker();

        // Act
        var issues = checker.CheckFilesystemHealth(fixture.Profile);

        // Assert
        Assert.DoesNotContain(issues, i => i.Description.Contains("directory not found"));
    }

    [Fact]
    public void CheckFilesystemHealth_LocalStateMissing_ReturnsWarning()
    {
        // Arrange
        using var fixture = new ProfileFixture();
        var checker = new ProfileHealthChecker();

        // Act
        var issues = checker.CheckFilesystemHealth(fixture.Profile);

        // Assert
        Assert.Contains(issues, i =>
            i.Category == HealthCategory.Filesystem &&
            i.Severity == IssueSeverity.Warning &&
            i.Description.Contains("Local State"));
    }

    [Fact]
    public void CheckFilesystemHealth_PreferencesMissing_ReturnsWarning()
    {
        // Arrange
        using var fixture = new ProfileFixture();
        fixture.CreateLocalState();
        var checker = new ProfileHealthChecker();

        // Act
        var issues = checker.CheckFilesystemHealth(fixture.Profile);

        // Assert
        Assert.Contains(issues, i =>
            i.Category == HealthCategory.Filesystem &&
            i.Severity == IssueSeverity.Warning &&
            i.Description.Contains("Preferences"));
    }

    [Fact]
    public void CheckFilesystemHealth_SecurePreferencesMissing_ReturnsInfo()
    {
        // Arrange
        using var fixture = new ProfileFixture();
        fixture.CreateLocalState();
        fixture.CreatePreferences();
        var checker = new ProfileHealthChecker();

        // Act
        var issues = checker.CheckFilesystemHealth(fixture.Profile);

        // Assert
        Assert.Contains(issues, i =>
            i.Category == HealthCategory.Filesystem &&
            i.Severity == IssueSeverity.Info &&
            i.Description.Contains("Secure Preferences"));
    }

    [Fact]
    public void CheckFilesystemHealth_AllFilesPresent_NoIssues()
    {
        // Arrange
        using var fixture = new ProfileFixture();
        fixture.CreateLocalState();
        fixture.CreatePreferences();
        fixture.CreateSecurePreferences();
        var checker = new ProfileHealthChecker();

        // Act
        var issues = checker.CheckFilesystemHealth(fixture.Profile);

        // Assert
        Assert.Empty(issues);
    }

    [Fact]
    public void CheckFilesystemHealth_ExistingProfileWithMissingFiles_ReturnsCompleteIssueSet()
    {
        // Arrange
        var tempUserData = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempProfile = Path.Combine(tempUserData, "Profile 1");
        Directory.CreateDirectory(tempProfile);
        var profile = new ChromeProfile("test-id", "Test Profile", "Profile 1", tempUserData, false);
        var checker = new ProfileHealthChecker();

        try
        {
            // Act
            var issues = checker.CheckFilesystemHealth(profile);

            // Assert
            Assert.Equal(3, issues.Count);
            Assert.Contains(issues, issue =>
                issue.Severity == IssueSeverity.Warning &&
                issue.Description == "Chrome Local State file missing");
            Assert.Contains(issues, issue =>
                issue.Severity == IssueSeverity.Warning &&
                issue.Description == "Profile Preferences file missing");
            Assert.Contains(issues, issue =>
                issue.Severity == IssueSeverity.Info &&
                issue.Description.StartsWith("Secure Preferences file missing", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempUserData, true);
        }
    }

    private sealed class ProfileFixture : IDisposable
    {
        private const string ProfileDirectoryName = "Profile 1";

        public ProfileFixture(bool createProfileDirectory = true)
        {
            UserDataDirectory = Path.Combine(
                Path.GetTempPath(),
                "RouterPlusTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(UserDataDirectory);
            if (createProfileDirectory)
            {
                Directory.CreateDirectory(ProfilePath);
            }

            Profile = new ChromeProfile(
                "test-id",
                "Test Profile",
                ProfileDirectoryName,
                UserDataDirectory,
                false);
        }

        public string UserDataDirectory { get; }

        public string ProfilePath => Path.Combine(UserDataDirectory, ProfileDirectoryName);

        public ChromeProfile Profile { get; }

        public void CreateLocalState() => File.WriteAllText(
            Path.Combine(UserDataDirectory, "Local State"), "{}");

        public void CreatePreferences() => File.WriteAllText(
            Path.Combine(ProfilePath, "Preferences"), "{}");

        public void CreateSecurePreferences() => File.WriteAllText(
            Path.Combine(ProfilePath, "Secure Preferences"), "{}");

        public void Dispose()
        {
            if (Directory.Exists(UserDataDirectory))
            {
                Directory.Delete(UserDataDirectory, recursive: true);
            }
        }
    }
}
