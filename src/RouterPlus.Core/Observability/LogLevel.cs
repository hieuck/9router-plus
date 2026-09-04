namespace RouterPlus.Core.Observability;

/// <summary>
/// Log severity levels for observability events.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Verbose debugging information (only in Debug builds).
    /// </summary>
    Debug = 0,

    /// <summary>
    /// Informational messages about normal application flow.
    /// </summary>
    Info = 1,

    /// <summary>
    /// Warning messages for potentially problematic situations.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Error messages for failures and exceptions.
    /// </summary>
    Error = 3
}
