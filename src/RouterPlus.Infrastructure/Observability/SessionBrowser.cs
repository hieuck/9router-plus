using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Provides information about observability sessions for UI display.
/// </summary>
public sealed class SessionBrowser
{
    private readonly ObservabilityPaths _paths;

    public SessionBrowser(ObservabilityPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>
    /// Lists all sessions, newest first.
    /// </summary>
    public List<SessionInfo> ListSessions()
    {
        var sessions = new List<SessionInfo>();

        try
        {
            if (!Directory.Exists(_paths.SessionsDirectory))
            {
                return sessions;
            }

            var sessionDirs = Directory.GetDirectories(_paths.SessionsDirectory);

            foreach (var dir in sessionDirs)
            {
                try
                {
                    var sessionId = Path.GetFileName(dir);
                    var info = GetSessionInfo(sessionId);
                    if (info != null)
                    {
                        sessions.Add(info);
                    }
                }
                catch
                {
                    // Skip invalid sessions
                }
            }

            return sessions.OrderByDescending(s => s.StartTime).ToList();
        }
        catch
        {
            return sessions;
        }
    }

    /// <summary>
    /// Gets detailed information about a specific session.
    /// </summary>
    public SessionInfo? GetSessionInfo(string sessionId)
    {
        try
        {
            var sessionDir = _paths.GetSessionDirectory(sessionId);
            if (!Directory.Exists(sessionDir))
            {
                return null;
            }

            var eventsFile = _paths.GetEventsFilePath(sessionId);
            var snapshotsFile = _paths.GetSnapshotsFilePath(sessionId);

            long totalSize = 0;
            int fileCount = 0;
            DateTime? startTime = null;

            foreach (var file in Directory.GetFiles(sessionDir))
            {
                var fileInfo = new FileInfo(file);
                totalSize += fileInfo.Length;
                fileCount++;

                if (startTime == null || fileInfo.CreationTimeUtc < startTime)
                {
                    startTime = fileInfo.CreationTimeUtc;
                }
            }

            return new SessionInfo
            {
                SessionId = sessionId,
                StartTime = startTime ?? DateTime.UtcNow,
                TotalSizeBytes = totalSize,
                FileCount = fileCount,
                HasEvents = File.Exists(eventsFile),
                HasSnapshots = File.Exists(snapshotsFile)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes a session and all its files.
    /// </summary>
    public bool DeleteSession(string sessionId)
    {
        try
        {
            var sessionDir = _paths.GetSessionDirectory(sessionId);
            if (Directory.Exists(sessionDir))
            {
                Directory.Delete(sessionDir, recursive: true);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes sessions older than the specified number of days.
    /// </summary>
    public int DeleteOldSessions(int olderThanDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var sessions = ListSessions();
        int deletedCount = 0;

        foreach (var session in sessions)
        {
            if (session.StartTime < cutoffDate)
            {
                if (DeleteSession(session.SessionId))
                {
                    deletedCount++;
                }
            }
        }

        return deletedCount;
    }
}

/// <summary>
/// Information about an observability session.
/// </summary>
public sealed class SessionInfo
{
    public required string SessionId { get; init; }
    public required DateTime StartTime { get; init; }
    public required long TotalSizeBytes { get; init; }
    public required int FileCount { get; init; }
    public required bool HasEvents { get; init; }
    public required bool HasSnapshots { get; init; }

    public double SizeMB => TotalSizeBytes / (1024.0 * 1024.0);
}
