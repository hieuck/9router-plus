using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class MetricsTests
{
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
