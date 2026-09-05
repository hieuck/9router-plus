using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Creates diagnostic report ZIP files for support/debugging.
/// </summary>
public sealed class DiagnosticReportBuilder
{
    private readonly ObservabilityPaths _paths;
    private readonly SessionBrowser _browser;

    public DiagnosticReportBuilder(ObservabilityPaths paths, SessionBrowser browser)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    /// <summary>
    /// Creates a ZIP file containing the specified session's diagnostic data.
    /// </summary>
    public string CreateReport(string sessionId, string outputPath)
    {
        var sessionDir = _paths.GetSessionDirectory(sessionId);
        if (!Directory.Exists(sessionDir))
        {
            throw new DirectoryNotFoundException($"Session {sessionId} not found");
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        // Create ZIP
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        ZipFile.CreateFromDirectory(sessionDir, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);

        // Add report metadata
        AddReportMetadata(outputPath, sessionId);

        return outputPath;
    }

    /// <summary>
    /// Creates a ZIP file containing the most recent session.
    /// </summary>
    public string CreateLatestReport(string outputDirectory)
    {
        var sessions = _browser.ListSessions();
        if (sessions.Count == 0)
        {
            throw new InvalidOperationException("No sessions available");
        }

        var latestSession = sessions[0];
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var outputPath = Path.Combine(outputDirectory, $"diagnostic_report_{timestamp}.zip");

        return CreateReport(latestSession.SessionId, outputPath);
    }

    private void AddReportMetadata(string zipPath, string sessionId)
    {
        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);

            var metadata = new
            {
                session_id = sessionId,
                report_generated = DateTime.UtcNow,
                app_version = typeof(DiagnosticReportBuilder).Assembly.GetName().Version?.ToString(),
                machine_name = Environment.MachineName,
                os_version = Environment.OSVersion.ToString()
            };

            var entry = archive.CreateEntry("report_metadata.json");
            using var stream = entry.Open();
            JsonSerializer.Serialize(stream, metadata, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            // Metadata is optional - continue if it fails
        }
    }
}
