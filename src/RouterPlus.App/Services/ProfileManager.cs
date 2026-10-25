using System.IO;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Services;

public sealed class ProfileManager : IProfileManager
{
    private const int MaxRecentSlots = 10;

    private readonly ChromeLocator _chromeLocator;
    private readonly ChromeProfileReader _profileReader;
    private readonly List<ManagedChromeProfile> _managedProfiles = new();
    private readonly List<RecentProfile> _recentProfiles = new();

    public ProfileManager(
        ChromeLocator? chromeLocator = null,
        ChromeProfileReader? profileReader = null)
    {
        _chromeLocator = chromeLocator ?? new ChromeLocator();
        _profileReader = profileReader ?? new ChromeProfileReader();
    }

    public IReadOnlyList<ManagedChromeProfile> ManagedProfiles => _managedProfiles;

    public IReadOnlyList<RecentProfile> RecentProfiles => _recentProfiles;

    public ProfileDiscoveryResult DiscoverProfiles(string? chromeExecutablePath, string? userDataDirectory)
    {
        var installation = _chromeLocator.Find(
            string.IsNullOrWhiteSpace(chromeExecutablePath) ? null : chromeExecutablePath,
            string.IsNullOrWhiteSpace(userDataDirectory) ? null : userDataDirectory);

        if (installation is null)
        {
            return new ProfileDiscoveryResult(null, Array.Empty<ChromeProfile>());
        }

        var discoveredProfiles = _profileReader.Read(installation.UserDataDirectory)
            .Where(profile => Directory.Exists(profile.ProfilePath))
            .ToArray();

        var managedProfiles = _managedProfiles
            .Where(profile => Directory.Exists(Path.Combine(profile.UserDataDirectory, profile.DirectoryName)))
            .ToArray();

        var profiles = ChromeProfileCatalog.Merge(
            discoveredProfiles,
            managedProfiles,
            installation.UserDataDirectory);

        return new ProfileDiscoveryResult(installation, profiles);
    }

    public IReadOnlyList<ChromeInstallation> FindAllInstallations() =>
        _chromeLocator.FindAll();

    public void LoadManaged(IEnumerable<ManagedChromeProfile>? profiles)
    {
        _managedProfiles.Clear();
        if (profiles is not null)
        {
            _managedProfiles.AddRange(profiles);
        }
    }

    public void LoadRecent(IEnumerable<RecentProfile>? profiles)
    {
        _recentProfiles.Clear();
        if (profiles is not null)
        {
            _recentProfiles.AddRange(profiles);
        }
    }

    public void AddManaged(ManagedChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _managedProfiles.Add(profile);
    }

    public void RemoveManaged(ChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _managedProfiles.RemoveAll(managedProfile =>
            string.Equals(managedProfile.DirectoryName.Trim(), profile.DirectoryName.Trim(), StringComparison.OrdinalIgnoreCase)
            && PathsEqual(managedProfile.UserDataDirectory, profile.UserDataDirectory));
    }

    public void TrackLaunch(ChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var existing = _recentProfiles.FirstOrDefault(recent =>
            recent.ProfileId == profile.Id &&
            PathsEqual(recent.UserDataDirectory, profile.UserDataDirectory));

        if (existing is not null)
        {
            _recentProfiles.Remove(existing);
            _recentProfiles.Insert(0, existing with
            {
                LastUsedUtc = DateTime.UtcNow,
                LaunchCount = existing.LaunchCount + 1
            });
        }
        else
        {
            _recentProfiles.Insert(0, new RecentProfile(
                profile.Id,
                profile.Name,
                profile.UserDataDirectory,
                DateTime.UtcNow,
                1,
                false));
        }

        while (_recentProfiles.Count > MaxRecentSlots)
        {
            _recentProfiles.RemoveAt(_recentProfiles.Count - 1);
        }
    }

    public void PinRecent(ChromeProfile profile, bool isPinned)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var recent = _recentProfiles.FirstOrDefault(r =>
            r.ProfileId == profile.Id &&
            PathsEqual(r.UserDataDirectory, profile.UserDataDirectory));

        if (recent is null)
        {
            return;
        }

        var index = _recentProfiles.IndexOf(recent);
        _recentProfiles[index] = recent with { IsPinned = isPinned };

        var sorted = _recentProfiles
            .OrderByDescending(r => r.IsPinned)
            .ThenByDescending(r => r.LastUsedUtc)
            .ToList();
        _recentProfiles.Clear();
        _recentProfiles.AddRange(sorted);
    }

    public void ClearRecent()
    {
        _recentProfiles.Clear();
    }

    private static bool PathsEqual(string left, string right)
    {
        var leftPath = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightPath = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
    }
}