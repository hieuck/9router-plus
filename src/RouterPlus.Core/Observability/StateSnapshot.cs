using System.Text.Json;
using System.Text.Json.Serialization;

namespace RouterPlus.Core.Observability;

/// <summary>
/// Represents a snapshot of application state at a specific point in time.
/// </summary>
public sealed class StateSnapshot
{
    [JsonPropertyName("timestamp")]
    public required DateTime Timestamp { get; init; }

    [JsonPropertyName("component")]
    public required string Component { get; init; }

    [JsonPropertyName("state")]
    public required Dictionary<string, object?> State { get; init; }

    [JsonPropertyName("trigger")]
    public required SnapshotTrigger Trigger { get; init; }

    [JsonPropertyName("error_context")]
    public string? ErrorContext { get; init; }
}

/// <summary>
/// Indicates what caused a state snapshot to be captured.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SnapshotTrigger
{
    Periodic,
    OnDemand,
    Error
}
