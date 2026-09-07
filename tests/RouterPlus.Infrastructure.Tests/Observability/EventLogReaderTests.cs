using System.Text.Json;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class EventLogReaderTests : IDisposable
{
    private readonly ObservabilityPaths _paths;

    public EventLogReaderTests()
    {
        var root = Path.Combine(Path.GetTempPath(), "RouterPlusTests", $"event_reader_{Guid.NewGuid():N}");
        _paths = new ObservabilityPaths(root);
    }

    [Fact]
    public void ReadEventsFromSession_returns_empty_when_file_is_missing()
    {
        var reader = new EventLogReader(_paths);

        var events = reader.ReadEventsFromSession(UniqueSessionId());

        Assert.Empty(events);
    }

    [Fact]
    public void ReadEventsFromSession_skips_blank_malformed_and_null_lines_and_returns_newest_first()
    {
        var sessionId = UniqueSessionId();
        try
        {
            var first = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
            var second = first.AddMinutes(1);
            WriteLines(sessionId,
                "",
                "not json",
                "null",
                JsonEvent(first, 1, "Auth", "SignedIn", "Welcome"),
                JsonEvent(second, 3, "Auth", "SignedOut", "Goodbye"));

            var reader = new EventLogReader(_paths);
            var events = reader.ReadEventsFromSession(sessionId);

            Assert.Equal(2, events.Count);
            Assert.Equal("SignedOut", events[0].Operation);
            Assert.Equal("SignedIn", events[1].Operation);
        }
        finally
        {
            DeleteSession(sessionId);
        }
    }

    [Fact]
    public void ReadEventsFromSession_limits_to_last_lines_before_reversing()
    {
        var sessionId = UniqueSessionId();
        try
        {
            WriteLines(sessionId,
                JsonEvent(new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc), 1, "Test", "One", "one"),
                JsonEvent(new DateTime(2026, 9, 8, 10, 1, 0, DateTimeKind.Utc), 1, "Test", "Two", "two"),
                JsonEvent(new DateTime(2026, 9, 8, 10, 2, 0, DateTimeKind.Utc), 1, "Test", "Three", "three"));

            var reader = new EventLogReader(_paths);
            var events = reader.ReadEventsFromSession(sessionId, maxCount: 2);

            Assert.Equal(new[] { "Three", "Two" }, events.Select(e => e.Operation));
        }
        finally
        {
            DeleteSession(sessionId);
        }
    }

    [Fact]
    public void ReadFilteredEvents_applies_category_level_text_and_time_filters()
    {
        var sessionId = UniqueSessionId();
        try
        {
            var before = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);
            var matching = before.AddHours(1);
            WriteLines(sessionId,
                JsonEvent(before, 1, "System", "Started", "boot complete"),
                JsonEvent(matching, 2, "Auth", "PasswordChanged", "credential updated", new { account = "synthetic" }),
                JsonEvent(matching.AddHours(1), 3, "Auth", "Failed", "request failed"));

            var reader = new EventLogReader(_paths);
            var events = reader.ReadFilteredEvents(
                sessionId,
                category: "auth",
                level: "Warning",
                searchText: "synthetic",
                startTime: matching,
                endTime: matching);

            var result = Assert.Single(events);
            Assert.Equal("PasswordChanged", result.Operation);
        }
        finally
        {
            DeleteSession(sessionId);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("All", "All")]
    public void ReadFilteredEvents_treats_empty_or_all_category_and_level_as_unrestricted(string? category, string? level)
    {
        var sessionId = UniqueSessionId();
        try
        {
            var timestamp = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
            WriteLines(sessionId,
                JsonEvent(timestamp, 1, "A", "One", "one"),
                JsonEvent(timestamp.AddMinutes(1), 2, "B", "Two", "two"));

            var reader = new EventLogReader(_paths);
            var events = reader.ReadFilteredEvents(sessionId, category, level);

            Assert.Equal(2, events.Count);
        }
        finally
        {
            DeleteSession(sessionId);
        }
    }

    [Fact]
    public void ReadRecentEvents_returns_events_within_duration_newest_first()
    {
        var sessionId = UniqueSessionId();
        try
        {
            var now = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
            WriteLines(sessionId,
                JsonEvent(now.AddHours(-2), 1, "Test", "Old", "old"),
                JsonEvent(now.AddMinutes(-10), 1, "Test", "Recent", "recent"));

            var reader = new EventLogReader(_paths, () => now);
            var events = reader.ReadRecentEvents(sessionId, TimeSpan.FromHours(1));

            var result = Assert.Single(events);
            Assert.Equal("Recent", result.Operation);
        }
        finally
        {
            DeleteSession(sessionId);
        }
    }

    [Fact]
    public void GetCategories_returns_non_empty_categories_sorted_without_duplicates()
    {
        var sessionId = UniqueSessionId();
        try
        {
            WriteLines(sessionId,
                JsonEvent(DateTime.UtcNow, 1, "Zeta", "One", "one"),
                JsonEvent(DateTime.UtcNow, 1, "Alpha", "Two", "two"),
                JsonEvent(DateTime.UtcNow, 1, "Zeta", "Three", "three"),
                JsonEvent(DateTime.UtcNow, 1, "", "Four", "four"));

            var reader = new EventLogReader(_paths);

            Assert.Equal(new[] { "Alpha", "Zeta" }, reader.GetCategories(sessionId));
        }
        finally
        {
            DeleteSession(sessionId);
        }
    }

    [Fact]
    public void ObservabilityEvent_formats_context_values_and_handles_missing_context()
    {
        var evt = JsonSerializer.Deserialize<ObservabilityEvent>(
            "{" +
            "\"timestamp\":\"2026-09-08T10:00:00Z\"," +
            "\"level\":0," +
            "\"category\":\"Test\"," +
            "\"event\":\"Context\"," +
            "\"message\":\"message\"," +
            "\"context\":{" +
            "\"text\":\"value\",\"number\":42,\"yes\":true,\"no\":false,\"empty\":null," +
            "\"array\":[1,2]}}")!;

        Assert.Equal("Debug", evt.Level);
        Assert.Equal("value", evt.GetContextValue("text"));
        Assert.Equal("42", evt.GetContextValue("number"));
        Assert.Equal("true", evt.GetContextValue("yes"));
        Assert.Equal("false", evt.GetContextValue("no"));
        Assert.Equal("null", evt.GetContextValue("empty"));
        Assert.Equal("[1,2]", evt.GetContextValue("array"));
        Assert.Contains("text=value", evt.FormatContext());
        Assert.Equal(string.Empty, evt.GetContextValue("missing"));

        var unknownLevel = new ObservabilityEvent { LevelInt = 99 };
        Assert.Equal("Unknown", unknownLevel.Level);

        var noContext = new ObservabilityEvent();
        Assert.Equal(string.Empty, noContext.FormatContext());
    }

    [Fact]
    public void CalculateMetrics_counts_levels_categories_and_time_bounds()
    {
        var oldest = new DateTime(2026, 9, 8, 10, 0, 0, DateTimeKind.Utc);
        var events = new List<ObservabilityEvent>
        {
            new() { Timestamp = oldest, LevelInt = 0, Category = "A" },
            new() { Timestamp = oldest.AddMinutes(1), LevelInt = 1, Category = "A" },
            new() { Timestamp = oldest.AddMinutes(2), LevelInt = 2, Category = "B" },
            new() { Timestamp = oldest.AddMinutes(3), LevelInt = 3, Category = "B" },
            new() { Timestamp = oldest.AddMinutes(4), LevelInt = 99, Category = "B" }
        };
        var reader = new EventLogReader(_paths);

        var summary = reader.CalculateMetrics(events);

        Assert.Equal(5, summary.TotalEvents);
        Assert.Equal(1, summary.DebugCount);
        Assert.Equal(1, summary.InfoCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(2, summary.CategoryCounts["A"]);
        Assert.Equal(3, summary.CategoryCounts["B"]);
        Assert.Equal(oldest, summary.OldestEvent);
        Assert.Equal(oldest.AddMinutes(4), summary.NewestEvent);

        var empty = reader.CalculateMetrics(new List<ObservabilityEvent>());
        Assert.Null(empty.OldestEvent);
        Assert.Null(empty.NewestEvent);
    }

    private static string UniqueSessionId() => $"event_reader_{Guid.NewGuid():N}";

    private static string JsonEvent(
        DateTime timestamp,
        int level,
        string category,
        string operation,
        string message,
        object? context = null)
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

    private void WriteLines(string sessionId, params string[] lines)
    {
        var paths = _paths;
        var directory = paths.GetSessionDirectory(sessionId);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(paths.GetEventsFilePath(sessionId), lines);
    }

    private void DeleteSession(string sessionId)
    {
        var directory = _paths.GetSessionDirectory(sessionId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_paths.RootDirectory))
        {
            Directory.Delete(_paths.RootDirectory, recursive: true);
        }
    }
}
