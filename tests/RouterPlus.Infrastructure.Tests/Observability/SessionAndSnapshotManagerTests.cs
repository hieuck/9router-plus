using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class SessionAndSnapshotManagerTests
{
    [Fact]
    public void SessionManager_initialize_writes_session_metadata_to_isolated_directory()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var manager = new SessionManager(fixture.Paths);

        manager.Initialize();

        var sessionDirectory = fixture.Paths.GetSessionDirectory(manager.SessionId);
        var metadataPath = fixture.Paths.GetSessionMetadataPath(manager.SessionId);
        Assert.True(Directory.Exists(sessionDirectory));
        Assert.True(File.Exists(metadataPath));

        var metadata = JsonSerializer.Deserialize<SessionMetadata>(File.ReadAllText(metadataPath));
        Assert.NotNull(metadata);
        Assert.Equal(manager.SessionId, metadata.SessionId);
        Assert.True(metadata.StartTime <= DateTime.UtcNow);
        Assert.Null(metadata.EndTime);
        Assert.NotEmpty(metadata.AppVersion);
        Assert.NotEmpty(metadata.OperatingSystem);
        Assert.NotEmpty(metadata.DotNetVersion);
    }

    [Fact]
    public async Task SessionManager_finalize_updates_metadata_with_end_time()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var manager = new SessionManager(fixture.Paths);
        manager.Initialize();

        await manager.FinalizeAsync();

        var metadata = JsonSerializer.Deserialize<SessionMetadata>(
            File.ReadAllText(fixture.Paths.GetSessionMetadataPath(manager.SessionId)));
        Assert.NotNull(metadata);
        Assert.NotNull(metadata.EndTime);
        Assert.True(metadata.EndTime >= metadata.StartTime);
    }

    [Fact]
    public void SessionManager_cleanup_removes_sessions_older_than_seven_days_only()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var oldSession = Directory.CreateDirectory(Path.Combine(fixture.Paths.SessionsDirectory, "old-session"));
        var recentSession = Directory.CreateDirectory(Path.Combine(fixture.Paths.SessionsDirectory, "recent-session"));
        Directory.SetCreationTimeUtc(oldSession.FullName, DateTime.UtcNow.AddDays(-8));
        Directory.SetCreationTimeUtc(recentSession.FullName, DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(oldSession.FullName, "session.json"), "old");
        File.WriteAllText(Path.Combine(recentSession.FullName, "session.json"), "recent");

        new SessionManager(fixture.Paths).CleanupOldSessions();

        Assert.False(Directory.Exists(oldSession.FullName));
        Assert.True(Directory.Exists(recentSession.FullName));
    }

    [Fact]
    public async Task SnapshotManager_capture_writes_json_and_skips_unchanged_state()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var session = new SessionManager(fixture.Paths);
        session.Initialize();
        using var manager = new SnapshotManager(fixture.Paths, session.SessionId);
        var state = new { ProfileCount = 2, SelectedProfile = "Default" };

        await manager.CaptureSnapshotAsync(state, "on-demand");
        var firstSnapshot = Assert.Single(Directory.GetFiles(fixture.Paths.GetSessionDirectory(session.SessionId), "snapshot_*.json"));
        var firstContents = await File.ReadAllTextAsync(firstSnapshot);

        await manager.CaptureSnapshotAsync(new { ProfileCount = 2, SelectedProfile = "Default" }, "unchanged");

        Assert.Single(Directory.GetFiles(fixture.Paths.GetSessionDirectory(session.SessionId), "snapshot_*.json"));
        Assert.Equal(firstContents, await File.ReadAllTextAsync(firstSnapshot));
        Assert.Contains("\"ProfileCount\": 2", firstContents);
        Assert.Contains("\"SelectedProfile\": \"Default\"", firstContents);
    }

    [Fact]
    public async Task SnapshotManager_capture_persists_changed_state_as_a_new_snapshot()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var session = new SessionManager(fixture.Paths);
        session.Initialize();
        using var manager = new SnapshotManager(fixture.Paths, session.SessionId);

        await manager.CaptureSnapshotAsync(new { Status = "starting" }, "first");
        await Task.Delay(TimeSpan.FromSeconds(1.2));
        await manager.CaptureSnapshotAsync(new { Status = "complete" }, "changed");

        var snapshots = Directory.GetFiles(fixture.Paths.GetSessionDirectory(session.SessionId), "snapshot_*.json");
        Assert.Equal(2, snapshots.Length);
        Assert.Contains(snapshots, path => File.ReadAllText(path).Contains("starting"));
        Assert.Contains(snapshots, path => File.ReadAllText(path).Contains("complete"));
    }

    [Fact]
    public async Task SnapshotManager_capture_compresses_snapshots_larger_than_one_megabyte()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var session = new SessionManager(fixture.Paths);
        session.Initialize();
        using var manager = new SnapshotManager(fixture.Paths, session.SessionId);
        var state = new { Payload = new string('x', 1_100_000) };

        await manager.CaptureSnapshotAsync(state, "large-state");

        var compressedSnapshot = Assert.Single(
            Directory.GetFiles(fixture.Paths.GetSessionDirectory(session.SessionId), "snapshot_*.json.gz"));
        await using var file = File.OpenRead(compressedSnapshot);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var document = JsonDocument.Parse(json);
        Assert.Equal(1_100_000, document.RootElement.GetProperty("Payload").GetString()?.Length);
    }

    [Fact]
    public async Task SnapshotManager_capture_after_dispose_does_not_write_snapshot()
    {
        using var fixture = TestObservabilityDirectory.Create();
        var session = new SessionManager(fixture.Paths);
        session.Initialize();
        var manager = new SnapshotManager(fixture.Paths, session.SessionId);
        manager.Dispose();

        await manager.CaptureSnapshotAsync(new { Status = "ignored" }, "after-dispose");

        Assert.Empty(Directory.GetFiles(fixture.Paths.GetSessionDirectory(session.SessionId), "snapshot_*.json*"));
    }

    private sealed class TestObservabilityDirectory : IDisposable
    {
        private TestObservabilityDirectory(string root, ObservabilityPaths paths)
        {
            Root = root;
            Paths = paths;
        }

        public string Root { get; }
        public ObservabilityPaths Paths { get; }

        public static TestObservabilityDirectory Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "RouterPlusObservabilityTests", Guid.NewGuid().ToString("N"));
            var paths = new ObservabilityPaths();
            SetReadOnlyAutoProperty(paths, nameof(ObservabilityPaths.RootDirectory), root);
            SetReadOnlyAutoProperty(paths, nameof(ObservabilityPaths.SessionsDirectory), Path.Combine(root, "sessions"));
            Directory.CreateDirectory(paths.SessionsDirectory);
            return new TestObservabilityDirectory(root, paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void SetReadOnlyAutoProperty<T>(ObservabilityPaths paths, string propertyName, T value)
        {
            var field = typeof(ObservabilityPaths).GetField(
                $"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(paths, value);
        }
    }
}
