using RouterPlus.Core.Chrome;

namespace RouterPlus.Infrastructure.Storage;

public sealed record RouterSettings(
    string DashboardBaseUrl = "http://localhost:20128",
    string? ChromeExecutablePath = null,
    string? ChromeUserDataDirectory = null,
    double FontScale = 1d,
    bool UseLightTheme = true,
    IReadOnlyList<ManagedChromeProfile>? ManagedProfiles = null,
    double? WindowLeft = null,
    double? WindowTop = null,
    double? WindowWidth = null,
    double? WindowHeight = null,
    IReadOnlyList<RecentProfile>? RecentProfiles = null,
    IReadOnlyList<QuotaAutoDisableMarker>? QuotaAutoDisableMarkers = null);
