using System.Text.Json;
using RouterPlus.Infrastructure.Observability;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class EventLogReaderTests
{
    [Fact]
    public void ObservabilityEvent_formats_levels_and_context_values()
    {
        // Arrange
        var context = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            "{\"text\":\"value\",\"number\":42,\"enabled\":true,\"disabled\":false,\"empty\":null,\"object\":{\"nested\":1}}")!;
        var evt = new ObservabilityEvent { Context = context };

        // Act and Assert
        Assert.Equal("Debug", new ObservabilityEvent { LevelInt = 0 }.Level);
        Assert.Equal("Info", new ObservabilityEvent { LevelInt = 1 }.Level);
        Assert.Equal("Warning", new ObservabilityEvent { LevelInt = 2 }.Level);
        Assert.Equal("Error", new ObservabilityEvent { LevelInt = 3 }.Level);
        Assert.Equal("Unknown", new ObservabilityEvent { LevelInt = 99 }.Level);
        Assert.Equal("value", evt.GetContextValue("text"));
        Assert.Equal("42", evt.GetContextValue("number"));
        Assert.Equal("true", evt.GetContextValue("enabled"));
        Assert.Equal("false", evt.GetContextValue("disabled"));
        Assert.Equal("null", evt.GetContextValue("empty"));
        Assert.Equal("{\"nested\":1}", evt.GetContextValue("object"));
        Assert.Equal(string.Empty, evt.GetContextValue("missing"));
        Assert.Contains("text=value", evt.FormatContext());
        Assert.Contains("object={\"nested\":1}", evt.FormatContext());
        Assert.Equal(string.Empty, new ObservabilityEvent().FormatContext());
        Assert.Equal(string.Empty, new ObservabilityEvent { Context = new() }.FormatContext());
    }

    [Fact]
    public void ReadEventsFromSession_returns_empty_when_file_is_missing()
    {
        // Arrange
        var paths = new ObservabilityPaths();
        var reader = new EventLogReader(paths);

        // Act
        var events = reader.ReadEventsFromSession(UniqueSessionId());

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void ReadEventsFromSession_skips_blank_malformed_and_null_lines_and_returns_newest_first()
    {
        // Arrange
        var sessionId = UniqueSessionId();
        var paths = new ObservabilityPaths();
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllLines(paths.GetEventsFilePath(sessionId),
        [
            EventJson("2026-09-08T10:00:00Z", 1, "First", "first"),
            "",
            "not-json",
            "null",
            EventJson("2026-09-08T10:01:00Z", 3, "Second", "second"),
            EventJson("2026-09-08T10:02:00Z", 2, "Third", "third")
        ]);
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadEventsFromSession(sessionId);
            var limited = reader.ReadEventsFromSession(sessionId, maxCount: 2);

            // Assert
            Assert.Equal(["third", "second", "first"], events.Select(e => e.Operation));
            Assert.Equal(["third", "second"], limited.Select(e => e.Operation));
        }
        finally
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReadRecentEvents_returns_events_at_or_after_cutoff_newest_first()
    {
        // Arrange
        var sessionId = UniqueSessionId();
        var paths = new ObservabilityPaths();
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllLines(paths.GetEventsFilePath(sessionId),
        [
            EventJson(DateTime.UtcNow.AddMinutes(-10), 1, "Old", "old"),
            EventJson(DateTime.UtcNow.AddMinutes(-1), 1, "Recent", "recent")
        ]);
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var events = reader.ReadRecentEvents(sessionId, TimeSpan.FromMinutes(5));

            // Assert
            var single = Assert.Single(events);
            Assert.Equal("recent", single.Operation);
        }
        finally
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    [Fact]
    public void ReadFilteredEvents_applies_category_level_search_and_time_filters()
    {
        // Arrange
        var sessionId = UniqueSessionId();
        var paths = new ObservabilityPaths();
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllLines(paths.GetEventsFilePath(sessionId),
        [
            EventJson("2026-09-08T10:00:00Z", 1, "Login", "signed in", "account=synthetic"),
            EventJson("2026-09-08T10:01:00Z", 3, "Router", "request failed"),
            EventJson("2026-09-08T10:02:00Z", 1, "Login", "signed out")
        ]);
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var category = reader.ReadFilteredEvents(sessionId, category: "login");
            var level = reader.ReadFilteredEvents(sessionId, level: "ERROR");
            var contextSearch = reader.ReadFilteredEvents(sessionId, searchText: "SYNTHETIC");
            var range = reader.ReadFilteredEvents(
                sessionId,
                startTime: DateTime.Parse("2026-09-08T10:01:00Z").ToUniversalTime(),
                endTime: DateTime.Parse("2026-09-08T10:01:00Z").ToUniversalTime());
            var all = reader.ReadFilteredEvents(sessionId, category: "All", level: "All", maxCount: 2);

            // Assert
            Assert.Equal(["Login", "Login"], category.Select(e => e.Category));
            Assert.Equal("Router", Assert.Single(level).Category);
            Assert.Equal("Login", Assert.Single(contextSearch).Category);
            Assert.Equal("Router", Assert.Single(range).Category);
            Assert.Equal(["Login", "Router"], all.Select(e => e.Category));
        }
        finally
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetCategories_returns_sorted_distinct_nonempty_categories()
    {
        // Arrange
        var sessionId = UniqueSessionId();
        var paths = new ObservabilityPaths();
        var sessionDirectory = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllLines(paths.GetEventsFilePath(sessionId),
        [
            EventJson("2026-09-08T10:00:00Z", 1, "Zeta", "z"),
            EventJson("2026-09-08T10:01:00Z", 1, "", "empty"),
            EventJson("2026-09-08T10:02:00Z", 1, "Alpha", "a"),
            EventJson("2026-09-08T10:03:00Z", 1, "Zeta", "z2")
        ]);
        var reader = new EventLogReader(paths);

        try
        {
            // Act
            var categories = reader.GetCategories(sessionId);

            // Assert
            Assert.Equal(["Alpha", "Zeta"], categories);
        }
        finally
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    [Fact]
    public void CalculateMetrics_counts_levels_categories_and_time_bounds()
    {
        // Arrange
        var events = new List<ObservabilityEvent>
        {
            new() { Timestamp = DateTime.Parse("2026-09-08T10:00:00Z").ToUniversalTime(), LevelInt = 0, Category = "A" },
            new() { Timestamp = DateTime.Parse("2026-09-08T10:01:00Z").ToUniversalTime(), LevelInt = 1, Category = "A" },
            new() { Timestamp = DateTime.Parse("2026-09-08T10:02:00Z").ToUniversalTime(), LevelInt = 2, Category = "B" },
            new() { Timestamp = DateTime.Parse("2026-09-08T10:03:00Z").ToUniversalTime(), LevelInt = 3, Category = "B" }
        };
        var reader = new EventLogReader(new ObservabilityPaths());

        // Act
        var metrics = reader.CalculateMetrics(events);
        var empty = reader.CalculateMetrics([]);

        // Assert
        Assert.Equal(4, metrics.TotalEvents);
        Assert.Equal(1, metrics.DebugCount);
        Assert.Equal(1, metrics.InfoCount);
        Assert.Equal(1, metrics.WarningCount);
        Assert.Equal(1, metrics.ErrorCount);
        Assert.Equal(2, metrics.CategoryCounts["A"]);
        Assert.Equal(2, metrics.CategoryCounts["B"]);
        Assert.Equal(events[0].Timestamp, metrics.OldestEvent);
        Assert.Equal(events[^1].Timestamp, metrics.NewestEvent);
        Assert.Null(empty.OldestEvent);
        Assert.Null(empty.NewestEvent);
    }

    private static string UniqueSessionId() => $"event_reader_test_{Guid.NewGuid():N}";

    private static string EventJson(
        object timestamp,
        int level,
        string category,
        string operation,
        string? context = null)
    {
        var contextJson = context is null ? "{}" : $"{{\"details\":\"{context}\"}}";
        var timestampText = timestamp is DateTime dateTime ? dateTime.ToString("O") : timestamp.ToString();
        return $"{{\"timestamp\":\"{timestampText}\",\"level\":{level},\"category\":\"{category}\",\"event\":\"{operation}\",\"message\":\"message\",\"context\":{contextJson}}}";
    }
}
