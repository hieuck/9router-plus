using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeProfileDeleter
{
    public void Delete(ChromeProfile profile, string userDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);

        var expectedUserDataDirectory = NormalizePath(userDataDirectory);
        var profileUserDataDirectory = NormalizePath(profile.UserDataDirectory);
        if (!string.Equals(profileUserDataDirectory, expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Chrome profile is outside the configured User Data directory.");
        }

        var profilePath = NormalizePath(profile.ProfilePath);
        var profileParent = Directory.GetParent(profilePath)?.FullName;
        if (string.Equals(profilePath, expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase)
            || profileParent is null
            || !string.Equals(NormalizePath(profileParent), expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The Chrome profile directory must be an immediate child of User Data.");
        }

        if (File.Exists(profilePath))
        {
            throw new InvalidOperationException("The Chrome profile path is not a directory.");
        }

        if (Directory.Exists(profilePath))
        {
            Directory.Delete(profilePath, recursive: true);
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
