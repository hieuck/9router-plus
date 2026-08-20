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
}
