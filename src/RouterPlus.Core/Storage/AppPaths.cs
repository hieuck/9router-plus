using System;
using System.IO;
using System.Linq;

namespace RouterPlus.Core.Storage;

/// <summary>
/// Centralized application data paths. When the ROUTERPLUS_TEST_ROOT environment
/// variable is set to a fully-qualified absolute path, all paths resolve under it
/// so tests never touch the real user profile. Production behavior is unchanged
/// when it is unset (or set to an invalid/relative/traversal value).
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Returns the validated, normalized test root override, or null when unset/invalid.
    /// </summary>
    private static string? GetValidatedOverride()
    {
        var raw = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Only fully-qualified absolute paths are honored; relative paths are ignored.
        if (!Path.IsPathFullyQualified(raw))
        {
            return null;
        }

        // Reject traversal segments.
        if (raw.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == ".."))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Root directory for 9RouterPlus application data.
    /// </summary>
    public static string LocalAppDataRoot
    {
        get
        {
            var overrideRoot = GetValidatedOverride();
            if (overrideRoot is not null)
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

    /// <summary>
    /// Root directory for RouterPlus observability data.
    /// </summary>
    public static string ObservabilityRoot
    {
        get
        {
            var overrideRoot = GetValidatedOverride();
            if (overrideRoot is not null)
            {
                return Path.Combine(overrideRoot, "Observability");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RouterPlus",
                "Observability");
        }
    }

    /// <summary>
    /// Default path to the Google login diagnostic log.
    /// </summary>
    public static string DiagnosticLogPath
    {
        get
        {
            var overrideRoot = GetValidatedOverride();
            var baseDir = overrideRoot is not null
                ? overrideRoot
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RouterPlus");

            return Path.Combine(baseDir, "google-login-diag.log");
        }
    }
}