using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RouterPlus.Core.Observability;

/// <summary>
/// Central hub for collecting observability data (logs, metrics, traces).
/// Singleton that coordinates all diagnostic data collection.
/// </summary>
public sealed class ObservabilityHub : IDisposable
{
    private static readonly Lazy<ObservabilityHub> LazyInstance = new(() => new ObservabilityHub());

    public static ObservabilityHub Instance => LazyInstance.Value;

    private readonly ConcurrentQueue<LogEvent> _eventQueue = new();
    private readonly ConcurrentQueue<StateSnapshot> _snapshotQueue = new();
    private readonly ConcurrentDictionary<string, double> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _flushTask;
    private IObservabilityWriter? _writer;
    private bool _disposed;

    private ObservabilityHub()
    {
        // Start background flush task
        _flushTask = Task.Run(FlushLoopAsync, _shutdownCts.Token);
    }

    /// <summary>
    /// Sets the writer that will persist events to storage.
    /// Must be called once during app initialization.
    /// </summary>
    public void SetWriter(IObservabilityWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <summary>
    /// Logs an informational event.
    /// </summary>
    public void LogEvent(LogLevel level, string category, string eventName, string message, object? context = null)
    {
        if (_disposed) return;

        try
        {
            var logEvent = new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Category = category,
                Event = eventName,
                Message = message,
                Context = context != null ? PrivacyScrubber.Scrub(context) : null
            };

            _eventQueue.Enqueue(logEvent);
        }
        catch
        {
            // Never crash the app due to logging failure
        }
    }

    /// <summary>
    /// Logs an error event with exception details.
    /// </summary>
    public void LogError(string category, string eventName, Exception exception, object? context = null)
    {
        if (_disposed) return;

        try
        {
            var logEvent = new LogEvent
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevel.Error,
                Category = category,
                Event = eventName,
                Message = exception.Message,
                ErrorType = exception.GetType().Name,
                StackTrace = exception.StackTrace,
                Context = context != null ? PrivacyScrubber.Scrub(context) : null
            };

            _eventQueue.Enqueue(logEvent);
        }
        catch
        {
            // Never crash the app due to logging failure
        }
    }

    /// <summary>
    /// Captures a snapshot of application state.
    /// </summary>
    public void CaptureSnapshot(string component, Dictionary<string, object?> state, SnapshotTrigger trigger, string? errorContext = null)
    {
        if (_disposed) return;

        try
        {
            var scrubbedState = PrivacyScrubber.Scrub(state) as Dictionary<string, object?>
                ?? new Dictionary<string, object?>();

            var snapshot = new StateSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Component = component,
                State = scrubbedState,
                Trigger = trigger,
                ErrorContext = errorContext
            };

            _snapshotQueue.Enqueue(snapshot);
        }
        catch
        {
            // Never crash the app due to snapshot failure
        }
    }

    private async Task FlushLoopAsync()
    {
        while (!_shutdownCts.Token.IsCancellationRequested)
        {
            try
            {
                // Flush every 5 seconds
                await Task.Delay(TimeSpan.FromSeconds(5), _shutdownCts.Token);
                await FlushEventsAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch
            {
                // Continue on errors
            }
        }

        // Final flush on shutdown
        await FlushEventsAsync();
    }

    /// <summary>
    /// Flush pending events immediately (primarily for testing).
    /// </summary>
    public async Task FlushAsync()
    {
        await FlushEventsAsync();
    }

    private async Task FlushEventsAsync()
    {
        if (_writer == null) return;

        var events = new System.Collections.Generic.List<LogEvent>();
        while (_eventQueue.TryDequeue(out var evt))
        {
            events.Add(evt);
        }

        var snapshots = new System.Collections.Generic.List<StateSnapshot>();
        while (_snapshotQueue.TryDequeue(out var snapshot))
        {
            snapshots.Add(snapshot);
        }

        if (events.Count > 0)
        {
            await _writer.WriteEventsAsync(events);
        }

        if (snapshots.Count > 0)
        {
            await _writer.WriteSnapshotsAsync(snapshots);
        }
    }

    /// <summary>
    /// Increments a counter metric.
    /// </summary>
    public void IncrementCounter(string name, double delta = 1.0, Dictionary<string, string>? tags = null)
    {
        try
        {
            var key = BuildMetricKey(name, tags);
            _counters.AddOrUpdate(key, delta, (_, current) => current + delta);
        }
        catch
        {
            // Never crash the app due to metrics failure
        }
    }

    /// <summary>
    /// Sets a gauge metric to a specific value.
    /// </summary>
    public void RecordGauge(string name, double value, Dictionary<string, string>? tags = null)
    {
        try
        {
            var key = BuildMetricKey(name, tags);
            _gauges[key] = value;
        }
        catch
        {
            // Never crash the app due to metrics failure
        }
    }

    /// <summary>
    /// Records a histogram observation (e.g., duration, size).
    /// </summary>
    public void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null, string? unit = null)
    {
        try
        {
            var key = BuildMetricKey(name, tags);
            var histogram = _histograms.GetOrAdd(key, _ => CreateDefaultHistogram());
            histogram.Observe(value);
        }
        catch
        {
            // Never crash the app due to metrics failure
        }
    }

    private static string BuildMetricKey(string name, Dictionary<string, string>? tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return name;
        }

        var tagString = string.Join(",", tags.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{name}{{{tagString}}}";
    }

    private static Histogram CreateDefaultHistogram()
    {
        // Default buckets suitable for duration (ms) and size (bytes) measurements
        return new Histogram(new[] { 1.0, 5.0, 10.0, 25.0, 50.0, 100.0, 250.0, 500.0, 1000.0, 2500.0, 5000.0, 10000.0 });
    }

    /// <summary>
    /// Gets current metric snapshots (for testing/debugging).
    /// </summary>
    public (Dictionary<string, double> counters, Dictionary<string, double> gauges, Dictionary<string, (long count, double sum)> histograms) GetMetricSnapshots()
    {
        var counterSnapshot = new Dictionary<string, double>(_counters);
        var gaugeSnapshot = new Dictionary<string, double>(_gauges);
        var histogramSnapshot = new Dictionary<string, (long count, double sum)>();

        foreach (var kvp in _histograms)
        {
            var snapshot = kvp.Value.GetSnapshot();
            histogramSnapshot[kvp.Key] = (snapshot.count, snapshot.sum);
        }

        return (counterSnapshot, gaugeSnapshot, histogramSnapshot);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            // Signal shutdown
            _shutdownCts.Cancel();

            // Wait for flush task to complete (with timeout)
            _flushTask.Wait(TimeSpan.FromSeconds(10));

            _writer?.Dispose();
        }
        catch
        {
            // Best effort cleanup
        }
        finally
        {
            _shutdownCts.Dispose();
        }
    }
}

/// <summary>
/// Interface for observability data writers.
/// </summary>
public interface IObservabilityWriter : IDisposable
{
    Task WriteEventsAsync(System.Collections.Generic.IEnumerable<LogEvent> events);
    Task WriteSnapshotsAsync(System.Collections.Generic.IEnumerable<StateSnapshot> snapshots);
}
