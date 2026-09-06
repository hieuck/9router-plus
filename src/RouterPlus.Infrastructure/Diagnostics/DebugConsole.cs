using System.Diagnostics;

namespace RouterPlus.Infrastructure.Diagnostics;

/// <summary>
/// Debug-only console logging. All calls are stripped from Release builds.
/// </summary>
/// <remarks>
/// DEPRECATED: This class is obsolete. Use ObservabilityHub instead for unified logging
/// that works in both Debug and Release builds. ObservabilityHub provides structured
/// logging with privacy scrubbing and session-based log files.
/// </remarks>
[Obsolete("Use ObservabilityHub instead. DebugConsole is deprecated and will be removed in a future version.")]
public static class DebugConsole
{
    /// <summary>
    /// Write a debug message to console. Stripped from Release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void WriteLine(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// Write a formatted debug message to console. Stripped from Release builds.
    /// </summary>
    [Conditional("DEBUG")]
    public static void WriteLine(string format, params object?[] args)
    {
        Console.WriteLine(format, args);
    }
}
