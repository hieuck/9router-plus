using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class ObservabilitySettingsTests
{
    [Fact]
    public void Load_returns_defaults_when_file_not_exists()
    {
        // Act
        var settings = ObservabilitySettings.Load();

        // Assert
        Assert.True(settings.EnableLogging);
        Assert.True(settings.EnableMetrics);
        Assert.True(settings.EnableSnapshots);
        Assert.Equal(7, settings.RetentionDays);
        Assert.Equal(100, settings.MaxSessionSizeMB);
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        // Arrange
        var settings = new ObservabilitySettings
        {
            EnableLogging = false,
            EnableMetrics = true,
            EnableSnapshots = false,
            RetentionDays = 14,
            MaxSessionSizeMB = 50
        };

        // Act
        settings.Save();
        var loaded = ObservabilitySettings.Load();

        // Assert
        Assert.False(loaded.EnableLogging);
        Assert.True(loaded.EnableMetrics);
        Assert.False(loaded.EnableSnapshots);
        Assert.Equal(14, loaded.RetentionDays);
        Assert.Equal(50, loaded.MaxSessionSizeMB);

        // Cleanup - restore defaults
        var defaults = new ObservabilitySettings();
        defaults.Save();
    }

    [Fact]
    public void Load_returns_defaults_when_file_contains_invalid_json()
    {
        // Arrange
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RouterPlus", "Observability", "settings.json");
        var settingsDirectory = Path.GetDirectoryName(settingsPath)!;
        var hadExistingFile = File.Exists(settingsPath);
        var existingContents = hadExistingFile ? File.ReadAllBytes(settingsPath) : null;

        Directory.CreateDirectory(settingsDirectory);
        File.WriteAllText(settingsPath, "{ invalid json");

        try
        {
            // Act
            var settings = ObservabilitySettings.Load();

            // Assert
            Assert.True(settings.EnableLogging);
            Assert.True(settings.EnableMetrics);
            Assert.True(settings.EnableSnapshots);
            Assert.Equal(7, settings.RetentionDays);
            Assert.Equal(100, settings.MaxSessionSizeMB);
        }
        finally
        {
            if (hadExistingFile)
            {
                File.WriteAllBytes(settingsPath, existingContents!);
            }
            else
            {
                File.Delete(settingsPath);
            }
        }
    }
}
