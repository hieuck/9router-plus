using System;
using System.IO;
using System.Reflection;
using RouterPlus.Core.Storage;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public class DefaultObservabilityIsolationTests
{
    [Fact]
    public void ObservabilityPaths_Default_UsesAppPaths()
    {
        var paths = new ObservabilityPaths();
        Assert.Equal(AppPaths.ObservabilityRoot, paths.RootDirectory);
        Assert.Equal(Path.Combine(AppPaths.ObservabilityRoot, "sessions"), paths.SessionsDirectory);
    }

    [Fact]
    public void GoogleLoginCdpBrowser_DiagnosticPath_UsesAppPaths()
    {
        var method = typeof(GoogleLoginCdpBrowser).GetMethod(
            "GetDiagnosticFilePath",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, null) as string;
        Assert.Equal(AppPaths.DiagnosticLogPath, result);
    }
}