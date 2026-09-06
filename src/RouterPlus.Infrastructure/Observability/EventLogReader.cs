using System.Text.Json;
using System.Text.Json.Serialization;

namespace RouterPlus.Infrastructure.Observability;

/// <summary>
/// Represents a parsed observability event from app-debug.log
/// </summary>
public sealed class ObservabilityEvent
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; }

    [JsonPropertyName("level")]
    public string Level { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("operation")]
    public string Operation { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("context")]
    public Dictionary<string, JsonElement>? Context { get; init; }

    /// <summary>
    /// Get context value as string for display
    /// </summary>
    public string GetContextValue(string key)
    {
        if (Context == null || !Context.TryGetValue(key, out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };
    }

    /// <summary>
    /// Format context as readable string
    /// </summary>
    public string FormatContext()
    {
        if (Context == null || Context.Count == 0)
            return string.Empty;

        return string.Join(", ", Context.Select(kvp => $"{kvp.Key}={GetContextValue(kvp.Key)}"));
    }
}

/// <summary>
/// Reads and parses observability events from app-debug.log
/// </summary>
public sealed class EventLogReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Read events from log file
    /// </summary>
    /// <param name="logPath">Path to app-debug.log</param>
    /// <param name="maxCount">Maximum number of events to read (null = all)</param>
    /// <returns>List of parsed events, newest first</returns>
    public List<ObservabilityEvent> ReadEvents(string logPath, int? maxCount = null)
    {
        if (!File.Exists(logPath))
            return new List<ObservabilityEvent>();

        var events = new List<ObservabilityEvent>();

        try
        {
            // Read all lines
            var lines = File.ReadAllLines(logPath);

            // Process from end (newest first) if maxCount specified
            var startIndex = maxCount.HasValue && lines.Length > maxCount.Value
                ? lines.Length - maxCount.Value
                : 0;

            for (int i = startIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                try
                {
                    var evt = JsonSerializer.Deserialize<ObservabilityEvent>(line, JsonOptions);
                    if (evt != null)
                    {
                        events.Add(evt);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed JSON lines
                    continue;
                }
            }

            // Reverse to get newest first
            events.Reverse();
        }
        catch (IOException)
        {
            // File in use or inaccessible
            return new List<ObservabilityEvent>();
        }

        return events;
    }

    /// <summary>
    /// Read events from a specific time range
    /// </summary>
    /// <param name="logPath">Path to app-debug.log</param>
    /// <param name="duration">How far back to read</param>
    /// <returns>Events within the time range, newest first</returns>
    public List<ObservabilityEvent> ReadRecentEvents(string logPath, TimeSpan duration)
    {
        var cutoff = DateTime.UtcNow - duration;
        var allEvents = ReadEvents(logPath, maxCount: null);

        return allEvents
            .Where(e => e.Timestamp >= cutoff)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Read events matching filter criteria
    /// </summary>
    public List<ObservabilityEvent> ReadFilteredEvents(
        string logPath,
        string? category = null,
        string? level = null,
        string? searchText = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? maxCount = null)
    {
        var events = ReadEvents(logPath, maxCount);

        // Apply filters
        IEnumerable<ObservabilityEvent> filtered = events;

        if (!string.IsNullOrWhiteSpace(category) && category != "All")
        {
            filtered = filtered.Where(e => e.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(level) && level != "All")
        {
            filtered = filtered.Where(e => e.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var search = searchText.ToLowerInvariant();
            filtered = filtered.Where(e =>
                e.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.Operation.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                e.FormatContext().Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (startTime.HasValue)
        {
            filtered = filtered.Where(e => e.Timestamp >= startTime.Value);
        }

        if (endTime.HasValue)
        {
            filtered = filtered.Where(e => e.Timestamp <= endTime.Value);
        }

        return filtered.ToList();
    }

    /// <summary>
    /// Get unique categories from log
    /// </summary>
    public List<string> GetCategories(string logPath)
    {
        var events = ReadEvents(logPath, maxCount: 5000);
        return events
            .Select(e => e.Category)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// Calculate simple metrics from events
    /// </summary>
    public EventMetricsSummary CalculateMetrics(List<ObservabilityEvent> events)
    {
        return new EventMetricsSummary
        {
            TotalEvents = events.Count,
            ErrorCount = events.Count(e => e.Level.Equals("Error", StringComparison.OrdinalIgnoreCase)),
            WarningCount = events.Count(e => e.Level.Equals("Warning", StringComparison.OrdinalIgnoreCase)),
            InfoCount = events.Count(e => e.Level.Equals("Info", StringComparison.OrdinalIgnoreCase)),
            DebugCount = events.Count(e => e.Level.Equals("Debug", StringComparison.OrdinalIgnoreCase)),
            CategoryCounts = events
                .GroupBy(e => e.Category)
                .ToDictionary(g => g.Key, g => g.Count()),
            OldestEvent = events.Any() ? events.Min(e => e.Timestamp) : (DateTime?)null,
            NewestEvent = events.Any() ? events.Max(e => e.Timestamp) : (DateTime?)null
        };
    }
}

/// <summary>
/// Summary metrics calculated from events
/// </summary>
public sealed class EventMetricsSummary
{
    public int TotalEvents { get; init; }
    public int ErrorCount { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public int DebugCount { get; init; }
    public Dictionary<string, int> CategoryCounts { get; init; } = new();
    public DateTime? OldestEvent { get; init; }
    public DateTime? NewestEvent { get; init; }
}
