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

        var issues = new List<HealthIssue>();

        // Check #1: Profile directory exists
        if (!Directory.Exists(profile.ProfilePath))
        {
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

        return issues;
    }
}
