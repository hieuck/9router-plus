using System.Text.Json;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class EventLogReaderTests
{
    [Fact]
    public void Constructor_throws_when_paths_are_null()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventLogReader(null!));
    }

    [Fact]
    public void ReadEventsFromSession_returns_empty_when_events_file_does_not_exist()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadEventsFromSession(sessionId);

            // Assert
            Assert.Empty(events);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadEventsFromSession_parses_valid_lines_skips_blank_and_malformed_lines_and_returns_newest_first()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var firstTimestamp = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var secondTimestamp = firstTimestamp.AddMinutes(1);
        var firstEvent = JsonSerializer.Serialize(new
        {
            timestamp = firstTimestamp,
            level = 1,
            category = "Auth",
            @event = "Login",
            message = "started",
            context = new { count = 2, enabled = true, note = (string?)null }
        });
        var secondEvent = JsonSerializer.Serialize(new
        {
            TimeStamp = secondTimestamp,
            LEVEL = 3,
            CATEGORY = "Router",
            EVENT = "Request",
            MESSAGE = "failed",
            context = new { items = new[] { 1, 2 }, details = new { code = "E1" } }
        });
        WriteEvents(paths, sessionId, string.Join(Environment.NewLine, firstEvent, string.Empty, "not json", secondEvent));
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadEventsFromSession(sessionId);

            // Assert
            Assert.Equal(2, events.Count);
            Assert.Equal(secondTimestamp, events[0].Timestamp);
            Assert.Equal("Error", events[0].Level);
            Assert.Equal("Router", events[0].Category);
            Assert.Equal("Request", events[0].Operation);
            Assert.Equal("failed", events[0].Message);
            Assert.Equal(firstTimestamp, events[1].Timestamp);
            Assert.Equal("2", events[1].GetContextValue("count"));
            Assert.Equal("true", events[1].GetContextValue("enabled"));
            Assert.Equal("null", events[1].GetContextValue("note"));
            Assert.Equal("[1,2]", events[0].GetContextValue("items"));
            Assert.Equal("{\"code\":\"E1\"}", events[0].GetContextValue("details"));
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadEventsFromSession_allows_trailing_commas_in_json_objects()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var timestamp = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        WriteEvents(paths, sessionId, $"{{\"timestamp\":\"{timestamp:O}\",\"message\":\"trailing\",}}");
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadEventsFromSession(sessionId);

            // Assert
            var result = Assert.Single(events);
            Assert.Equal("trailing", result.Message);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadEventsFromSession_returns_empty_when_file_cannot_be_read()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var filePath = paths.GetEventsFilePath(sessionId);
        Directory.CreateDirectory(filePath);
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadEventsFromSession(sessionId);

            // Assert
            Assert.Empty(events);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadEventsFromSession_limits_to_last_lines_before_reversing_results()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var timestamps = Enumerable.Range(0, 3)
            .Select(index => new DateTime(2026, 9, 1, 10, index, 0, DateTimeKind.Utc))
            .ToArray();
        WriteEvents(paths, sessionId, string.Join(Environment.NewLine, timestamps.Select((timestamp, index) =>
            $"{{\"timestamp\":\"{timestamp:O}\",\"category\":\"C{index}\"}}")));
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadEventsFromSession(sessionId, maxCount: 2);

            // Assert
            Assert.Equal(2, events.Count);
            Assert.Equal(timestamps[2], events[0].Timestamp);
            Assert.Equal(timestamps[1], events[1].Timestamp);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadRecentEvents_returns_events_at_or_after_cutoff_in_descending_timestamp_order()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var now = DateTime.UtcNow;
        var cutoffBoundary = now.AddMinutes(-4);
        WriteEvents(paths, sessionId, string.Join(Environment.NewLine,
            EventJson(cutoffBoundary, "boundary"),
            EventJson(now.AddMinutes(-10), "old"),
            EventJson(now.AddMinutes(-1), "recent")));
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadRecentEvents(sessionId, TimeSpan.FromMinutes(5));

            // Assert
            Assert.Equal(2, events.Count);
            Assert.Equal("recent", events[0].Message);
            Assert.Equal("boundary", events[1].Message);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadFilteredEvents_applies_category_level_search_and_time_filters_inclusive()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        var start = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        WriteEvents(paths, sessionId, string.Join(Environment.NewLine,
            EventJson(start, "matching", level: 3, category: "Router", operation: "Request", context: "target"),
            EventJson(start.AddMinutes(1), "different level", level: 1, category: "Router", operation: "Request"),
            EventJson(start.AddMinutes(2), "different category", level: 3, category: "Auth", operation: "Request"),
            EventJson(start.AddMinutes(3), "different text", level: 3, category: "Router", operation: "Health")));
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadFilteredEvents(sessionId, "router", "error", "TARGET", start, start);

            // Assert
            var result = Assert.Single(events);
            Assert.Equal("matching", result.Message);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void ReadFilteredEvents_treats_all_filters_as_unrestricted_when_using_all_or_blank_values()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        WriteEvents(paths, sessionId, string.Join(Environment.NewLine,
            EventJson(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), "one", category: "A"),
            EventJson(new DateTime(2026, 9, 1, 10, 1, 0, DateTimeKind.Utc), "two", category: "B")));
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadFilteredEvents(sessionId, category: "All", level: "All", searchText: " ");

            // Assert
            Assert.Equal(2, events.Count);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void GetCategories_returns_sorted_distinct_non_empty_categories()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var sessionId = CreateSessionId();
        WriteEvents(paths, sessionId, string.Join(Environment.NewLine,
            EventJson(DateTime.UtcNow, "one", category: "Zebra"),
            EventJson(DateTime.UtcNow, "two", category: ""),
            EventJson(DateTime.UtcNow, "three", category: "Alpha"),
            EventJson(DateTime.UtcNow, "four", category: "Zebra")));
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var categories = reader.GetCategories(sessionId);

            // Assert
            Assert.Equal(new[] { "Alpha", "Zebra" }, categories);
        }
        finally
        {
            DeleteSession(paths, sessionId);
        }
    }

    [Fact]
    public void CalculateMetrics_counts_levels_categories_and_timestamp_bounds()
    {
        // Arrange
        var oldest = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var events = new List<ObservabilityEvent>
        {
            new() { Timestamp = oldest, LevelInt = 0, Category = "A" },
            new() { Timestamp = oldest.AddMinutes(1), LevelInt = 1, Category = "A" },
            new() { Timestamp = oldest.AddMinutes(2), LevelInt = 2, Category = "B" },
            new() { Timestamp = oldest.AddMinutes(3), LevelInt = 3, Category = "B" },
            new() { Timestamp = oldest.AddMinutes(4), LevelInt = 99, Category = "B" }
        };
        var reader = new EventLogReader(new ObservabilityPaths());

        // Act
        var metrics = reader.CalculateMetrics(events);

        // Assert
        Assert.Equal(5, metrics.TotalEvents);
        Assert.Equal(1, metrics.DebugCount);
        Assert.Equal(1, metrics.InfoCount);
        Assert.Equal(1, metrics.WarningCount);
        Assert.Equal(1, metrics.ErrorCount);
        Assert.Equal(2, metrics.CategoryCounts["A"]);
        Assert.Equal(3, metrics.CategoryCounts["B"]);
        Assert.Equal(oldest, metrics.OldestEvent);
        Assert.Equal(oldest.AddMinutes(4), metrics.NewestEvent);
    }

    [Fact]
    public void CalculateMetrics_returns_empty_bounds_and_counts_for_empty_input()
    {
        // Arrange
        var reader = new EventLogReader(new ObservabilityPaths());

        // Act
        var metrics = reader.CalculateMetrics(new List<ObservabilityEvent>());

        // Assert
        Assert.Equal(0, metrics.TotalEvents);
        Assert.Empty(metrics.CategoryCounts);
        Assert.Null(metrics.OldestEvent);
        Assert.Null(metrics.NewestEvent);
    }

    [Fact]
    public void ObservabilityEvent_context_helpers_handle_missing_empty_and_all_json_value_kinds()
    {
        // Arrange
        using var document = JsonDocument.Parse("{\"text\":\"value\",\"number\":1.5,\"yes\":true,\"no\":false,\"nothing\":null,\"array\":[1],\"object\":{\"key\":\"value\"}}");
        var context = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        var withoutContext = new ObservabilityEvent();
        var emptyContext = new ObservabilityEvent { Context = new Dictionary<string, JsonElement>() };
        var eventWithContext = new ObservabilityEvent { Context = context };

        // Act
        var missingValue = eventWithContext.GetContextValue("missing");
        var noContextFormatting = withoutContext.FormatContext();
        var emptyContextFormatting = emptyContext.FormatContext();
        var formattedContext = eventWithContext.FormatContext();

        // Assert
        Assert.Equal(string.Empty, missingValue);
        Assert.Equal(string.Empty, noContextFormatting);
        Assert.Equal(string.Empty, emptyContextFormatting);
        Assert.Equal("value", eventWithContext.GetContextValue("text"));
        Assert.Equal("1.5", eventWithContext.GetContextValue("number"));
        Assert.Equal("true", eventWithContext.GetContextValue("yes"));
        Assert.Equal("false", eventWithContext.GetContextValue("no"));
        Assert.Equal("null", eventWithContext.GetContextValue("nothing"));
        Assert.Contains("text=value", formattedContext);
        Assert.Contains("array=[1]", formattedContext);
        Assert.Contains("object={\"key\":\"value\"}", formattedContext);
    }

    [Theory]
    [InlineData(0, "Debug")]
    [InlineData(1, "Info")]
    [InlineData(2, "Warning")]
    [InlineData(3, "Error")]
    [InlineData(99, "Unknown")]
    public void Level_returns_readable_name_for_level_value(int level, string expected)
    {
        // Arrange
        var observabilityEvent = new ObservabilityEvent { LevelInt = level };

        // Act
        var actual = observabilityEvent.Level;

        // Assert
        Assert.Equal(expected, actual);
    }

    private static string CreateSessionId() => $"event-log-reader-{Guid.NewGuid():N}";

    private static void WriteEvents(ObservabilityPaths paths, string sessionId, string content)
    {
        var filePath = paths.GetEventsFilePath(sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
    }

    private static string EventJson(DateTime timestamp, string message, int level = 1, string category = "Category", string operation = "Operation", string? context = null)
    {
        var contextJson = context is null ? "{}" : $"{{\"value\":\"{context}\"}}";
        return $"{{\"timestamp\":\"{timestamp:O}\",\"level\":{level},\"category\":\"{category}\",\"event\":\"{operation}\",\"message\":\"{message}\",\"context\":{contextJson}}}";
    }

    private static void DeleteSession(ObservabilityPaths paths, string sessionId)
    {
        var directory = paths.GetSessionDirectory(sessionId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
