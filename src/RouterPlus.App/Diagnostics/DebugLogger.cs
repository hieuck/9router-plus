using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RouterPlus.App.Diagnostics;

/// <summary>
/// Centralized debug logging utility for development diagnostics.
/// All logging is compiled out in Release builds via conditional compilation.
/// </summary>
public static class DebugLogger
{
    private static readonly Stopwatch AppStopwatch = Stopwatch.StartNew();

    /// <summary>
    /// Log a message with timestamp and category
    /// </summary>
    [Conditional("DEBUG")]
    public static void Log(string category, string message)
    {
        var logMessage = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] {message}";
        Debug.WriteLine(logMessage);
        Console.WriteLine(logMessage);
    }

    /// <summary>
    /// Log the start of an operation
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogStart(string category, string operation, [CallerMemberName] string? caller = null)
    {
        var logMessage = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] START {operation} (from {caller})";
        Debug.WriteLine(logMessage);
        Console.WriteLine(logMessage);
    }

    /// <summary>
    /// Log the completion of an operation with timing
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogEnd(string category, string operation, long elapsedMs, [CallerMemberName] string? caller = null)
    {
        var logMessage = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] END {operation} ({elapsedMs}ms) (from {caller})";
        Debug.WriteLine(logMessage);
        Console.WriteLine(logMessage);
    }

    /// <summary>
    /// Log an error or exception
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogError(string category, string message, Exception? exception = null)
    {
        var logMessage = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] ERROR: {message}";
        Debug.WriteLine(logMessage);
        Console.WriteLine(logMessage);
        if (exception != null)
        {
            var exTypeMsg = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}]   ExceptionType: {exception.GetType().Name}";
            var hResultMsg = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}]   HResult: 0x{exception.HResult:X8}";
            Debug.WriteLine(exTypeMsg);
            Debug.WriteLine(hResultMsg);
            Console.WriteLine(exTypeMsg);
            Console.WriteLine(hResultMsg);
        }
    }

    /// <summary>
    /// Log a warning
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogWarning(string category, string message)
    {
        Debug.WriteLine($"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] WARNING: {message}");
    }

    /// <summary>
    /// Log performance timing for an operation
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogPerformance(string category, string operation, Stopwatch sw, string? details = null)
    {
        var detailsStr = details != null ? $" - {details}" : "";
        Debug.WriteLine($"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] PERF {operation}: {sw.ElapsedMilliseconds}ms{detailsStr}");
    }

    /// <summary>
    /// Log a separator for visual grouping
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogSeparator(string category)
    {
        Debug.WriteLine($"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] ========================================");
    }

    /// <summary>
    /// Create a performance measurement scope.
    /// Usage: using var perf = DebugLogger.MeasurePerformance("Category", "Operation");
    /// </summary>
    public static PerformanceScope MeasurePerformance(string category, string operation)
    {
        return new PerformanceScope(category, operation);
    }

    /// <summary>
    /// Disposable scope for measuring performance
    /// </summary>
    public readonly struct PerformanceScope : IDisposable
    {
#if DEBUG
        private readonly string _category;
        private readonly string _operation;
        private readonly Stopwatch _sw;
#endif

        public PerformanceScope(string category, string operation)
        {
#if DEBUG
            _category = category;
            _operation = operation;
            _sw = Stopwatch.StartNew();
            LogStart(category, operation);
#endif
        }

        public void Dispose()
        {
#if DEBUG
            _sw.Stop();
            LogEnd(_category, _operation, _sw.ElapsedMilliseconds);
#endif
        }
    }
}
