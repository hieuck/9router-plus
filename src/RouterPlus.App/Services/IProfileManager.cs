using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Services;

public sealed record ProfileDiscoveryResult(
    ChromeInstallation? Installation,
    IReadOnlyList<ChromeProfile> Profiles);

public interface IProfileManager
{
    ProfileDiscoveryResult DiscoverProfiles(string? chromeExecutablePath, string? userDataDirectory);

    IReadOnlyList<ChromeInstallation> FindAllInstallations();

    IReadOnlyList<ManagedChromeProfile> ManagedProfiles { get; }

    IReadOnlyList<RecentProfile> RecentProfiles { get; }

    void LoadManaged(IEnumerable<ManagedChromeProfile>? profiles);

    void LoadRecent(IEnumerable<RecentProfile>? profiles);

    void AddManaged(ManagedChromeProfile profile);

    void RemoveManaged(ChromeProfile profile);

    void TrackLaunch(ChromeProfile profile);

    void PinRecent(ChromeProfile profile, bool isPinned);

    void ClearRecent();
}