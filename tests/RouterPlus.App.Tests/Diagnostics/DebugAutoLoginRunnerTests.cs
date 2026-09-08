using RouterPlus.App.Diagnostics;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;

namespace RouterPlus.App.Tests.Diagnostics;

public sealed class DebugAutoLoginRunnerTests
{
    [Fact]
    public void SelectProfile_PrefersNameMatchBeforeDirectoryMatch()
    {
        // Arrange
        var directoryMatch = Profile("directory-id", "Other", "Target");
        var nameMatch = Profile("name-id", "Target", "Other");

        // Act
        var selected = DebugAutoLoginRunner.SelectProfile(
            [directoryMatch, nameMatch],
            "Target");

        // Assert
        Assert.Same(nameMatch, selected);
    }

    [Fact]
    public void SelectProfile_FallsBackToDirectoryName()
    {
        // Arrange
        var profile = Profile("profile-id", "Friendly name", "Profile 1");

        // Act
        var selected = DebugAutoLoginRunner.SelectProfile([profile], "Profile 1");

        // Assert
        Assert.Same(profile, selected);
    }

    [Fact]
    public void SelectProfile_ReturnsNullWhenNoProfileMatches()
    {
        // Arrange
        var profiles = new[] { Profile("profile-id", "Friendly name", "Default") };

        // Act
        var selected = DebugAutoLoginRunner.SelectProfile(profiles, "Missing");

        // Assert
        Assert.Null(selected);
    }

    [Fact]
    public void ResolveCredential_PrefersStableProfileId()
    {
        // Arrange
        var profile = Profile("stable-id", "Account", "Default");
        var stableCredential = Credential(profile.Id, "stable@example.test");
        var legacyCredential = Credential(profile.Name, "legacy@example.test");
        var vault = new GoogleAccountVault([legacyCredential, stableCredential]);

        // Act
        var resolved = DebugAutoLoginRunner.ResolveCredential(vault, profile, [profile]);

        // Assert
        Assert.Same(stableCredential, resolved);
    }

    [Fact]
    public void ResolveCredential_UsesLegacyNameOnlyForUniqueProfileName()
    {
        // Arrange
        var profile = Profile("stable-id", "Account", "Default");
        var legacyCredential = Credential(profile.Name, "legacy@example.test");
        var vault = new GoogleAccountVault([legacyCredential]);

        // Act
        var resolved = DebugAutoLoginRunner.ResolveCredential(vault, profile, [profile]);

        // Assert
        Assert.Same(legacyCredential, resolved);
    }

    [Fact]
    public void ResolveCredential_DoesNotUseLegacyNameWhenProfileNameIsDuplicated()
    {
        // Arrange
        var profile = Profile("stable-id", "Account", "Default");
        var duplicate = Profile("duplicate-id", profile.Name, "Profile 1");
        var vault = new GoogleAccountVault([Credential(profile.Name, "legacy@example.test")]);

        // Act
        var resolved = DebugAutoLoginRunner.ResolveCredential(vault, profile, [profile, duplicate]);

        // Assert
        Assert.Null(resolved);
    }

    private static ChromeProfile Profile(string id, string name, string directoryName) =>
        new(id, name, directoryName, @"C:\Synthetic\Chrome", IsDefault: directoryName == "Default");

    private static GoogleLoginCredential Credential(string profileId, string email) =>
        new(profileId, email, "synthetic-password", "synthetic-totp");
}
