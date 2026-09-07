using System.Globalization;
using System.Text.Json;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class EventLogReaderTests
{
    [Fact]
    public void ReadEventsFromSession_returns_empty_for_missing_file()
    {
        var paths = new ObservabilityPaths();
        using var log = new TemporaryEventLog(paths);
        var reader = new EventLogReader(paths);

        var events = reader.ReadEventsFromSession(log.SessionId);

        Assert.Empty(events);
    }

    [Fact]
    public void ReadEventsFromSession_skips_blank_and_malformed_lines()
    {
        var paths = new ObservabilityPaths();
        using var log = new TemporaryEventLog(paths);
        log.Write(
            "  ",
            EventJson(timestamp: "2026-09-07T10:00:00Z", operation: "valid"),
            "{ not valid json",
            "",
            EventJson(timestamp: "2026-09-07T10:01:00Z", operation: "also-valid"));
        var reader = new EventLogReader(paths);

        var events = reader.ReadEventsFromSession(log.SessionId);

        Assert.Equal(new[] { "also-valid", "valid" }, events.Select(e => e.Operation));
    }

    [Fact]
    public void ReadEventsFromSession_returns_newest_first_and_honors_max_count()
    {
        var paths = new ObservabilityPaths();
        using var log = new TemporaryEventLog(paths);
        log.Write(
            EventJson(timestamp: "2026-09-07T10:00:00Z", operation: "first"),
            EventJson(timestamp: "2026-09-07T10:01:00Z", operation: "second"),
            EventJson(timestamp: "2026-09-07T10:02:00Z", operation: "third"));
        var reader = new EventLogReader(paths);

        var events = reader.ReadEventsFromSession(log.SessionId, maxCount: 2);

        Assert.Equal(new[] { "third", "second" }, events.Select(e => e.Operation));
    }

    [Fact]
    public void ReadFilteredEvents_filters_by_category_level_search_and_time_range()
    {
        var paths = new ObservabilityPaths();
        using var log = new TemporaryEventLog(paths);
        log.Write(
            EventJson("2026-09-07T10:00:00Z", "startup", "System", 1, "Started", new Dictionary<string, object?> { ["host"] = "local" }),
            EventJson("2026-09-07T10:01:00Z", "request-failed", "Http", 3, "Request failed", new Dictionary<string, object?> { ["status"] = 500 }),
            EventJson("2026-09-07T10:02:00Z", "request-ok", "Http", 1, "Request completed", new Dictionary<string, object?> { ["status"] = 200 }));
        var reader = new EventLogReader(paths);

        var allEvents = reader.ReadEventsFromSession(log.SessionId);
        Assert.Equal(3, allEvents.Count);
        Assert.Equal("request-failed", allEvents[1].Operation);
        Assert.Equal("Http", allEvents[1].Category);
        Assert.Equal("Error", allEvents[1].Level);
        Assert.Equal("500", allEvents[1].GetContextValue("status"));
        Assert.Equal(DateTime.Parse("2026-09-07T10:01:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), allEvents[1].Timestamp);
        Assert.Equal(2, reader.ReadFilteredEvents(log.SessionId, category: "http").Count);
        Assert.Single(reader.ReadFilteredEvents(log.SessionId, level: "error"));
        Assert.Single(reader.ReadFilteredEvents(log.SessionId, searchText: "500"));

        var events = reader.ReadFilteredEvents(
            log.SessionId,
            category: "http",
            level: "error",
            searchText: "500",
            startTime: Utc("2026-09-07T10:00:30Z"),
            endTime: Utc("2026-09-07T10:01:30Z"));

        var evt = Assert.Single(events);
        Assert.Equal("request-failed", evt.Operation);
    }

    [Fact]
    public void ReadFilteredEvents_searches_operation_and_treats_all_as_unfiltered()
    {
        var paths = new ObservabilityPaths();
        using var log = new TemporaryEventLog(paths);
        log.Write(
            EventJson("2026-09-07T10:00:00Z", "profile-loaded", "Chrome"),
            EventJson("2026-09-07T10:01:00Z", "other", "System"));
        var reader = new EventLogReader(paths);

        var allEvents = reader.ReadEventsFromSession(log.SessionId);
        Assert.Equal(2, allEvents.Count);
        Assert.Equal("profile-loaded", allEvents[1].Operation);
        Assert.Single(reader.ReadFilteredEvents(log.SessionId, searchText: "PROFILE"));

        var events = reader.ReadFilteredEvents(log.SessionId, category: "All", level: "All", searchText: "PROFILE");

        var evt = Assert.Single(events);
        Assert.Equal("profile-loaded", evt.Operation);
    }

    [Fact]
    public void GetCategories_returns_distinct_nonempty_sorted_categories()
    {
        var paths = new ObservabilityPaths();
        using var log = new TemporaryEventLog(paths);
        log.Write(
            EventJson("2026-09-07T10:00:00Z", "one", "Zeta"),
            EventJson("2026-09-07T10:01:00Z", "two", ""),
            EventJson("2026-09-07T10:02:00Z", "three", "Alpha"),
            EventJson("2026-09-07T10:03:00Z", "four", "Zeta"));
        var reader = new EventLogReader(paths);

        var categories = reader.GetCategories(log.SessionId);

        Assert.Equal(new[] { "Alpha", "Zeta" }, categories);
    }

    [Fact]
    public void CalculateMetrics_counts_levels_and_categories_and_tracks_bounds()
    {
        var events = new[]
        {
            Event("2026-09-07T10:00:00Z", "one", "A", 0),
            Event("2026-09-07T10:01:00Z", "two", "A", 1),
            Event("2026-09-07T10:02:00Z", "three", "B", 2),
            Event("2026-09-07T10:03:00Z", "four", "B", 3),
            Event("2026-09-07T10:04:00Z", "five", "A", 3)
        }.ToList();
        var reader = new EventLogReader(new ObservabilityPaths());

        var metrics = reader.CalculateMetrics(events);

        Assert.Equal(5, metrics.TotalEvents);
        Assert.Equal(2, metrics.ErrorCount);
        Assert.Equal(1, metrics.WarningCount);
        Assert.Equal(1, metrics.InfoCount);
        Assert.Equal(1, metrics.DebugCount);
        Assert.Equal(3, metrics.CategoryCounts["A"]);
        Assert.Equal(2, metrics.CategoryCounts["B"]);
        Assert.Equal(DateTime.Parse("2026-09-07T10:00:00Z"), metrics.OldestEvent);
        Assert.Equal(DateTime.Parse("2026-09-07T10:04:00Z"), metrics.NewestEvent);
    }

    [Fact]
    public void ObservabilityEvent_formats_context_values_for_display()
    {
        var evt = new ObservabilityEvent
        {
            Context = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                "{\"text\":\"hello\",\"number\":42,\"enabled\":true,\"disabled\":false,\"empty\":null,\"nested\":{\"key\":\"value\"},\"items\":[1,2]}")
        };

        Assert.Equal("hello", evt.GetContextValue("text"));
        Assert.Equal("42", evt.GetContextValue("number"));
        Assert.Equal("true", evt.GetContextValue("enabled"));
        Assert.Equal("false", evt.GetContextValue("disabled"));
        Assert.Equal("null", evt.GetContextValue("empty"));
        Assert.Equal("", evt.GetContextValue("missing"));
        Assert.Equal("text=hello, number=42, enabled=true, disabled=false, empty=null, nested={\"key\":\"value\"}, items=[1,2]", evt.FormatContext());
    }

    private static string EventJson(
        string timestamp,
        string operation,
        string category = "Test",
        int level = 1,
        string message = "message",
        Dictionary<string, object?>? context = null)
    {
        return JsonSerializer.Serialize(new
        {
            timestamp,
            level,
            category,
            @event = operation,
            message,
            context
        });
    }

    private static DateTime Utc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static ObservabilityEvent Event(string timestamp, string operation, string category, int level)
    {
        return new ObservabilityEvent
        {
            Timestamp = DateTime.Parse(timestamp),
            Operation = operation,
            Category = category,
            LevelInt = level
        };
    }

    private sealed class TemporaryEventLog : IDisposable
    {
        private readonly string _directory;

        public TemporaryEventLog(ObservabilityPaths paths)
        {
            SessionId = $"event-log-reader-{Guid.NewGuid():N}";
            _directory = paths.GetSessionDirectory(SessionId);
        }

        public string SessionId { get; }

        public void Write(params string[] lines)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllLines(Path.Combine(_directory, "events.jsonl"), lines);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
