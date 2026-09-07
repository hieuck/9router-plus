using System.Reflection;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class ObservabilityHubTests
{
    [Fact]
    public void SetWriter_rejects_null_writer()
    {
        using var hub = CreateHub();

        Assert.Throws<ArgumentNullException>(() => hub.SetWriter(null!));
    }

    [Fact]
    public async Task FlushAsync_does_not_write_when_queues_are_empty()
    {
        using var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);

        await hub.FlushAsync();

        Assert.Empty(writer.Events);
        Assert.Empty(writer.Snapshots);
    }

    [Fact]
    public async Task FlushAsync_writes_queued_events_and_snapshots_to_in_memory_writer()
    {
        using var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);

        hub.LogEvent(LogLevel.Info, "Test", "Event", "Message", new { value = 42 });
        hub.LogError("Test", "Error", new InvalidOperationException("failure"));
        hub.CaptureSnapshot(
            "TestComponent",
            new Dictionary<string, object?> { ["value"] = 42 },
            SnapshotTrigger.OnDemand);

        await hub.FlushAsync();

        Assert.Equal(2, writer.Events.Count);
        var loggedEvent = Assert.Single(writer.Events, evt => evt.Event == "Event");
        Assert.Equal("Test", loggedEvent.Category);
        Assert.NotNull(loggedEvent.Context);

        var errorEvent = Assert.Single(writer.Events, evt => evt.Event == "Error");
        Assert.Equal(LogLevel.Error, errorEvent.Level);
        Assert.Equal("InvalidOperationException", errorEvent.ErrorType);
        Assert.Equal("failure", errorEvent.Message);

        var snapshot = Assert.Single(writer.Snapshots);
        Assert.Equal("TestComponent", snapshot.Component);
        Assert.Equal(SnapshotTrigger.OnDemand, snapshot.Trigger);
        Assert.NotNull(snapshot.State);
    }

    [Fact]
    public async Task FlushAsync_without_writer_preserves_queued_data_until_writer_is_set()
    {
        using var hub = CreateHub();
        hub.LogEvent(LogLevel.Info, "Test", "Event", "Message");

        await hub.FlushAsync();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        await hub.FlushAsync();

        Assert.Single(writer.Events);
        Assert.Equal("Event", writer.Events[0].Event);
    }

    [Fact]
    public async Task Disposed_hub_ignores_logging_and_snapshot_requests()
    {
        var hub = CreateHub();
        var writer = new RecordingWriter();
        hub.SetWriter(writer);
        hub.Dispose();

        hub.LogEvent(LogLevel.Info, "Test", "Event", "Message");
        hub.LogError("Test", "Error", new InvalidOperationException("failure"));
        hub.CaptureSnapshot(
            "TestComponent",
            new Dictionary<string, object?> { ["value"] = 42 },
            SnapshotTrigger.Error,
            "failure");
        await hub.FlushAsync();
        hub.Dispose();

        Assert.Empty(writer.Events);
        Assert.Empty(writer.Snapshots);
    }

    [Fact]
    public void Metrics_use_sorted_tags_and_keep_distinct_series()
    {
        using var hub = CreateHub();
        var tags = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" };
        var alternateTags = new Dictionary<string, string> { ["z"] = "last", ["a"] = "alternate" };

        hub.IncrementCounter("test.metric", tags: tags);
        hub.IncrementCounter("test.metric", tags: alternateTags);
        hub.RecordGauge("test.metric", 3.5, tags);
        hub.RecordHistogram("test.metric", 7.5, tags: tags, unit: "ignored");
        var (counters, gauges, histograms) = hub.GetMetricSnapshots();

        Assert.Equal(1, counters["test.metric{a=first,z=last}"]);
        Assert.Equal(1, counters["test.metric{a=alternate,z=last}"]);
        Assert.Equal(3.5, gauges["test.metric{a=first,z=last}"]);
        Assert.Equal((1L, 7.5), histograms["test.metric{a=first,z=last}"]);
        Assert.DoesNotContain("test.metric{a=alternate,z=last}", gauges.Keys);
        Assert.DoesNotContain("test.metric{a=alternate,z=last}", histograms.Keys);
    }

    [Fact]
    public void Metrics_return_independent_snapshots()
    {
        using var hub = CreateHub();
        var tags = new Dictionary<string, string> { ["z"] = "last", ["a"] = "first" };

        hub.IncrementCounter("requests", tags: tags);
        hub.RecordGauge("queue", 3, tags);
        hub.RecordHistogram("latency", 12, tags);
        var (counters, gauges, histograms) = hub.GetMetricSnapshots();

        counters.Clear();
        gauges.Clear();
        histograms.Clear();

        var (freshCounters, freshGauges, freshHistograms) = hub.GetMetricSnapshots();
        Assert.Single(freshCounters);
        Assert.Single(freshGauges);
        Assert.Single(freshHistograms);
    }

    private static ObservabilityHub CreateHub()
    {
        var constructor = typeof(ObservabilityHub).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        return (ObservabilityHub)constructor!.Invoke(null);
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
}
