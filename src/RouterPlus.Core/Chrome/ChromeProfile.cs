using System.Security.Cryptography;
using System.Text;

namespace RouterPlus.Core.Chrome;

public sealed record ChromeProfile(
    string Id,
    string Name,
    string DirectoryName,
    string UserDataDirectory,
    bool IsDefault)
{
    public string ProfilePath => Path.Combine(UserDataDirectory, DirectoryName);

    public static string CreateId(string userDataDirectory, string directoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryName);

        var fullPath = Path.GetFullPath(userDataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var input = Encoding.UTF8.GetBytes($"{fullPath}|{directoryName.ToUpperInvariant()}");
        return Convert.ToHexString(SHA256.HashData(input))[..16].ToLowerInvariant();
    }
}
