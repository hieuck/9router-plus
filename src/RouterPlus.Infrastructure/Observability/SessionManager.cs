using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Manages observability session lifecycle and cleanup.
/// </summary>
public sealed class SessionManager
{
    private readonly ObservabilityPaths _paths;
    private readonly string _sessionId;
    private readonly SessionMetadata _metadata;

    public string SessionId => _sessionId;

    public SessionManager(ObservabilityPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));

        // Generate session ID: YYYY-MM-DD_HHmmss_{short-guid}
        var now = DateTime.UtcNow;
        var shortGuid = Guid.NewGuid().ToString("N")[..8];
        _sessionId = $"{now:yyyy-MM-dd_HHmmss}_{shortGuid}";

        _metadata = new SessionMetadata
        {
            SessionId = _sessionId,
            StartTime = now,
            AppVersion = GetAppVersion(),
            OperatingSystem = Environment.OSVersion.ToString(),
            DotNetVersion = Environment.Version.ToString()
        };
    }

    /// <summary>
    /// Initializes the session directory and writes metadata.
    /// </summary>
    public void Initialize()
    {
        // Create session directory
        var sessionDir = _paths.GetSessionDirectory(_sessionId);
        Directory.CreateDirectory(sessionDir);

        // Write session metadata synchronously
        var metadataPath = _paths.GetSessionMetadataPath(_sessionId);
        var json = JsonSerializer.Serialize(_metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(metadataPath, json);
    }

    /// <summary>
    /// Updates session metadata on shutdown.
    /// </summary>
    public async Task FinalizeAsync()
    {
        _metadata.EndTime = DateTime.UtcNow;

        var metadataPath = _paths.GetSessionMetadataPath(_sessionId);
        var json = JsonSerializer.Serialize(_metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(metadataPath, json);
    }

    /// <summary>
    /// Cleans up old sessions beyond retention period (7 days).
    /// </summary>
    public void CleanupOldSessions()
    {
        try
        {
            if (!Directory.Exists(_paths.SessionsDirectory)) return;

            var cutoff = DateTime.UtcNow.AddDays(-7);
            var sessionDirs = Directory.GetDirectories(_paths.SessionsDirectory);

            foreach (var dir in sessionDirs)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.CreationTimeUtc < cutoff)
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch
                {
                    // Best effort - continue with other directories
                }
            }
        }
        catch
        {
            // Never crash app due to cleanup failure
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly();
            return assembly?.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}

/// <summary>
/// Metadata for an observability session.
/// </summary>
public sealed class SessionMetadata
{
    public string SessionId { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
    public string AppVersion { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string DotNetVersion { get; init; } = string.Empty;
}
