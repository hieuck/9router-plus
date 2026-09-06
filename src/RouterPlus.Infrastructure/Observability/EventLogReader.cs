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
    public int LevelInt { get; init; }

    /// <summary>
    /// Level as readable string (Debug=0, Info=1, Warning=2, Error=3)
    /// </summary>
    public string Level => LevelInt switch
    {
        0 => "Debug",
        1 => "Info",
        2 => "Warning",
        3 => "Error",
        _ => "Unknown"
    };

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("event")]
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
/// Reads and parses observability events from session events.jsonl
/// </summary>
public sealed class EventLogReader
{
    private readonly ObservabilityPaths _paths;

    public EventLogReader(ObservabilityPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Read events from current session
    /// </summary>
    /// <param name="sessionId">Current session ID</param>
    /// <param name="maxCount">Maximum number of events to read (null = all)</param>
    /// <returns>List of parsed events, newest first</returns>
    public List<ObservabilityEvent> ReadEventsFromSession(string sessionId, int? maxCount = null)
    {
        var eventsPath = _paths.GetEventsFilePath(sessionId);
        return ReadEvents(eventsPath, maxCount);
    }

    /// <summary>
    /// Read events from log file
    /// </summary>
    /// <param name="logPath">Path to events.jsonl</param>
    /// <param name="maxCount">Maximum number of events to read (null = all)</param>
    /// <returns>List of parsed events, newest first</returns>
    private List<ObservabilityEvent> ReadEvents(string logPath, int? maxCount = null)
    {
        if (!File.Exists(logPath))
        {
            System.Diagnostics.Debug.WriteLine($"EventLogReader: File not found: {logPath}");
            return new List<ObservabilityEvent>();
        }

        var events = new List<ObservabilityEvent>();

        try
        {
            // Read all lines with FileShare.Read to allow reading while writer has lock
            string[] lines;
            using (var fileStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fileStream))
            {
                var linesList = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    linesList.Add(line);
                }
                lines = linesList.ToArray();
            }

            System.Diagnostics.Debug.WriteLine($"EventLogReader: Read {lines.Length} lines from {logPath}");

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
                catch (JsonException ex)
                {
                    // Skip malformed JSON lines
                    System.Diagnostics.Debug.WriteLine($"EventLogReader: Failed to parse line {i}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"EventLogReader: Line content: {line.Substring(0, Math.Min(100, line.Length))}");
                    continue;
                }
            }

            System.Diagnostics.Debug.WriteLine($"EventLogReader: Parsed {events.Count} events");

            // Reverse to get newest first
            events.Reverse();
        }
        catch (IOException ex)
        {
            // File in use or inaccessible
            System.Diagnostics.Debug.WriteLine($"EventLogReader: IOException: {ex.Message}");
            return new List<ObservabilityEvent>();
        }

        return events;
    }

    /// <summary>
    /// Read events from a specific time range
    /// </summary>
    /// <param name="sessionId">Current session ID</param>
    /// <param name="duration">How far back to read</param>
    /// <returns>Events within the time range, newest first</returns>
    public List<ObservabilityEvent> ReadRecentEvents(string sessionId, TimeSpan duration)
    {
        var cutoff = DateTime.UtcNow - duration;
        var allEvents = ReadEventsFromSession(sessionId, maxCount: null);

        return allEvents
            .Where(e => e.Timestamp >= cutoff)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Read events matching filter criteria
    /// </summary>
    public List<ObservabilityEvent> ReadFilteredEvents(
        string sessionId,
        string? category = null,
        string? level = null,
        string? searchText = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int? maxCount = null)
    {
        var events = ReadEventsFromSession(sessionId, maxCount);

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
    public List<string> GetCategories(string sessionId)
    {
        var events = ReadEventsFromSession(sessionId, maxCount: 5000);
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
