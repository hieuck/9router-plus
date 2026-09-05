namespace RouterPlus.Core.Observability;

/// <summary>
/// Metric types for performance monitoring.
/// </summary>
public enum MetricType
{
    /// <summary>
    /// Counter - monotonically increasing value (e.g., total requests, errors).
    /// </summary>
    Counter,

    /// <summary>
    /// Gauge - point-in-time value that can go up or down (e.g., memory usage, active connections).
    /// </summary>
    Gauge,

    /// <summary>
    /// Histogram - distribution of values (e.g., request durations, file sizes).
    /// </summary>
    Histogram
}

/// <summary>
/// Represents a metric data point.
/// </summary>
public sealed class MetricEvent
{
    public required DateTime Timestamp { get; init; }
    public required MetricType Type { get; init; }
    public required string Name { get; init; }
    public required double Value { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public string? Unit { get; init; }
}

/// <summary>
/// Histogram bucket for distribution tracking.
/// </summary>
public sealed class HistogramBucket
{
    public required double UpperBound { get; init; }
    public int Count { get; set; }
}

/// <summary>
/// Histogram for tracking value distributions.
/// </summary>
public sealed class Histogram
{
    private readonly List<HistogramBucket> _buckets;
    private readonly object _lock = new();
    private long _count;
    private double _sum;

    public Histogram(double[] bucketBounds)
    {
        _buckets = bucketBounds
            .OrderBy(b => b)
            .Select(b => new HistogramBucket { UpperBound = b })
            .ToList();

        // Add +Inf bucket
        _buckets.Add(new HistogramBucket { UpperBound = double.PositiveInfinity });
    }

    public void Observe(double value)
    {
        lock (_lock)
        {
            _count++;
            _sum += value;

            foreach (var bucket in _buckets)
            {
                if (value <= bucket.UpperBound)
                {
                    bucket.Count++;
                }
            }
        }
    }

    public (long count, double sum, IReadOnlyList<HistogramBucket> buckets) GetSnapshot()
    {
        lock (_lock)
        {
            return (_count, _sum, _buckets.ToList());
        }
    }
}
