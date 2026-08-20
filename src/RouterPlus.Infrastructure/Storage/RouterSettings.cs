namespace RouterPlus.Infrastructure.Storage;

public sealed record RouterSettings(
    string DashboardBaseUrl = "http://localhost:20128",
    string? ChromeExecutablePath = null,
    string? ChromeUserDataDirectory = null,
    double FontScale = 1d,
    bool UseLightTheme = false);
