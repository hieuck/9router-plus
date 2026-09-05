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
}
