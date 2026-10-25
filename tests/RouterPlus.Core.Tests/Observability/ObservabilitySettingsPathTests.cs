using System;
using System.IO;
using System.Reflection;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Storage;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public class ObservabilitySettingsPathTests
{
    [Fact]
    public void SettingsPath_UsesAppPaths()
    {
        var field = typeof(ObservabilitySettings).GetField(
            "SettingsPath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var value = field!.GetValue(null) as string;
        Assert.Equal(Path.Combine(AppPaths.ObservabilityRoot, "settings.json"), value);
    }
}