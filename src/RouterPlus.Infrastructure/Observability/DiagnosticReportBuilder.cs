using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var outputPath = Path.Combine(outputDirectory, $"diagnostic_report_{timestamp}.zip");

        return CreateReport(latestSession.SessionId, outputPath);
    }

    private void AddReportMetadata(string zipPath, string sessionId)
    {
        try
        {
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
            var sessionDir = _paths.GetSessionDirectory(sessionId);
            var assembly = typeof(DiagnosticReportBuilder).Assembly;
            var version = assembly.GetName().Version?.ToString() ?? "unknown";
            var informationalVersion = assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            var artifacts = EnumerateArtifacts(sessionDir);
            var metadata = new
            {
                session_id = sessionId,
                report_generated = DateTime.UtcNow,
                app_version = version,
                informational_version = informationalVersion,
                operating_system = Environment.OSVersion.ToString(),
                os_architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                dotnet_version = Environment.Version.ToString(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                file_count = artifacts.Count,
                total_bytes = artifacts.Sum(item => item.Bytes),
                artifacts
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

    private static IReadOnlyList<ReportArtifact> EnumerateArtifacts(string sessionDirectory)
    {
        var artifacts = new List<ReportArtifact>();
        foreach (var path in Directory.EnumerateFiles(sessionDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sessionDirectory, path).Replace('\\', '/');
            artifacts.Add(new ReportArtifact
            {
                Path = relativePath,
                Bytes = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path),
            });
        }

        return artifacts.OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "<unreadable>";
        }
    }
}

internal sealed class ReportArtifact
{
    public required string Path { get; init; }
    public required string Sha256 { get; init; }
    public required long Bytes { get; init; }
}
