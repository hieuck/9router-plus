using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;

namespace RouterPlus.Core.Chrome;

/// <summary>
/// Stateless health checker for Chrome profiles.
/// Performs filesystem, vault, credentials, and provider checks.
/// </summary>
public sealed class ProfileHealthChecker
{
    /// <summary>
    /// Check filesystem health (directory exists, files readable, required files present).
    /// </summary>
    public IReadOnlyList<HealthIssue> CheckFilesystemHealth(ChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "HealthCheck",
            "FilesystemCheckStarted",
            "Starting filesystem health check",
            new { profile = profile.Name, profile_path = profile.ProfilePath });

        var issues = new List<HealthIssue>();

        // Check #1: Profile directory exists
        if (!Directory.Exists(profile.ProfilePath))
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "HealthCheck",
                "ProfileDirectoryNotFound",
                "Profile directory does not exist",
                new { profile = profile.Name, profile_path = profile.ProfilePath });

            issues.Add(HealthIssue.Error(
                HealthCategory.Filesystem,
                "Profile directory not found",
                "Profile may have been deleted externally. Consider removing from catalog."));
            // Stop checks if directory doesn't exist
            return issues;
        }

        // Check #2: Profile directory readable
        try
        {
            Directory.EnumerateFiles(profile.ProfilePath).Any();
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(HealthIssue.Error(
                HealthCategory.Filesystem,
                "Cannot access profile directory",
                "Check file permissions."));
        }
        catch (IOException ex)
        {
            issues.Add(HealthIssue.Error(
                HealthCategory.Filesystem,
                $"I/O error accessing profile directory: {ex.Message}",
                null));
        }

        // Check #3: Local State file
        var localStatePath = Path.Combine(profile.UserDataDirectory, "Local State");
        if (!File.Exists(localStatePath))
        {
            issues.Add(HealthIssue.Warning(
                HealthCategory.Filesystem,
                "Chrome Local State file missing",
                "Chrome may not have been launched yet."));
        }

        // Check #4: Preferences file
        var preferencesPath = Path.Combine(profile.ProfilePath, "Preferences");
        if (!File.Exists(preferencesPath))
        {
            issues.Add(HealthIssue.Warning(
                HealthCategory.Filesystem,
                "Profile Preferences file missing",
                "Profile may never have been used."));
        }

        // Check #5: Secure Preferences file
        var securePreferencesPath = Path.Combine(profile.ProfilePath, "Secure Preferences");
        if (!File.Exists(securePreferencesPath))
        {
            issues.Add(HealthIssue.Info(
                HealthCategory.Filesystem,
                "Secure Preferences file missing. This is normal for older Chrome versions or unused profiles."));
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "HealthCheck",
            "FilesystemCheckCompleted",
            "Filesystem health check completed",
            new { profile = profile.Name, issue_count = issues.Count });

        return issues;
    }

    /// <summary>
    /// Check credentials configuration health.
    /// </summary>
    /// <param name="profile">Profile to check</param>
    /// <param name="vault">Google account vault (null if not loaded)</param>
    public IReadOnlyList<HealthIssue> CheckCredentialsHealth(
        ChromeProfile profile,
        GoogleAccountVault? vault)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "HealthCheck",
            "CredentialsCheckStarted",
            "Starting credentials health check",
            new { profile = profile.Name, profile_id = profile.Id, vault_loaded = vault != null });

        var issues = new List<HealthIssue>();

        // Check #12: Google credentials present
        if (vault == null)
        {
            issues.Add(HealthIssue.Info(
                HealthCategory.Credentials,
                "Google vault not loaded")
                with { Recommendation = "Cannot check credential status." });
            return issues;
        }

        var credential = vault.Find(profile.Id);
        if (credential == null)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "HealthCheck",
                "CredentialsNotFound",
                "No credentials found for profile",
                new { profile = profile.Name, profile_id = profile.Id });

            issues.Add(HealthIssue.Warning(
                HealthCategory.Credentials,
                "No Google account linked to this profile",
                "Profile has not been logged into Google, or credentials not saved."));
        }
        else
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "HealthCheck",
                "CredentialsFound",
                "Credentials found for profile",
                new { profile = profile.Name, email = credential.Email });
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "HealthCheck",
            "CredentialsCheckCompleted",
            "Credentials health check completed",
            new { profile = profile.Name, issue_count = issues.Count });

        return issues;
    }
}
