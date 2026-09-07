using System.Text.Json;
using RouterPlus.Infrastructure.Observability;

namespace RouterPlus.Infrastructure.Tests.Observability;

public sealed class EventLogReaderTests
{
    [Fact]
    public void Constructor_throws_for_null_paths()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventLogReader(null!));
    }

    [Fact]
    public void ReadEventsFromSession_returns_empty_when_events_file_is_missing()
    {
        // Arrange
        using var session = TestSession.Create();
        var reader = new EventLogReader(session.Paths);

        // Act
        var events = reader.ReadEventsFromSession(session.Id);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void ReadEventsFromSession_skips_blank_and_malformed_lines_and_returns_newest_first()
    {
        // Arrange
        using var session = TestSession.Create();
        session.WriteEvents(
            "",
            EventJson("2026-09-08T10:00:00Z", "Info", "First", "first message"),
            "{not valid json",
            EventJson("2026-09-08T11:00:00Z", "Error", "Second", "second message"));
        var reader = new EventLogReader(session.Paths);

        // Act
        var events = reader.ReadEventsFromSession(session.Id);

        // Assert
        Assert.Collection(
            events,
            current =>
            {
                Assert.Equal("Second", current.Operation);
                Assert.Equal("Error", current.Level);
            },
            current =>
            {
                Assert.Equal("First", current.Operation);
                Assert.Equal("Info", current.Level);
            });
    }

    [Fact]
    public void ReadEventsFromSession_max_count_limits_to_newest_events()
    {
        // Arrange
        using var session = TestSession.Create();
        session.WriteEvents(
            EventJson("2026-09-08T10:00:00Z", "Info", "First", "first"),
            EventJson("2026-09-08T11:00:00Z", "Info", "Second", "second"),
            EventJson("2026-09-08T12:00:00Z", "Info", "Third", "third"));
        var reader = new EventLogReader(session.Paths);

        // Act
        var events = reader.ReadEventsFromSession(session.Id, maxCount: 2);

        // Assert
        Assert.Equal(new[] { "Third", "Second" }, events.Select(e => e.Operation));
    }

    [Fact]
    public void ReadFilteredEvents_applies_category_level_search_and_time_filters()
    {
        // Arrange
        using var session = TestSession.Create();
        session.WriteEvents(
            EventJson("2026-09-08T10:00:00Z", "Info", "Login", "signed in", "Auth", "account=alpha"),
            EventJson("2026-09-08T11:00:00Z", "Warning", "Refresh", "token expiring", "Auth", "account=beta"),
            EventJson("2026-09-08T12:00:00Z", "Error", "Save", "save failed", "Storage", "account=beta"));
        var reader = new EventLogReader(session.Paths);

        // Act
        var events = reader.ReadFilteredEvents(
            session.Id,
            category: "auth",
            level: "warning",
            searchText: "ACCOUNT=BETA",
            startTime: DateTime.Parse("2026-09-08T10:30:00Z").ToUniversalTime(),
            endTime: DateTime.Parse("2026-09-08T11:30:00Z").ToUniversalTime());

        // Assert
        var result = Assert.Single(events);
        Assert.Equal("Refresh", result.Operation);
        Assert.Equal("token expiring", result.Message);
    }

    [Fact]
    public void ReadFilteredEvents_with_all_filters_returns_all_matching_events()
    {
        // Arrange
        using var session = TestSession.Create();
        session.WriteEvents(
            EventJson("2026-09-08T10:00:00Z", "Info", "First", "first", "Auth"),
            EventJson("2026-09-08T11:00:00Z", "Info", "Second", "second", "Auth"));
        var reader = new EventLogReader(session.Paths);

        // Act
        var events = reader.ReadFilteredEvents(session.Id, category: "All", level: "All");

        // Assert
        Assert.Equal(new[] { "Second", "First" }, events.Select(e => e.Operation));
    }

    [Fact]
    public void ReadRecentEvents_returns_events_within_duration_in_newest_first_order()
    {
        // Arrange
        using var session = TestSession.Create();
        var now = DateTime.UtcNow;
        session.WriteEvents(
            EventJson(now.AddMinutes(-30).ToString("O"), "Info", "Old", "old"),
            EventJson(now.AddMinutes(-5).ToString("O"), "Info", "Recent", "recent"));
        var reader = new EventLogReader(session.Paths);

        // Act
        var events = reader.ReadRecentEvents(session.Id, TimeSpan.FromMinutes(10));

        // Assert
        var result = Assert.Single(events);
        Assert.Equal("Recent", result.Operation);
    }

    [Fact]
    public void GetCategories_returns_distinct_nonempty_categories_in_order()
    {
        // Arrange
        using var session = TestSession.Create();
        session.WriteEvents(
            EventJson("2026-09-08T10:00:00Z", "Info", "One", "one", "Storage"),
            EventJson("2026-09-08T11:00:00Z", "Info", "Two", "two", "Auth"),
            EventJson("2026-09-08T12:00:00Z", "Info", "Three", "three", "Storage"),
            EventJson("2026-09-08T13:00:00Z", "Info", "Four", "four", ""));
        var reader = new EventLogReader(session.Paths);

        // Act
        var categories = reader.GetCategories(session.Id);

        // Assert
        Assert.Equal(new[] { "Auth", "Storage" }, categories);
    }

    [Fact]
    public void CalculateMetrics_counts_levels_categories_and_time_bounds()
    {
        // Arrange
        var events = new List<ObservabilityEvent>
        {
            CreateEvent("2026-09-08T10:00:00Z", 0, "Auth"),
            CreateEvent("2026-09-08T11:00:00Z", 1, "Auth"),
            CreateEvent("2026-09-08T12:00:00Z", 2, "Storage"),
            CreateEvent("2026-09-08T13:00:00Z", 3, "Storage"),
            CreateEvent("2026-09-08T14:00:00Z", 99, "Other")
        };
        using var session = TestSession.Create();
        var reader = new EventLogReader(session.Paths);

        // Act
        var metrics = reader.CalculateMetrics(events);

        // Assert
        Assert.Equal(5, metrics.TotalEvents);
        Assert.Equal(1, metrics.DebugCount);
        Assert.Equal(1, metrics.InfoCount);
        Assert.Equal(1, metrics.WarningCount);
        Assert.Equal(1, metrics.ErrorCount);
        Assert.Equal(1, metrics.CategoryCounts["Other"]);
        Assert.Equal("Unknown", events[^1].Level);
        Assert.Equal(DateTime.Parse("2026-09-08T10:00:00Z").ToUniversalTime(), metrics.OldestEvent);
        Assert.Equal(DateTime.Parse("2026-09-08T14:00:00Z").ToUniversalTime(), metrics.NewestEvent);
    }

    [Fact]
    public void ObservabilityEvent_context_values_format_by_json_value_kind()
    {
        // Arrange
        using var document = JsonDocument.Parse("{\"text\":\"hello\",\"number\":42,\"enabled\":true,\"disabled\":false,\"empty\":null,\"items\":[1,2]}");
        var observabilityEvent = new ObservabilityEvent
        {
            Context = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value)
        };

        // Act
        var formatted = observabilityEvent.FormatContext();

        // Assert
        Assert.Equal("hello", observabilityEvent.GetContextValue("text"));
        Assert.Equal("42", observabilityEvent.GetContextValue("number"));
        Assert.Equal("true", observabilityEvent.GetContextValue("enabled"));
        Assert.Equal("false", observabilityEvent.GetContextValue("disabled"));
        Assert.Equal("null", observabilityEvent.GetContextValue("empty"));
        Assert.Equal("[1,2]", observabilityEvent.GetContextValue("items"));
        Assert.Equal(string.Empty, observabilityEvent.GetContextValue("missing"));
        Assert.Contains("text=hello", formatted);
        Assert.Contains("items=[1,2]", formatted);
    }

    [Fact]
    public void CalculateMetrics_for_empty_events_has_no_time_bounds()
    {
        // Arrange
        using var session = TestSession.Create();
        var reader = new EventLogReader(session.Paths);

        // Act
        var metrics = reader.CalculateMetrics(new List<ObservabilityEvent>());

        // Assert
        Assert.Equal(0, metrics.TotalEvents);
        Assert.Null(metrics.OldestEvent);
        Assert.Null(metrics.NewestEvent);
        Assert.Empty(metrics.CategoryCounts);
    }

    [Fact]
    public void ObservabilityEvent_without_context_formats_as_empty()
    {
        // Arrange
        var observabilityEvent = new ObservabilityEvent();

        // Act
        var formatted = observabilityEvent.FormatContext();

        // Assert
        Assert.Equal(string.Empty, formatted);
        Assert.Equal(string.Empty, observabilityEvent.GetContextValue("missing"));
    }

    private static string EventJson(
        string timestamp,
        string level,
        string operation,
        string message,
        string category = "",
        string? contextValue = null)
    {
        var context = contextValue is null ? "{}" : $"{{\"details\":\"{contextValue}\"}}";
        return $"{{\"timestamp\":\"{timestamp}\",\"level\":{LevelNumber(level)},\"category\":\"{category}\",\"event\":\"{operation}\",\"message\":\"{message}\",\"context\":{context}}}";
    }

    private static int LevelNumber(string level) => level switch
    {
        "Debug" => 0,
        "Info" => 1,
        "Warning" => 2,
        "Error" => 3,
        _ => 99
    };

    private static ObservabilityEvent CreateEvent(string timestamp, int level, string category) => new()
    {
        Timestamp = DateTime.Parse(timestamp).ToUniversalTime(),
        LevelInt = level,
        Category = category
    };

    private sealed class TestSession : IDisposable
    {
        private TestSession(string id, ObservabilityPaths paths)
        {
            Id = id;
            Paths = paths;
            Directory.CreateDirectory(Paths.GetSessionDirectory(Id));
        }

        public string Id { get; }
        public ObservabilityPaths Paths { get; }

        public static TestSession Create() => new($"event-reader-test-{Guid.NewGuid():N}", new ObservabilityPaths());

        public void WriteEvents(params string[] lines) => File.WriteAllLines(Paths.GetEventsFilePath(Id), lines);

        public void Dispose()
        {
            var directory = Paths.GetSessionDirectory(Id);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
