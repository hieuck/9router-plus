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
    public void Merge_without_configured_root_uses_the_single_discovered_root()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var discovered = new[]
        {
            new ChromeProfile(
                ChromeProfile.CreateId(userDataDirectory, "Default"),
                "Default",
                "Default",
                userDataDirectory,
                true)
        };
        var managed = new[]
        {
            new ManagedChromeProfile("Work", "Profile 1", userDataDirectory)
        };

        var profiles = ChromeProfileCatalog.Merge(discovered, managed);

        var profile = Assert.Single(profiles, profile => profile.Name == "Work");
        Assert.Equal("Profile 1", profile.DirectoryName);
        Assert.Equal(userDataDirectory, profile.UserDataDirectory);
    }

    [Fact]
    public void Merge_matches_discovered_profile_by_path_and_updates_managed_metadata()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var discovered = new[]
        {
            new ChromeProfile(
                "discovered-id",
                "Old name",
                "profile 1",
                userDataDirectory,
                false)
        };
        var managed = new[]
        {
            new ManagedChromeProfile("Work", "Profile 1", userDataDirectory)
        };

        var profiles = ChromeProfileCatalog.Merge(discovered, managed, userDataDirectory);

        var profile = Assert.Single(profiles);
        Assert.Equal("discovered-id", profile.Id);
        Assert.Equal("Work", profile.Name);
        Assert.Equal("Profile 1", profile.DirectoryName);
        Assert.Equal(userDataDirectory, profile.UserDataDirectory);
    }

    [Fact]
    public void Merge_orders_profiles_by_name_then_directory_name_case_insensitively()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var managed = new[]
        {
            new ManagedChromeProfile("zulu", "Profile 3", userDataDirectory),
            new ManagedChromeProfile("Alpha", "Profile 2", userDataDirectory),
            new ManagedChromeProfile("alpha", "Profile 1", userDataDirectory)
        };

        var profiles = ChromeProfileCatalog.Merge(Array.Empty<ChromeProfile>(), managed, userDataDirectory);

        Assert.Equal(new[] { "alpha", "Alpha", "zulu" }, profiles.Select(profile => profile.Name));
        Assert.Equal("Profile 1", profiles[0].DirectoryName);
        Assert.Equal("Profile 2", profiles[1].DirectoryName);
    }

    [Fact]
    public void Merge_rejects_null_discovered_or_managed_sequences()
    {
        var managed = Array.Empty<ManagedChromeProfile>();
        var discovered = Array.Empty<ChromeProfile>();

        Assert.Throws<ArgumentNullException>(() => ChromeProfileCatalog.Merge(null!, managed));
        Assert.Throws<ArgumentNullException>(() => ChromeProfileCatalog.Merge(discovered, null!));
    }
}
