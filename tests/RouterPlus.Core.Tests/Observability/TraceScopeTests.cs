using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class TraceScopeTests
{
    [Fact]
    public async Task TraceScope_logs_start_and_completion()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = $"test_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var sessionDir = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDir);

        var writer = new JsonLinesWriter(paths, sessionId);
        ObservabilityHub.Instance.SetWriter(writer);

        // Act
        using (var trace = TraceScope.Begin("TestCategory", "TestOperation", new { test_id = 123 }))
        {
            Thread.Sleep(100); // Simulate work
        }

        // Flush and wait for write
        await ObservabilityHub.Instance.FlushAsync();
        Thread.Sleep(500);

        // Assert
        var eventsFile = paths.GetEventsFilePath(sessionId);
        Assert.True(File.Exists(eventsFile), "events.jsonl should exist");

        var events = File.ReadAllLines(eventsFile)
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line))
            .ToList();

        var startedEvent = events.FirstOrDefault(e => e.GetProperty("event").GetString() == "TestOperationStarted");
        var completedEvent = events.FirstOrDefault(e => e.GetProperty("event").GetString() == "TestOperationCompleted");

        Assert.False(startedEvent.Equals(default(System.Text.Json.JsonElement)), "Should have Started event");
        Assert.False(completedEvent.Equals(default(System.Text.Json.JsonElement)), "Should have Completed event");

        // Verify completion has duration
        var context = completedEvent.GetProperty("context");
        Assert.True(context.TryGetProperty("duration_ms", out var duration));
        Assert.True(duration.GetInt64() >= 100); // At least 100ms

        // Cleanup
        writer.Dispose();
        try { Directory.Delete(sessionDir, true); } catch { }
    }

    [Fact]
    public async Task TraceScope_supports_checkpoints()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = $"test_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
        var sessionDir = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDir);

        var writer = new JsonLinesWriter(paths, sessionId);
        ObservabilityHub.Instance.SetWriter(writer);

        // Act
        using (var trace = TraceScope.Begin("TestCategory", "MultiStepOp"))
        {
            Thread.Sleep(50);
            trace.LogCheckpoint("Step1Complete", new { items_processed = 10 });
            Thread.Sleep(50);
            trace.LogCheckpoint("Step2Complete", new { items_processed = 20 });
        }

        // Flush and wait for write
        await ObservabilityHub.Instance.FlushAsync();
        Thread.Sleep(500);

        // Assert
        var eventsFile = paths.GetEventsFilePath(sessionId);
        var events = File.ReadAllLines(eventsFile)
            .Select(line => System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(line))
            .ToList();

        var checkpoints = events.Where(e => e.GetProperty("event").GetString() == "MultiStepOpCheckpoint").ToList();
        Assert.Equal(2, checkpoints.Count);

        // Verify checkpoint has elapsed time
        var checkpoint1 = checkpoints[0].GetProperty("context");
        Assert.True(checkpoint1.TryGetProperty("elapsed_ms", out var elapsed1));
        Assert.True(elapsed1.GetInt64() >= 50);

        // Cleanup
        writer.Dispose();
        try { Directory.Delete(sessionDir, true); } catch { }
    }

    [Fact]
    public void TraceScope_current_tracks_active_scope()
    {
        // Act & Assert
        Assert.Null(TraceScope.Current);

        using (var outer = TraceScope.Begin("Test", "Outer"))
        {
            Assert.NotNull(TraceScope.Current);
            Assert.Same(outer, TraceScope.Current);

            using (var inner = TraceScope.Begin("Test", "Inner"))
            {
                Assert.NotNull(TraceScope.Current);
                Assert.Same(inner, TraceScope.Current);
            }

            // After inner disposed, outer is current again
            Assert.Same(outer, TraceScope.Current);
        }

        // After all disposed, no current
        Assert.Null(TraceScope.Current);
    }
}
