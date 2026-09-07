using RouterPlus.Core.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests.Chrome;

/// <summary>
/// TDD tests for ManagedChromeProfile record.
/// </summary>
public sealed class ManagedChromeProfileTests
{
    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        // Arrange & Act
        var profile = new ManagedChromeProfile(
            "Work",
            "Profile 1",
            @"C:\Chrome\User Data");

        // Assert
        Assert.Equal("Work", profile.Name);
        Assert.Equal("Profile 1", profile.DirectoryName);
        Assert.Equal(@"C:\Chrome\User Data", profile.UserDataDirectory);
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        // Arrange
        var first = new ManagedChromeProfile("Work", "Profile 1", @"C:\Chrome\User Data");
        var second = new ManagedChromeProfile("Work", "Profile 1", @"C:\Chrome\User Data");

        // Act & Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        // Arrange
        var first = new ManagedChromeProfile("Work", "Profile 1", @"C:\Chrome\User Data");
        var second = new ManagedChromeProfile("Personal", "Profile 2", @"C:\Chrome\Personal");

        // Act & Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void WithExpression_CreatesProfileWithUpdatedName()
    {
        // Arrange
        var profile = new ManagedChromeProfile("Work", "Profile 1", @"C:\Chrome\User Data");

        // Act
        var renamed = profile with { Name = "Personal" };

        // Assert
        Assert.Equal("Personal", renamed.Name);
        Assert.Equal(profile.DirectoryName, renamed.DirectoryName);
        Assert.Equal(profile.UserDataDirectory, renamed.UserDataDirectory);
        Assert.Equal("Work", profile.Name);
    }
}
