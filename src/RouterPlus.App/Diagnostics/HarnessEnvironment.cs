using System.IO;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Diagnostics;

internal static class HarnessEnvironment
{
    private const string EnabledVariable = "ROUTERPLUS_HARNESS";
    private const string RootVariable = "ROUTERPLUS_HARNESS_ROOT";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnabledVariable), "1", StringComparison.Ordinal);

    public static string? RootPath =>
        IsEnabled ? Environment.GetEnvironmentVariable(RootVariable) : null;

    public static string SettingsPath =>
        Path.Combine(GetRequiredRootPath(), "settings.json");

    public static IReadOnlyList<ChromeProfile> CreateProfiles()
    {
        var userDataDirectory = Path.Combine(GetRequiredRootPath(), "SyntheticChromeUserData");
        Directory.CreateDirectory(userDataDirectory);

        return
        [
            new ChromeProfile(
                ChromeProfile.CreateId(userDataDirectory, "Default"),
                "Harness Alpha",
                "Default",
                userDataDirectory,
                true),
            new ChromeProfile(
                ChromeProfile.CreateId(userDataDirectory, "Profile 1"),
                "Harness Beta",
                "Profile 1",
                userDataDirectory,
                false)
        ];
    }

    public static SettingsStore CreateSettingsStore() =>
        new(IsEnabled ? SettingsPath : null);

    public static void Trace(string message)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(RootPath))
        {
            return;
        }

        File.AppendAllText(
            Path.Combine(GetRequiredRootPath(), "startup.trace"),
            $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}");
    }

    public static RouterSettings CreateSettings() =>
        new(
            DashboardBaseUrl: "http://127.0.0.1:20128",
            ChromeExecutablePath: null,
            ChromeUserDataDirectory: null,
            UseLightTheme: true);

    private static string GetRequiredRootPath()
    {
        var root = RootPath;
        if (!IsEnabled || string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                $"{RootVariable} must be set when {EnabledVariable}=1.");
        }

        Directory.CreateDirectory(root);
        return Path.GetFullPath(root);
    }
}
