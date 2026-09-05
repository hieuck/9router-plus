using System.Diagnostics;

namespace RouterPlus.Core.Observability;

/// <summary>
/// Represents a traced operation with automatic timing and hierarchical structure.
/// Use with 'using' statement for automatic completion tracking.
/// </summary>
public sealed class TraceScope : IDisposable
{
    private readonly string _operationName;
    private readonly string _category;
    private readonly object? _context;
    private readonly Stopwatch _stopwatch;
    private readonly TraceScope? _parent;
    private bool _disposed;

    [ThreadStatic]
    private static TraceScope? _current;

    /// <summary>
    /// Current active trace scope on this thread.
    /// </summary>
    public static TraceScope? Current => _current;

    private TraceScope(string category, string operationName, object? context)
    {
        _category = category;
        _operationName = operationName;
        _context = context;
        _stopwatch = Stopwatch.StartNew();
        _parent = _current;
        _current = this;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            _category,
            $"{_operationName}Started",
            $"Trace started: {_operationName}",
            _context);
    }

    /// <summary>
    /// Begin a new traced operation.
    /// </summary>
    public static TraceScope Begin(string category, string operationName, object? context = null)
    {
        return new TraceScope(category, operationName, context);
    }

    /// <summary>
    /// Log an intermediate event within this trace.
    /// </summary>
    public void LogCheckpoint(string checkpointName, object? checkpointContext = null)
    {
        object combinedContext = (_context, checkpointContext) switch
        {
            (null, null) => new { elapsed_ms = _stopwatch.ElapsedMilliseconds, checkpoint = checkpointName },
            (not null, null) => new { elapsed_ms = _stopwatch.ElapsedMilliseconds, checkpoint = checkpointName, parent_context = _context },
            (null, not null) => new { elapsed_ms = _stopwatch.ElapsedMilliseconds, checkpoint = checkpointName, checkpoint_context = checkpointContext },
            (not null, not null) => new { elapsed_ms = _stopwatch.ElapsedMilliseconds, checkpoint = checkpointName, parent_context = _context, checkpoint_context = checkpointContext }
        };

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            _category,
            $"{_operationName}Checkpoint",
            $"Checkpoint: {checkpointName}",
            combinedContext);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _stopwatch.Stop();

        object completionContext = _context != null
            ? new { duration_ms = _stopwatch.ElapsedMilliseconds, operation_context = _context }
            : new { duration_ms = _stopwatch.ElapsedMilliseconds };

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            _category,
            $"{_operationName}Completed",
            $"Trace completed: {_operationName}",
            completionContext);

        // Record duration metric
        ObservabilityHub.Instance.RecordHistogram(
            $"{_category}.{_operationName}.duration",
            _stopwatch.ElapsedMilliseconds,
            unit: "ms");

        // Restore parent scope
        _current = _parent;
    }
}
