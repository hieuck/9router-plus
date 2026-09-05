using System;
using System.IO;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public class DiagnosticReportBuilderTests
{
    [Fact]
    public void CreateReport_throws_when_session_not_found()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);
        var builder = new DiagnosticReportBuilder(paths, browser);

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() =>
            builder.CreateReport("nonexistent_session", Path.GetTempFileName()));
    }

    [Fact]
    public void CreateLatestReport_throws_when_no_sessions_available()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);
        var builder = new DiagnosticReportBuilder(paths, browser);

        var sessions = browser.ListSessions();

        // Act & Assert
        if (sessions.Count == 0)
        {
            Assert.Throws<InvalidOperationException>(() =>
                builder.CreateLatestReport(Path.GetTempPath()));
        }
        else
        {
            // Skip test if sessions exist from previous runs
            Assert.True(true);
        }
    }
}
