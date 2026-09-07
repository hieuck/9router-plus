using RouterPlus.Core.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class ChromeProfileCatalogTests
{
    [Fact]
    public void Merge_preserves_managed_names_adds_missing_profiles_and_ignores_other_roots()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var otherUserDataDirectory = Path.Combine(Path.GetTempPath(), "Other Chrome", "User Data");
        var discovered = new[]
        {
            new ChromeProfile(
                ChromeProfile.CreateId(userDataDirectory, "Default"),
                "Chrome Name",
                "Default",
                userDataDirectory,
                true)
        };
        var managed = new[]
        {
            new ManagedChromeProfile("Personal", "Default", userDataDirectory),
            new ManagedChromeProfile("Work", "Profile 1", userDataDirectory),
            new ManagedChromeProfile("Ignored", "Profile 2", otherUserDataDirectory)
        };

        var profiles = ChromeProfileCatalog.Merge(discovered, managed, userDataDirectory);

        Assert.Equal(2, profiles.Count);
        Assert.Equal("Personal", profiles[0].Name);
        Assert.Equal("Default", profiles[0].DirectoryName);
        Assert.True(profiles[0].IsDefault);
        Assert.Equal("Work", profiles[1].Name);
        Assert.Equal("Profile 1", profiles[1].DirectoryName);
        Assert.Equal(ChromeProfile.CreateId(userDataDirectory, "Profile 1"), profiles[1].Id);
    }

    [Fact]
    public void Merge_uses_the_single_discovered_root_when_configured_root_is_omitted()
    {
        // Arrange
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var otherUserDataDirectory = Path.Combine(Path.GetTempPath(), "Other Chrome", "User Data");
        var discovered = new[]
        {
            new ChromeProfile(
                ChromeProfile.CreateId(userDataDirectory, "Default"),
                "Default browser",
                "Default",
                userDataDirectory,
                true)
        };
        var managed = new[]
        {
            new ManagedChromeProfile("Work", "Profile 1", userDataDirectory),
            new ManagedChromeProfile("Other root", "Profile 2", otherUserDataDirectory)
        };

        // Act
        var profiles = ChromeProfileCatalog.Merge(discovered, managed);

        // Assert
        Assert.Equal(2, profiles.Count);
        Assert.DoesNotContain(profiles, profile => profile.Name == "Other root");
        Assert.Contains(profiles, profile => profile.Name == "Work");
    }

    [Fact]
    public void Merge_trims_managed_values_and_orders_profiles_case_insensitively()
    {
        // Arrange
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var managed = new[]
        {
            new ManagedChromeProfile(" zulu ", " Profile 2 ", userDataDirectory),
            new ManagedChromeProfile(" Alpha ", " Default ", userDataDirectory)
        };

        // Act
        var profiles = ChromeProfileCatalog.Merge(Array.Empty<ChromeProfile>(), managed, userDataDirectory);

        // Assert
        Assert.Equal(2, profiles.Count);
        Assert.Equal("Alpha", profiles[0].Name);
        Assert.Equal("Default", profiles[0].DirectoryName);
        Assert.True(profiles[0].IsDefault);
        Assert.Equal("zulu", profiles[1].Name);
        Assert.Equal("Profile 2", profiles[1].DirectoryName);
        Assert.Equal(Path.GetFullPath(userDataDirectory), profiles[1].UserDataDirectory);
    }

    [Fact]
    public void Merge_throws_when_discovered_or_managed_profiles_are_null()
    {
        // Arrange
        var managed = Array.Empty<ManagedChromeProfile>();
        var discovered = Array.Empty<ChromeProfile>();

        // Act
        var discoveredException = Assert.Throws<ArgumentNullException>(() =>
            ChromeProfileCatalog.Merge(null!, managed));
        var managedException = Assert.Throws<ArgumentNullException>(() =>
            ChromeProfileCatalog.Merge(discovered, null!));

        // Assert
        Assert.Equal("discovered", discoveredException.ParamName);
        Assert.Equal("managed", managedException.ParamName);
    }
}
