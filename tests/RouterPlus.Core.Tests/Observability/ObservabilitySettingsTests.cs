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
        Assert.True(settings.Enabled);
        Assert.Equal(30, settings.MaxSessionsToKeep);
        Assert.Equal(90, settings.MaxSessionAgeDays);
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        // Arrange
        var settings = new ObservabilitySettings
        {
            Enabled = false,
            MaxSessionsToKeep = 10,
            MaxSessionAgeDays = 30
        };

        // Act
        settings.Save();
        var loaded = ObservabilitySettings.Load();

        // Assert
        Assert.False(loaded.Enabled);
        Assert.Equal(10, loaded.MaxSessionsToKeep);
        Assert.Equal(30, loaded.MaxSessionAgeDays);

        // Cleanup - restore defaults
        var defaults = new ObservabilitySettings();
        defaults.Save();
    }
}
