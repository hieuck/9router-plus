namespace RouterPlus.Core.Chrome;

public static class ChromeProfileCatalog
{
    public static IReadOnlyList<ChromeProfile> Merge(
        IEnumerable<ChromeProfile> discovered,
        IEnumerable<ManagedChromeProfile> managed,
        string? configuredUserDataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(managed);

        var discoveredProfiles = discovered.ToArray();
        var profilesById = discoveredProfiles.ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        var expectedUserDataDirectory = string.IsNullOrWhiteSpace(configuredUserDataDirectory)
            ? discoveredProfiles
                .Select(profile => NormalizePath(profile.UserDataDirectory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .SingleOrDefault()
            : NormalizePath(configuredUserDataDirectory);

        foreach (var managedProfile in managed)
        {
            var name = managedProfile.Name.Trim();
            var directoryName = managedProfile.DirectoryName.Trim();
            var userDataDirectory = Path.GetFullPath(managedProfile.UserDataDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (expectedUserDataDirectory is not null &&
                !string.Equals(userDataDirectory, expectedUserDataDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var profileId = ChromeProfile.CreateId(userDataDirectory, directoryName);

            var discoveredProfile = profilesById.Values.FirstOrDefault(profile =>
                string.Equals(profile.Id, profileId, StringComparison.Ordinal) ||
                (string.Equals(
                     NormalizePath(profile.UserDataDirectory),
                     userDataDirectory,
                     StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(profile.DirectoryName, directoryName, StringComparison.OrdinalIgnoreCase)));

            if (discoveredProfile is not null)
            {
                profilesById.Remove(discoveredProfile.Id);
                profilesById[discoveredProfile.Id] = discoveredProfile with
                {
                    Name = name,
                    DirectoryName = directoryName,
                    UserDataDirectory = discoveredProfile.UserDataDirectory
                };
                continue;
            }

            profilesById[profileId] = new ChromeProfile(
                profileId,
                name,
                directoryName,
                userDataDirectory,
                string.Equals(directoryName, "Default", StringComparison.OrdinalIgnoreCase));
        }

        return profilesById.Values
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.DirectoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
