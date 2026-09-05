using System;
using System.IO;
using System.Text.Json;

namespace RouterPlus.Core.Observability;

/// <summary>
/// User-configurable settings for observability system.
/// </summary>
public sealed class ObservabilitySettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RouterPlus",
        "Observability",
        "settings.json");

    /// <summary>
    /// Whether observability logging is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of sessions to keep (older sessions auto-deleted).
    /// </summary>
    public int MaxSessionsToKeep { get; set; } = 30;

    /// <summary>
    /// Maximum age of sessions in days (older sessions auto-deleted).
    /// </summary>
    public int MaxSessionAgeDays { get; set; } = 90;

    /// <summary>
    /// Load settings from disk.
    /// </summary>
    public static ObservabilitySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<ObservabilitySettings>(json) ?? new ObservabilitySettings();
            }
        }
        catch
        {
            // Fall back to defaults on error
        }

        return new ObservabilitySettings();
    }

    /// <summary>
    /// Save settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Fail silently - don't crash app for settings save failure
        }
    }
}
