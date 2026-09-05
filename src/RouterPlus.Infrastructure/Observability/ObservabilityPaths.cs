using System;
using System.IO;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Provides paths for observability data storage.
/// </summary>
public sealed class ObservabilityPaths
{
    /// <summary>
    /// Root directory for all observability data.
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// Directory containing session subdirectories.
    /// </summary>
    public string SessionsDirectory { get; }

    public ObservabilityPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        RootDirectory = Path.Combine(localAppData, "RouterPlus", "Observability");
        SessionsDirectory = Path.Combine(RootDirectory, "sessions");
    }

    /// <summary>
    /// Gets the path for a specific session directory.
    /// </summary>
    public string GetSessionDirectory(string sessionId)
    {
        return Path.Combine(SessionsDirectory, sessionId);
    }

    /// <summary>
    /// Gets the path for the events.jsonl file in a session.
    /// </summary>
    public string GetEventsFilePath(string sessionId)
    {
        return Path.Combine(GetSessionDirectory(sessionId), "events.jsonl");
    }

    /// <summary>
    /// Gets the path for the snapshots.jsonl file in a session.
    /// </summary>
    public string GetSnapshotsFilePath(string sessionId)
    {
        return Path.Combine(GetSessionDirectory(sessionId), "snapshots.jsonl");
    }

    /// <summary>
    /// Gets the path for the session.json metadata file.
    /// </summary>
    public string GetSessionMetadataPath(string sessionId)
    {
        return Path.Combine(GetSessionDirectory(sessionId), "session.json");
    }

    /// <summary>
    /// Gets the path for a state snapshot file.
    /// </summary>
    public string GetSnapshotFilePath(string sessionId, DateTime timestamp, bool compressed)
    {
        var fileName = $"snapshot_{timestamp:yyyyMMdd_HHmmss}{(compressed ? ".json.gz" : ".json")}";
        return Path.Combine(GetSessionDirectory(sessionId), fileName);
    }
}
