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
}
