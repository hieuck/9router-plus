using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class ObservabilityHubBranchTests
{
    [Fact]
    public async Task SetWriter_rejects_null_without_replacing_existing_writer()
    {
        // Arrange
        using var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);

        // Act / Assert
        var exception = Assert.Throws<ArgumentNullException>(() => hub.SetWriter(null!));
        Assert.Equal("writer", exception.ParamName);

        hub.LogEvent(LogLevel.Info, "Test", "Preserved", "message");
        await hub.FlushAsync();

        var loggedEvent = Assert.Single(writer.Events);
        Assert.Equal("Preserved", loggedEvent.Event);
    }

    [Fact]
    public async Task FlushAsync_without_writer_does_not_throw()
    {
        // Arrange
        using var hub = CreateHub();

        // Act
        var exception = await Record.ExceptionAsync(() => hub.FlushAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task FlushAsync_with_no_pending_items_does_not_write()
    {
        // Arrange
        using var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);

        // Act
        await hub.FlushAsync();

        // Assert
        Assert.Empty(writer.Events);
        Assert.Empty(writer.Snapshots);
    }

    [Fact]
    public async Task FlushAsync_writes_pending_events_and_snapshots()
    {
        // Arrange
        using var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        hub.LogEvent(LogLevel.Warning, "Test", "WarningRaised", "A warning", new { token = "secret" });
        hub.LogError("Test", "Failure", new InvalidOperationException("broken"));
        hub.CaptureSnapshot(
            "TestComponent",
            new Dictionary<string, object?> { ["State"] = "ready" },
            SnapshotTrigger.Error,
            "operation failed");

        // Act
        await hub.FlushAsync();

        // Assert
        var events = Assert.IsAssignableFrom<IReadOnlyList<LogEvent>>(writer.Events);
        Assert.Equal(2, events.Count);
        Assert.Equal(LogLevel.Warning, events[0].Level);
        Assert.Equal("WarningRaised", events[0].Event);
        Assert.Equal("[REDACTED]", Assert.IsType<Dictionary<string, object?>>(events[0].Context)["token"]);
        Assert.Equal("InvalidOperationException", events[1].ErrorType);
        Assert.Single(writer.Snapshots);
        Assert.Equal(SnapshotTrigger.Error, writer.Snapshots[0].Trigger);
        Assert.Equal("operation failed", writer.Snapshots[0].ErrorContext);
    }

    [Fact]
    public void Logging_ignores_context_scrubbing_failures()
    {
        // Arrange
        using var hub = CreateHub();
        var context = new ThrowingEnumerable();

        // Act
        var logException = Record.Exception(() =>
            hub.LogEvent(LogLevel.Info, "Test", "Event", "message", context));
        var errorException = Record.Exception(() =>
            hub.LogError("Test", "Error", new InvalidOperationException("failure"), context));

        // Assert
        Assert.Null(logException);
        Assert.Null(errorException);
    }

    [Fact]
    public void CaptureSnapshot_ignores_state_scrubbing_failures()
    {
        // Arrange
        using var hub = CreateHub();
        var state = new Dictionary<string, object?> { ["broken"] = new ThrowingEnumerable() };

        // Act
        var exception = Record.Exception(() =>
            hub.CaptureSnapshot("TestComponent", state, SnapshotTrigger.OnDemand));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Metric_tags_are_sorted_when_building_series_keys()
    {
        // Arrange
        using var hub = CreateHub();
        var tags = new Dictionary<string, string>
        {
            ["zeta"] = "last",
            ["alpha"] = "first"
        };

        // Act
        hub.IncrementCounter("requests", tags: tags);

        // Assert
        var (counters, _, _) = hub.GetMetricSnapshots();
        Assert.Equal(1, counters["requests{alpha=first,zeta=last}"]);
    }

    [Fact]
    public void Disposed_hub_ignores_new_events_and_snapshots()
    {
        // Arrange
        var hub = CreateHub();
        hub.Dispose();

        // Act
        var logException = Record.Exception(() =>
            hub.LogEvent(LogLevel.Info, "Test", "Event", "message"));
        var errorException = Record.Exception(() =>
            hub.LogError("Test", "Error", new InvalidOperationException("failure")));
        var snapshotException = Record.Exception(() =>
            hub.CaptureSnapshot("TestComponent", new Dictionary<string, object?>(), SnapshotTrigger.OnDemand));
        var secondDisposeException = Record.Exception(hub.Dispose);

        // Assert
        Assert.Null(logException);
        Assert.Null(errorException);
        Assert.Null(snapshotException);
        Assert.Null(secondDisposeException);
    }

    private static ObservabilityHub CreateHub()
    {
        return (ObservabilityHub)Activator.CreateInstance(
            typeof(ObservabilityHub),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null)!;
    }

    private sealed class RecordingWriter : IObservabilityWriter
    {
        public List<LogEvent> Events { get; } = [];
        public List<StateSnapshot> Snapshots { get; } = [];

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            Events.AddRange(events);
            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
        {
            Snapshots.AddRange(snapshots);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingEnumerable : IEnumerable
    {
        public IEnumerator GetEnumerator()
        {
            throw new InvalidOperationException("Enumeration failed");
        }
    }
}
