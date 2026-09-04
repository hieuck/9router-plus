using System;

namespace RouterPlus.Core.Observability;

/// <summary>
/// Represents a single log event in the observability system.
/// </summary>
public sealed class LogEvent
{
    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Severity level of the event.
    /// </summary>
    public LogLevel Level { get; init; }

    /// <summary>
    /// Category grouping (e.g., "Chrome", "Security", "HealthCheck").
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// PascalCase event name (e.g., "ProfileSelected", "LoginStarted").
    /// </summary>
    public string Event { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable message describing the event.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Optional duration in milliseconds for timed operations.
    /// </summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Optional contextual data for the event (will be serialized to JSON).
    /// </summary>
    public object? Context { get; init; }

    /// <summary>
    /// Optional exception type name if this is an error event.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Optional stack trace if this is an error event.
    /// </summary>
    public string? StackTrace { get; init; }
}
