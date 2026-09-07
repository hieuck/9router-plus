using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class ObservabilityHubTests
{
    [Fact]
    public void SetWriter_throws_when_writer_is_null()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => hub.SetWriter(null!));

        // Assert
        Assert.Equal("writer", exception.ParamName);
    }

    [Fact]
    public async Task FlushAsync_does_not_write_when_queues_are_empty()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var writer = await SetWriterAndFlushBaselineAsync(hub);

        // Act
        await hub.FlushAsync();

        // Assert
        Assert.Empty(writer.EventBatches);
        Assert.Empty(writer.SnapshotBatches);
    }

    [Fact]
    public async Task FlushAsync_writes_events_and_snapshots_to_in_memory_writer()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        var writer = await SetWriterAndFlushBaselineAsync(hub);
        var exception = new InvalidOperationException("test failure");

        // Act
        hub.LogEvent(LogLevel.Info, "Test", "EventWithNoContext", "message");
        hub.LogError("Test", "ErrorWithNoContext", exception);
        hub.CaptureSnapshot(
            "TestComponent",
            new Dictionary<string, object?> { ["state"] = "value" },
            SnapshotTrigger.Error);
        await hub.FlushAsync();

        // Assert
        var events = Assert.Single(writer.EventBatches);
        Assert.Equal(2, events.Count);
        Assert.Equal("EventWithNoContext", events[0].Event);
        Assert.Null(events[0].Context);
        Assert.Equal("ErrorWithNoContext", events[1].Event);
        Assert.Equal(nameof(InvalidOperationException), events[1].ErrorType);
        Assert.Null(events[1].Context);

        var snapshots = Assert.Single(writer.SnapshotBatches);
        var snapshot = Assert.Single(snapshots);
        Assert.Equal("TestComponent", snapshot.Component);
        Assert.Equal(SnapshotTrigger.Error, snapshot.Trigger);
        Assert.Null(snapshot.ErrorContext);
    }

    [Fact]
    public async Task Metrics_use_sorted_tags_and_keep_distinct_series()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;
        await SetWriterAndFlushBaselineAsync(hub);
        var metricName = $"test.metric.{Guid.NewGuid():N}";
        var tags = new Dictionary<string, string>
        {
            ["z"] = "last",
            ["a"] = "first"
        };

        var alternateTags = new Dictionary<string, string>
        {
            ["z"] = "last",
            ["a"] = "alternate"
        };

        // Act
        hub.IncrementCounter(metricName, tags: tags);
        hub.IncrementCounter(metricName, tags: alternateTags);
        hub.RecordGauge(metricName, 3.5, tags);
        hub.RecordHistogram(metricName, 7.5, tags: tags, unit: "ignored");
        var (counters, gauges, histograms) = hub.GetMetricSnapshots();

        // Assert
        var expectedKey = $"{metricName}{{a=first,z=last}}";
        var alternateKey = $"{metricName}{{a=alternate,z=last}}";
        Assert.Equal(1, counters[expectedKey]);
        Assert.Equal(1, counters[alternateKey]);
        Assert.Equal(3.5, gauges[expectedKey]);
        Assert.Equal((1L, 7.5), histograms[expectedKey]);
        Assert.DoesNotContain(alternateKey, gauges.Keys);
        Assert.DoesNotContain(alternateKey, histograms.Keys);
    }

    private static async Task<RecordingWriter> SetWriterAndFlushBaselineAsync(ObservabilityHub hub)
    {
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        await hub.FlushAsync();
        writer.Clear();
        return writer;
    }

    private sealed class RecordingWriter : IObservabilityWriter
    {
        private readonly object _lock = new();

        public List<IReadOnlyList<LogEvent>> EventBatches { get; } = new();
        public List<IReadOnlyList<StateSnapshot>> SnapshotBatches { get; } = new();

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            lock (_lock)
            {
                EventBatches.Add(events.ToList());
            }

            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
        {
            lock (_lock)
            {
                SnapshotBatches.Add(snapshots.ToList());
            }

            return Task.CompletedTask;
        }

        public void Clear()
        {
            lock (_lock)
            {
                EventBatches.Clear();
                SnapshotBatches.Clear();
            }
        }

        public void Dispose()
        {
        }
    }
}
