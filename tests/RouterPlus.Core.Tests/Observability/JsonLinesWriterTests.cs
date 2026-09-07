using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class JsonLinesWriterTests
{
    [Fact]
    public async Task WriteEventsAsync_creates_file_and_writes_events()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = $"test_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var sessionDir = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDir);

        var writer = new JsonLinesWriter(paths, sessionId);

        var events = new[]
        {
            new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Info,
                Category = "Test",
                Event = "TestEvent",
                Message = "Test message",
                Context = new { test_id = 123 }
            }
        };

        // Act
        await writer.WriteEventsAsync(events);

        // Assert
        var eventsFile = paths.GetEventsFilePath(sessionId);
        Assert.True(File.Exists(eventsFile), $"events.jsonl should exist at {eventsFile}");

        var lines = File.ReadAllLines(eventsFile);
        Assert.Single(lines);
        Assert.Contains("TestEvent", lines[0]);
        Assert.Contains("test_id", lines[0]);

        // Cleanup
        writer.Dispose();
        try { Directory.Delete(sessionDir, true); } catch { }
    }

    [Fact]
    public async Task ObservabilityHub_flushes_events_correctly()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = $"test_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var sessionDir = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDir);

        var writer = new JsonLinesWriter(paths, sessionId);
        ObservabilityHub.Instance.SetWriter(writer);

        // Act
        ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Test", "DirectTest", "Direct test message", new { value = 456 });
        await ObservabilityHub.Instance.FlushAsync();
        Thread.Sleep(500);

        // Assert
        var eventsFile = paths.GetEventsFilePath(sessionId);
        Assert.True(File.Exists(eventsFile), $"events.jsonl should exist at {eventsFile}");

        var lines = File.ReadAllLines(eventsFile);
        Assert.True(lines.Length > 0, "Should have at least one event");
        Assert.Contains("DirectTest", string.Join("\n", lines));

        // Cleanup
        writer.Dispose();
        try { Directory.Delete(sessionDir, true); } catch { }
    }

    [Fact]
    public async Task WriteSnapshotsAsync_creates_json_lines_for_each_snapshot()
    {
        using var scope = new TemporaryObservabilityScope();
        using var writer = new JsonLinesWriter(scope.Paths, scope.SessionId);

        var snapshots = new[]
        {
            new StateSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Component = "Router",
                State = new Dictionary<string, object?> { ["status"] = "ready" },
                Trigger = SnapshotTrigger.OnDemand
            },
            new StateSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Component = "Profiles",
                State = new Dictionary<string, object?> { ["count"] = 2 },
                Trigger = SnapshotTrigger.Periodic
            }
        };

        await writer.WriteSnapshotsAsync(snapshots);

        var lines = await File.ReadAllLinesAsync(scope.Paths.GetSnapshotsFilePath(scope.SessionId));
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"component\":\"Router\"", lines[0]);
        Assert.Contains("\"trigger\":\"OnDemand\"", lines[0]);
        Assert.Contains("\"component\":\"Profiles\"", lines[1]);
    }

    [Fact]
    public async Task WriteSnapshotsAsync_creates_an_empty_file_for_empty_collection()
    {
        using var scope = new TemporaryObservabilityScope();
        using var writer = new JsonLinesWriter(scope.Paths, scope.SessionId);

        await writer.WriteSnapshotsAsync(Array.Empty<StateSnapshot>());

        var snapshotsFile = scope.Paths.GetSnapshotsFilePath(scope.SessionId);
        Assert.True(File.Exists(snapshotsFile));
        Assert.Equal(0, new FileInfo(snapshotsFile).Length);
    }

    [Fact]
    public async Task WriteSnapshotsAsync_is_no_op_after_dispose()
    {
        using var scope = new TemporaryObservabilityScope();
        var writer = new JsonLinesWriter(scope.Paths, scope.SessionId);
        writer.Dispose();

        await writer.WriteSnapshotsAsync(new[]
        {
            new StateSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Component = "Router",
                State = new Dictionary<string, object?>(),
                Trigger = SnapshotTrigger.Error
            }
        });

        Assert.False(File.Exists(scope.Paths.GetSnapshotsFilePath(scope.SessionId)));
    }

    [Fact]
    public async Task WriteSnapshotsAsync_rotates_an_oversized_file_before_appending()
    {
        using var scope = new TemporaryObservabilityScope();
        var snapshotsFile = scope.Paths.GetSnapshotsFilePath(scope.SessionId);
        const long oversizedLength = 50L * 1024 * 1024 + 1;
        using (var existingFile = new FileStream(snapshotsFile, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            existingFile.SetLength(oversizedLength);
        }

        using var writer = new JsonLinesWriter(scope.Paths, scope.SessionId);
        await writer.WriteSnapshotsAsync(new[]
        {
            new StateSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Component = "Router",
                State = new Dictionary<string, object?> { ["status"] = "rotated" },
                Trigger = SnapshotTrigger.OnDemand
            }
        });

        var rotatedFile = Path.Combine(scope.SessionDirectory, "snapshots.1.jsonl");
        Assert.True(File.Exists(rotatedFile));
        Assert.Equal(oversizedLength, new FileInfo(rotatedFile).Length);
        Assert.Single(await File.ReadAllLinesAsync(snapshotsFile));
        Assert.Contains("rotated", await File.ReadAllTextAsync(snapshotsFile));
    }

    private sealed class TemporaryObservabilityScope : IDisposable
    {
        public TemporaryObservabilityScope()
        {
            Paths = new ObservabilityPaths();
            SessionId = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
            SessionDirectory = Paths.GetSessionDirectory(SessionId);
            Directory.CreateDirectory(SessionDirectory);
        }

        public ObservabilityPaths Paths { get; }
        public string SessionId { get; }
        public string SessionDirectory { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(SessionDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }
}
