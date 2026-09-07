using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests.Chrome;

/// <summary>
/// TDD tests for ChromeProfile record
/// Validates profile identity, path computation, and ID generation
/// </summary>
public sealed class ChromeProfileTests
{
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        // Arrange & Act
        var profile = new ChromeProfile(
            "test-id",
            "Test Profile",
            "Profile 1",
            @"C:\Users\Test\AppData\Local\Google\Chrome\User Data",
            false);

        // Assert
        Assert.Equal("test-id", profile.Id);
        Assert.Equal("Test Profile", profile.Name);
        Assert.Equal("Profile 1", profile.DirectoryName);
        Assert.Equal(@"C:\Users\Test\AppData\Local\Google\Chrome\User Data", profile.UserDataDirectory);
        Assert.False(profile.IsDefault);
    }

    [Fact]
    public void Constructor_HandlesDefaultProfile()
    {
        // Arrange & Act
        var profile = new ChromeProfile(
            "default-id",
            "Default",
            "Default",
            @"C:\Chrome\User Data",
            true);

        // Assert
        Assert.True(profile.IsDefault);
    }

    [Fact]
    public void ProfilePath_CombinesUserDataAndDirectoryName()
    {
        // Arrange
        var profile = new ChromeProfile(
            "id",
            "Test",
            "Profile 1",
            @"C:\Chrome\User Data",
            false);

        // Act
        var path = profile.ProfilePath;

        // Assert
        Assert.Equal(@"C:\Chrome\User Data\Profile 1", path);
    }

    [Fact]
    public void ProfilePath_HandlesDefaultDirectory()
    {
        // Arrange
        var profile = new ChromeProfile(
            "id",
            "Default",
            "Default",
            @"C:\Chrome\User Data",
            true);

        // Act
        var path = profile.ProfilePath;

        // Assert
        Assert.Equal(@"C:\Chrome\User Data\Default", path);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var profile1 = new ChromeProfile("id", "Name", "Profile 1", @"C:\Data", false);
        var profile2 = new ChromeProfile("id", "Name", "Profile 1", @"C:\Data", false);

        // Act & Assert
        Assert.Equal(profile1, profile2);
        Assert.True(profile1 == profile2);
    }

    [Fact]
    public void RecordEquality_DifferentId_AreNotEqual()
    {
        // Arrange
        var profile1 = new ChromeProfile("id1", "Name", "Profile 1", @"C:\Data", false);
        var profile2 = new ChromeProfile("id2", "Name", "Profile 1", @"C:\Data", false);

        // Act & Assert
        Assert.NotEqual(profile1, profile2);
    }

    #region CreateId Tests

    [Fact]
    public void CreateId_ThrowsWhenUserDataDirectoryIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ChromeProfile.CreateId(null!, "Profile 1"));
    }

    [Fact]
    public void CreateId_ThrowsWhenUserDataDirectoryIsEmpty()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ChromeProfile.CreateId("", "Profile 1"));
    }

    [Fact]
    public void CreateId_ThrowsWhenUserDataDirectoryIsWhitespace()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ChromeProfile.CreateId("   ", "Profile 1"));
    }

    [Fact]
    public void CreateId_ThrowsWhenDirectoryNameIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ChromeProfile.CreateId(@"C:\Data", null!));
    }

    [Fact]
    public void CreateId_ThrowsWhenDirectoryNameIsEmpty()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ChromeProfile.CreateId(@"C:\Data", ""));
    }

    [Fact]
    public void CreateId_ThrowsWhenDirectoryNameIsWhitespace()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ChromeProfile.CreateId(@"C:\Data", "   "));
    }

    [Fact]
    public void CreateId_ReturnsDeterministicHash()
    {
        // Arrange
        var userDataDir = @"C:\Users\Test\AppData\Local\Google\Chrome\User Data";
        var dirName = "Profile 1";

        // Act
        var id1 = ChromeProfile.CreateId(userDataDir, dirName);
        var id2 = ChromeProfile.CreateId(userDataDir, dirName);

        // Assert
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void CreateId_ReturnsLowercaseHexString()
    {
        // Arrange & Act
        var id = ChromeProfile.CreateId(@"C:\Data", "Profile 1");

        // Assert
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public void CreateId_Returns16CharacterString()
    {
        // Arrange & Act
        var id = ChromeProfile.CreateId(@"C:\Data", "Profile 1");

        // Assert
        Assert.Equal(16, id.Length);
    }

    [Fact]
    public void CreateId_IsCaseInsensitiveForUserDataDirectory()
    {
        // Arrange & Act
        var id1 = ChromeProfile.CreateId(@"C:\Chrome\User Data", "Profile 1");
        var id2 = ChromeProfile.CreateId(@"c:\chrome\user data", "Profile 1");

        // Assert
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void CreateId_IsCaseInsensitiveForDirectoryName()
    {
        // Arrange & Act
        var id1 = ChromeProfile.CreateId(@"C:\Data", "Profile 1");
        var id2 = ChromeProfile.CreateId(@"C:\Data", "PROFILE 1");

        // Assert
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void CreateId_NormalizesTrailingSlashes()
    {
        // Arrange & Act
        var id1 = ChromeProfile.CreateId(@"C:\Data", "Profile 1");
        var id2 = ChromeProfile.CreateId(@"C:\Data\", "Profile 1");
        var id3 = ChromeProfile.CreateId(@"C:\Data\\", "Profile 1");

        // Assert
        Assert.Equal(id1, id2);
        Assert.Equal(id1, id3);
    }

    [Fact]
    public void CreateId_DifferentDirectories_ProduceDifferentIds()
    {
        // Arrange & Act
        var id1 = ChromeProfile.CreateId(@"C:\Data1", "Profile 1");
        var id2 = ChromeProfile.CreateId(@"C:\Data2", "Profile 1");

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void CreateId_DifferentDirectoryNames_ProduceDifferentIds()
    {
        // Arrange & Act
        var id1 = ChromeProfile.CreateId(@"C:\Data", "Profile 1");
        var id2 = ChromeProfile.CreateId(@"C:\Data", "Profile 2");

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void CreateId_DefaultProfile_ProducesUniqueId()
    {
        // Arrange & Act
        var defaultId = ChromeProfile.CreateId(@"C:\Data", "Default");
        var profile1Id = ChromeProfile.CreateId(@"C:\Data", "Profile 1");

        // Assert
        Assert.NotEqual(defaultId, profile1Id);
    }

    #endregion
}
