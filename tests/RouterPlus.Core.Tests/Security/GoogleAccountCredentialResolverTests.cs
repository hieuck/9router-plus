using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;

namespace RouterPlus.Core.Tests.Security;

public sealed class GoogleAccountCredentialResolverTests
{
    [Fact]
    public void Resolve_PrefersStableProfileId()
    {
        var profile = Profile("stable-id", "Account");
        var stable = new GoogleLoginCredential(profile.Id, "stable@example.test", "stable-password", "STABLE");
        var legacy = new GoogleLoginCredential(profile.Name, "legacy@example.test", "legacy-password", "LEGACY");
        var vault = new GoogleAccountVault([legacy, stable]);

        var resolved = GoogleAccountCredentialResolver.Resolve(vault, profile, [profile]);

        Assert.Same(stable, resolved);
    }

    [Fact]
    public void Resolve_AdoptsLegacyNameCredentialOnlyWhenNameIsUnique()
    {
        var profile = Profile("stable-id", "Account");
        var legacy = new GoogleLoginCredential(profile.Name, "legacy@example.test", "legacy-password", "LEGACY");
        var vault = new GoogleAccountVault([legacy]);

        var resolved = GoogleAccountCredentialResolver.Resolve(vault, profile, [profile]);

        Assert.Same(legacy, resolved);
    }

    [Fact]
    public void Resolve_DoesNotAdoptLegacyNameCredentialWhenSameNameIsUsedByAnotherProfile()
    {
        var profile = Profile("stable-id", "Account");
        var sibling = Profile("sibling-id", "Account");
        var legacy = new GoogleLoginCredential(profile.Name, "legacy@example.test", "legacy-password", "LEGACY");
        var vault = new GoogleAccountVault([legacy]);

        var resolved = GoogleAccountCredentialResolver.Resolve(vault, profile, [profile, sibling]);

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_ReturnsNullWhenNoCredentialMatches()
    {
        var profile = Profile("stable-id", "Account");
        var vault = new GoogleAccountVault();

        var resolved = GoogleAccountCredentialResolver.Resolve(vault, profile, [profile]);

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_DoesNotFallBackToAnEmptyLegacyName()
    {
        var profile = Profile("stable-id", "");
        var unrelated = new GoogleLoginCredential("unrelated", "unrelated@example.test", "password", "TOTP");
        var vault = new GoogleAccountVault([unrelated]);

        var resolved = GoogleAccountCredentialResolver.Resolve(vault, profile, [profile]);

        Assert.Null(resolved);
    }

    private static ChromeProfile Profile(string id, string name) =>
        new(id, name, "Default", @"C:\Synthetic\Chrome", IsDefault: true);
}
