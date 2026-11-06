using RouterPlus.Core.Chrome;

namespace RouterPlus.Core.Security;

/// <summary>
/// Resolves the Google credential owned by a Chrome profile.
/// Stable profile Id is authoritative. The legacy display-name key is
/// adopted only when exactly one current profile carries that name.
/// </summary>
public static class GoogleAccountCredentialResolver
{
    public static GoogleLoginCredential? Resolve(
        GoogleAccountVault vault,
        ChromeProfile profile,
        IReadOnlyCollection<ChromeProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profiles);

        var stable = vault.Find(profile.Id);
        if (stable is not null || string.IsNullOrWhiteSpace(profile.Name))
        {
            return stable;
        }

        var sameNameCount = profiles.Count(p => p.Name == profile.Name);
        return sameNameCount == 1 ? vault.Find(profile.Name) : null;
    }
}
