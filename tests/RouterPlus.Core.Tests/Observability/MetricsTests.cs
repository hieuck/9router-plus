using System.Collections.Concurrent;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class MetricsTests
{
    private static string UniqueMetricName(string name) => $"{name}.{Guid.NewGuid():N}";

    [Fact]
    public void Metrics_with_multiple_tags_use_deterministic_key_order()
    {
        var hub = ObservabilityHub.Instance;
        var metricName = UniqueMetricName("http.requests");

        hub.IncrementCounter(metricName, tags: new Dictionary<string, string>
        {
            ["status"] = "200",
            ["method"] = "GET"
        });

        var (counters, _, _) = hub.GetMetricSnapshots();

        Assert.Equal(1.0, counters[$"{metricName}{{method=GET,status=200}}"]);
    }

    [Fact]
    public void Gauge_and_histogram_with_tags_keep_separate_series()
    {
        var hub = ObservabilityHub.Instance;
        var gaugeName = UniqueMetricName("connections");
        var histogramName = UniqueMetricName("request.duration");
        var tags = new Dictionary<string, string> { ["route"] = "/health" };

        hub.RecordGauge(gaugeName, 3, tags);
        hub.RecordHistogram(histogramName, 12.5, tags);

        var (_, gauges, histograms) = hub.GetMetricSnapshots();

        Assert.Equal(3, gauges[$"{gaugeName}{{route=/health}}"]);
        Assert.Equal((1, 12.5), histograms[$"{histogramName}{{route=/health}}"]);
    }

    [Fact]
    public async Task FlushAsync_writes_queued_events_and_snapshots()
    {
        var writer = new RecordingWriter();
        var hub = ObservabilityHub.Instance;
        hub.SetWriter(writer);

        hub.LogEvent(LogLevel.Info, "Tests", "FlushEvent", "message");
        hub.CaptureSnapshot("Tests", new Dictionary<string, object?> { ["Value"] = 1 }, SnapshotTrigger.OnDemand);

        await hub.FlushAsync();

        var writtenEvent = Assert.Single(writer.Events);
        Assert.Equal("FlushEvent", writtenEvent.Event);
        var snapshot = Assert.Single(writer.Snapshots);
        Assert.Equal("Tests", snapshot.Component);
    }

    [Fact]
    public void MetricEvent_and_histogram_bucket_expose_initialized_values()
    {
        var timestamp = DateTime.UtcNow;
        var metric = new MetricEvent
        {
            Timestamp = timestamp,
            Type = MetricType.Histogram,
            Name = "request.duration",
            Value = 12.5,
            Tags = new Dictionary<string, string> { ["route"] = "/health" },
            Unit = "ms"
        };
        var bucket = new HistogramBucket { UpperBound = 25, Count = 2 };

        Assert.Equal(timestamp, metric.Timestamp);
        Assert.Equal(MetricType.Histogram, metric.Type);
        Assert.Equal("request.duration", metric.Name);
        Assert.Equal(12.5, metric.Value);
        Assert.Equal("/health", metric.Tags!["route"]);
        Assert.Equal("ms", metric.Unit);
        Assert.Equal(25, bucket.UpperBound);
        Assert.Equal(2, bucket.Count);
    }

    private sealed class RecordingWriter : IObservabilityWriter
    {
        public ConcurrentBag<LogEvent> Events { get; } = new();
        public ConcurrentBag<StateSnapshot> Snapshots { get; } = new();

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            foreach (var item in events)
            {
                Events.Add(item);
            }

            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots)
        {
            foreach (var item in snapshots)
            {
                Snapshots.Add(item);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Counter_increments_correctly()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        hub.IncrementCounter("test.requests", 1.0);
        hub.IncrementCounter("test.requests", 2.0);
        hub.IncrementCounter("test.requests", 3.0);

        // Assert
        var (counters, _, _) = hub.GetMetricSnapshots();
        Assert.True(counters.ContainsKey("test.requests"));
        Assert.Equal(6.0, counters["test.requests"]);
    }

    [Fact]
    public void Counter_with_tags_creates_separate_series()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        hub.IncrementCounter("http.requests", 1.0, new() { ["method"] = "GET" });
        hub.IncrementCounter("http.requests", 2.0, new() { ["method"] = "POST" });
        hub.IncrementCounter("http.requests", 1.0, new() { ["method"] = "GET" });

        // Assert
        var (counters, _, _) = hub.GetMetricSnapshots();
        Assert.Equal(2.0, counters["http.requests{method=GET}"]);
        Assert.Equal(2.0, counters["http.requests{method=POST}"]);
    }

    [Fact]
    public void Gauge_records_current_value()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        hub.RecordGauge("memory.used", 100.0);
        hub.RecordGauge("memory.used", 150.0);
        hub.RecordGauge("memory.used", 120.0); // Gauge overwrites

        // Assert
        var (_, gauges, _) = hub.GetMetricSnapshots();
        Assert.Equal(120.0, gauges["memory.used"]);
    }

    [Fact]
    public void Histogram_tracks_distribution()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act - Record request durations
        hub.RecordHistogram("request.duration", 5.0);
        hub.RecordHistogram("request.duration", 15.0);
        hub.RecordHistogram("request.duration", 25.0);
        hub.RecordHistogram("request.duration", 150.0);

        // Assert
        var (_, _, histograms) = hub.GetMetricSnapshots();
        var (count, sum) = histograms["request.duration"];
        Assert.Equal(4, count);
        Assert.Equal(195.0, sum);
        Assert.Equal(48.75, sum / count); // Average
    }

    [Fact]
    public void Histogram_with_unit_records_correctly()
    {
        // Arrange
        var hub = ObservabilityHub.Instance;

        // Act
        hub.RecordHistogram("file.size", 1024.0, unit: "bytes");
        hub.RecordHistogram("file.size", 2048.0, unit: "bytes");

        // Assert
        var (_, _, histograms) = hub.GetMetricSnapshots();
        var (count, sum) = histograms["file.size"];
        Assert.Equal(2, count);
        Assert.Equal(3072.0, sum);
    }
}
