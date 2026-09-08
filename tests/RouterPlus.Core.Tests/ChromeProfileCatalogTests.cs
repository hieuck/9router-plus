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
            new ChromeProfile("discovered-id", "Old name", "profile 1", userDataDirectory, false)
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
    public void Merge_updates_discovered_profile_when_path_matches_but_id_does_not()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var discovered = new[]
        {
            new ChromeProfile("discovered-id", "Original", "Profile 1", userDataDirectory, false)
        };
        var managed = new[]
        {
            new ManagedChromeProfile("  Work  ", " Profile 1 ", userDataDirectory)
        };

        var profiles = ChromeProfileCatalog.Merge(discovered, managed, null);

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
    public void Merge_orders_same_name_profiles_by_directory_name()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var managed = new[]
        {
            new ManagedChromeProfile("Work", "Profile 2", userDataDirectory),
            new ManagedChromeProfile("Work", "Profile 1", userDataDirectory),
            new ManagedChromeProfile("Personal", "Default", userDataDirectory)
        };

        var profiles = ChromeProfileCatalog.Merge(Array.Empty<ChromeProfile>(), managed, userDataDirectory);

        Assert.Equal(["Personal", "Work", "Work"], profiles.Select(profile => profile.Name));
        Assert.Equal(["Default", "Profile 1", "Profile 2"], profiles.Select(profile => profile.DirectoryName));
    }

    [Fact]
    public void Merge_without_configured_root_rejects_ambiguous_discovered_roots()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), "Chrome", "First");
        var secondRoot = Path.Combine(Path.GetTempPath(), "Chrome", "Second");
        var discovered = new[]
        {
            new ChromeProfile("first", "First", "Default", firstRoot, true),
            new ChromeProfile("second", "Second", "Default", secondRoot, true)
        };
        var managed = new[]
        {
            new ManagedChromeProfile("Third", "Default", Path.Combine(Path.GetTempPath(), "Chrome", "Third"))
        };

        Assert.Throws<InvalidOperationException>(() => ChromeProfileCatalog.Merge(discovered, managed));
    }

    [Fact]
    public void Merge_uses_managed_profile_as_new_default_when_directory_is_default()
    {
        var userDataDirectory = Path.Combine(Path.GetTempPath(), "Chrome", "User Data");
        var managed = new[]
        {
            new ManagedChromeProfile("Personal", "Default", userDataDirectory)
        };

        var profile = Assert.Single(ChromeProfileCatalog.Merge(Array.Empty<ChromeProfile>(), managed, userDataDirectory));

        Assert.True(profile.IsDefault);
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
