using System;
using System.IO;
using RouterPlus.Infrastructure.Observability;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class ObservabilityPathsTests
{
    [Fact]
    public void Constructor_uses_local_app_data_observability_root()
    {
        var paths = new ObservabilityPaths();
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RouterPlus",
            "Observability");

        Assert.Equal(expectedRoot, paths.RootDirectory);
        Assert.Equal(Path.Combine(expectedRoot, "sessions"), paths.SessionsDirectory);
    }

    [Fact]
    public void Session_paths_use_session_id_and_expected_file_names()
    {
        var paths = new ObservabilityPaths();
        const string sessionId = "synthetic-session-42";
        var sessionDirectory = Path.Combine(paths.SessionsDirectory, sessionId);

        Assert.Equal(sessionDirectory, paths.GetSessionDirectory(sessionId));
        Assert.Equal(Path.Combine(sessionDirectory, "events.jsonl"), paths.GetEventsFilePath(sessionId));
        Assert.Equal(Path.Combine(sessionDirectory, "snapshots.jsonl"), paths.GetSnapshotsFilePath(sessionId));
        Assert.Equal(Path.Combine(sessionDirectory, "session.json"), paths.GetSessionMetadataPath(sessionId));
    }

    [Theory]
    [InlineData(true, ".json.gz")]
    [InlineData(false, ".json")]
    public void Snapshot_file_path_uses_timestamp_and_compression_extension(
        bool compressed,
        string extension)
    {
        var paths = new ObservabilityPaths();
        const string sessionId = "synthetic-session-42";
        var timestamp = new DateTime(2026, 9, 9, 14, 5, 6);
        var expected = Path.Combine(
            paths.SessionsDirectory,
            sessionId,
            $"snapshot_20260909_140506{extension}");

        Assert.Equal(expected, paths.GetSnapshotFilePath(sessionId, timestamp, compressed));
    }
}
