using System.Diagnostics;

namespace RouterPlus.Infrastructure.Diagnostics;

/// <summary>
/// Debug-only console logging. All calls are stripped from Release builds.
/// </summary>
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
