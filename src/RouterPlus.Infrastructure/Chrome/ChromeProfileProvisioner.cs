using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Chrome;

public sealed class ChromeProfileProvisioner
{
    public ManagedChromeProfile Create(
        string userDataDirectory,
        string name,
        IEnumerable<ChromeProfile> discovered,
        IEnumerable<ManagedChromeProfile> managed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(managed);

        var normalizedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new ArgumentException("Profile name cannot be blank.", nameof(name));
        }

        var normalizedUserDataDirectory = NormalizePath(userDataDirectory);
        var discoveredProfiles = discovered
            .Where(profile => IsInUserDataDirectory(profile.UserDataDirectory, normalizedUserDataDirectory))
            .ToArray();
        var managedProfiles = managed
            .Where(profile => IsInUserDataDirectory(profile.UserDataDirectory, normalizedUserDataDirectory))
            .ToArray();

        if (discoveredProfiles.Any(profile => string.Equals(profile.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)) ||
            managedProfiles.Any(profile => string.Equals(profile.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A Chrome profile named '{normalizedName}' already exists.");
        }

        var occupiedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in discoveredProfiles)
        {
            occupiedDirectoryNames.Add(profile.DirectoryName.Trim());
        }

        foreach (var profile in managedProfiles)
        {
            occupiedDirectoryNames.Add(profile.DirectoryName.Trim());
        }

        Directory.CreateDirectory(normalizedUserDataDirectory);
        for (var index = 1; ; index++)
        {
            var directoryName = $"Profile {index}";
            if (occupiedDirectoryNames.Contains(directoryName))
            {
                continue;
            }

            var profilePath = Path.Combine(normalizedUserDataDirectory, directoryName);
            if (Directory.Exists(profilePath) || File.Exists(profilePath))
            {
                continue;
            }

            Directory.CreateDirectory(profilePath);
            return new ManagedChromeProfile(normalizedName, directoryName, normalizedUserDataDirectory);
        }
    }

    private static bool IsInUserDataDirectory(string path, string expectedPath) =>
        string.Equals(NormalizePath(path), expectedPath, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
