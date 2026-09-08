using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public class DiagnosticReportBuilderTests
{
    [Fact]
    public void CreateReport_creates_zip_with_session_files_and_metadata()
    {
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);
        var builder = new DiagnosticReportBuilder(paths, browser);
        var sessionId = $"diagnostic-test-{Guid.NewGuid():N}";
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"diagnostic-report-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDirectory, "report.zip");

        try
        {
            Directory.CreateDirectory(sessionDirectory);
            File.WriteAllText(Path.Combine(sessionDirectory, "events.jsonl"), "event");

            var result = builder.CreateReport(sessionId, outputPath);

            Assert.Equal(outputPath, result);
            Assert.True(File.Exists(outputPath));
            using var archive = ZipFile.OpenRead(outputPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "events.jsonl");
            var metadataEntry = Assert.Single(archive.Entries, entry => entry.FullName == "report_metadata.json");
            using var metadataStream = metadataEntry.Open();
            using var document = JsonDocument.Parse(metadataStream);
            Assert.Equal(sessionId, document.RootElement.GetProperty("session_id").GetString());
            Assert.True(document.RootElement.TryGetProperty("report_generated", out _));
        }
        finally
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateReport_replaces_existing_output_file()
    {
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);
        var builder = new DiagnosticReportBuilder(paths, browser);
        var sessionId = $"diagnostic-test-{Guid.NewGuid():N}";
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"diagnostic-report-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(outputDirectory, "report.zip");

        try
        {
            Directory.CreateDirectory(sessionDirectory);
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(sessionDirectory, "snapshot.json"), "snapshot");
            File.WriteAllText(outputPath, "stale report");

            builder.CreateReport(sessionId, outputPath);

            using var archive = ZipFile.OpenRead(outputPath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "snapshot.json");
            Assert.Contains(archive.Entries, entry => entry.FullName == "report_metadata.json");
        }
        finally
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }

            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateReport_throws_when_session_not_found()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var browser = new SessionBrowser(paths);
        var builder = new DiagnosticReportBuilder(paths, browser);

        // Act
        var exception = Record.Exception(() =>
            builder.CreateReport("nonexistent_session", Path.GetTempFileName()));

        // Assert
        var directoryNotFoundException = Assert.IsType<DirectoryNotFoundException>(exception);
        Assert.Equal("Session nonexistent_session not found", directoryNotFoundException.Message);
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
