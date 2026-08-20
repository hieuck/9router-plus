using System.Security.Cryptography;
using System.Text;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Security;

public static class ProfileSecretKey
{
    public static string Create(ChromeProfile profile, ProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var stableProfile = $"{profile.UserDataDirectory}|{profile.DirectoryName}".ToUpperInvariant();
        var bytes = Encoding.UTF8.GetBytes($"{stableProfile}|{provider}");
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..24].ToLowerInvariant();
        return $"{provider.ToString().ToLowerInvariant()}-{hash}";
    }
}
