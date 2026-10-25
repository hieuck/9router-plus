using System;
using System.IO;

namespace RouterPlus.Core.Storage;

/// <summary>
/// Centralized application data paths. When the ROUTERPLUS_TEST_ROOT environment
/// variable is set (non-empty), all paths resolve under it so tests never touch
/// the real user profile. Production behavior is unchanged when it is unset.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Root directory for 9RouterPlus application data.
    /// </summary>
    public static string LocalAppDataRoot
    {
        get
        {
            var overrideRoot = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "9RouterPlus");
        }
    }

    /// <summary>
    /// Default path to settings.json.
    /// </summary>
    public static string SettingsPath => Path.Combine(LocalAppDataRoot, "settings.json");

    /// <summary>
    /// Default path to secrets.json.
    /// </summary>
    public static string SecretsPath => Path.Combine(LocalAppDataRoot, "secrets.json");

    /// <summary>
    /// Default path to the Google account vault.
    /// </summary>
    public static string GoogleAccountsVaultPath => Path.Combine(LocalAppDataRoot, "google-accounts.vault");

    /// <summary>
    /// Default path to the provider connection vault.
    /// </summary>
    public static string ProviderConnectionsVaultPath => Path.Combine(LocalAppDataRoot, "provider-connections.vault");
}