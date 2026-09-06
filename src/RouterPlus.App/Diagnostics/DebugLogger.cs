using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Observability;

namespace RouterPlus.App.Diagnostics;

/// <summary>
/// Centralized debug logging utility for development diagnostics.
/// All logging is compiled out in Release builds via conditional compilation.
/// In Debug builds, logs are also written to app-debug.log in the working directory.
/// WARNING: app-debug.log may contain PII. Apply privacy scrubbing before sharing.
/// </summary>
/// <remarks>
/// DEPRECATED: This class is obsolete. Use ObservabilityHub instead for unified logging
/// that works in both Debug and Release builds. ObservabilityHub provides structured
/// logging with privacy scrubbing and session-based log files.
/// </remarks>
[Obsolete("Use ObservabilityHub instead. DebugLogger is deprecated and will be removed in a future version.")]
public static class DebugLogger
{
    private static readonly Stopwatch AppStopwatch = Stopwatch.StartNew();
    private static readonly object FileLock = new();
    private static readonly string DebugLogPath = Path.Combine(
        AppContext.BaseDirectory,
        "app-debug.log");

    private static void WriteToFile(string logMessage)
    {
        try
        {
            lock (FileLock)
            {
                File.AppendAllText(DebugLogPath, logMessage + Environment.NewLine);
            }
        }
        catch
        {
            // Best effort - don't crash the app if logging fails
        }
    }

    private static void WriteToFile(string logMessage, Exception ex)
    {
        WriteToFile(logMessage);
        if (ex != null)
        {
            WriteToFile($"  ExceptionType: {ex.GetType().Name}");
            WriteToFile($"  HResult: 0x{ex.HResult:X8}");
            // Scrub exception message and stack trace to prevent PII leakage to disk
            WriteToFile($"  Message: {PrivacyScrubber.ScrubString(ex.Message)}");
            WriteToFile($"  StackTrace: {PrivacyScrubber.ScrubString(ex.StackTrace ?? string.Empty)}");
        }
    }

    /// <summary>
    /// Log a message with timestamp and category
    /// </summary>
    [Conditional("DEBUG")]
    public static void Log(string category, string message)
    {
        var logMessage = $"[{AppStopwatch.ElapsedMilliseconds:D8}ms] [{category}] {message}";
        Debug.WriteLine(logMessage);
        Console.WriteLine(logMessage);
        WriteToFile(logMessage);
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
        WriteToFile(logMessage);
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
        WriteToFile(logMessage);
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
        if (exception is not null)
        {
            WriteToFile(logMessage, exception);
        }
        else
        {
            WriteToFile(logMessage);
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
