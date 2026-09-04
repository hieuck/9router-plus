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

    private async Task FlushLoopAsync()
    {
        while (!_shutdownCts.Token.IsCancellationRequested)
        {
            try
            {
                // Flush every 5 seconds
                await Task.Delay(TimeSpan.FromSeconds(5), _shutdownCts.Token);
                await FlushAsync();
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
        await FlushAsync();
    }

    private async Task FlushAsync()
    {
        if (_writer == null) return;

        var events = new System.Collections.Generic.List<LogEvent>();
        while (_eventQueue.TryDequeue(out var evt))
        {
            events.Add(evt);
        }

        if (events.Count > 0)
        {
            await _writer.WriteEventsAsync(events);
        }
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
}
