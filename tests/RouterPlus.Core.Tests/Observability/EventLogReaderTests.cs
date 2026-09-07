using System.Globalization;
using System.Text.Json;
using RouterPlus.Infrastructure.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

public sealed class EventLogReaderTests
{
    [Fact]
    public void Constructor_throws_when_paths_are_null()
    {
        // Arrange
        ObservabilityPaths? paths = null;

        // Act
        var action = () => new EventLogReader(paths!);

        // Assert
        Assert.Throws<ArgumentNullException>(action);
    }

    [Fact]
    public void ReadEventsFromSession_returns_empty_when_events_file_is_missing()
    {
        // Arrange
        using var fixture = new EventLogFixture();
        var reader = new EventLogReader(fixture.Paths);

        // Act
        var events = reader.ReadEventsFromSession(fixture.SessionId);

        // Assert
        Assert.Empty(events);
    }

    [Fact]
    public void ReadEventsFromSession_skips_blank_malformed_and_null_lines_and_returns_newest_first()
    {
        // Arrange
        using var fixture = new EventLogFixture();
        fixture.WriteLines(
            "",
            " {\"timestamp\":\"2026-09-07T10:00:00Z\",\"level\":1,\"category\":\"Profile\",\"event\":\"Started\",\"message\":\"first\"} ",
            "not json",
            "null",
            "{\"timestamp\":\"2026-09-07T10:01:00Z\",\"level\":3,\"category\":\"Auth\",\"event\":\"Failed\",\"message\":\"second\"}");
        var reader = new EventLogReader(fixture.Paths);

        // Act
        var events = reader.ReadEventsFromSession(fixture.SessionId);

        // Assert
        Assert.Collection(
            events,
            newest =>
            {
                Assert.Equal("Failed", newest.Operation);
                Assert.Equal("Auth", newest.Category);
            },
            oldest =>
            {
                Assert.Equal("Started", oldest.Operation);
                Assert.Equal("Profile", oldest.Category);
            });
    }

    [Fact]
    public void ReadEventsFromSession_limits_to_last_lines_before_reversing()
    {
        // Arrange
        using var fixture = new EventLogFixture();
        fixture.WriteLines(
            Event("2026-09-07T10:00:00Z", "First"),
            Event("2026-09-07T10:01:00Z", "Second"),
            Event("2026-09-07T10:02:00Z", "Third"));
        var reader = new EventLogReader(fixture.Paths);

        // Act
        var events = reader.ReadEventsFromSession(fixture.SessionId, maxCount: 2);

        // Assert
        Assert.Equal(new[] { "Third", "Second" }, events.Select(e => e.Operation));
    }

    [Fact]
    public void ReadRecentEvents_returns_only_events_after_cutoff_in_descending_timestamp_order()
    {
        // Arrange
        using var fixture = new EventLogFixture();
        fixture.WriteLines(
            Event(DateTime.UtcNow.AddMinutes(-10).ToString("O"), "Old"),
            Event(DateTime.UtcNow.AddMinutes(-2).ToString("O"), "Recent older"),
            Event(DateTime.UtcNow.AddMinutes(-1).ToString("O"), "Recent newer"));
        var reader = new EventLogReader(fixture.Paths);

        // Act
        var events = reader.ReadRecentEvents(fixture.SessionId, TimeSpan.FromMinutes(5));

        // Assert
        Assert.Equal(new[] { "Recent newer", "Recent older" }, events.Select(e => e.Operation));
    }

    [Fact]
    public void ReadFilteredEvents_applies_category_level_search_time_and_max_count_filters()
    {
        // Arrange
        using var fixture = new EventLogFixture();
        fixture.WriteLines(
            Event("2026-09-07T10:00:00Z", "ProfileStarted", level: 1, category: "Profile", message: "Loaded", context: "profile=Default"),
            Event("2026-09-07T10:01:00Z", "AuthFailed", level: 3, category: "Auth", message: "Denied", context: "profile=Default"),
            Event("2026-09-07T10:02:00Z", "AuthWarning", level: 2, category: "Auth", message: "Review", context: "profile=Guest"));
        var reader = new EventLogReader(fixture.Paths);

        // Act
        var byCategoryAndLevel = reader.ReadFilteredEvents(
            fixture.SessionId, category: "auth", level: "ERROR");
        var byOperation = reader.ReadFilteredEvents(fixture.SessionId, searchText: "started");
        var byContext = reader.ReadFilteredEvents(fixture.SessionId, searchText: "guest");
        var byTime = reader.ReadFilteredEvents(
            fixture.SessionId,
            startTime: DateTime.Parse("2026-09-07T10:01:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            endTime: DateTime.Parse("2026-09-07T10:02:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            maxCount: 2);
        var all = reader.ReadFilteredEvents(fixture.SessionId, category: "All", level: "All");

        // Assert
        Assert.Single(byCategoryAndLevel);
        Assert.Equal("AuthFailed", byCategoryAndLevel[0].Operation);
        Assert.Single(byOperation);
        Assert.Equal("ProfileStarted", byOperation[0].Operation);
        Assert.Single(byContext);
        Assert.Equal("AuthWarning", byContext[0].Operation);
        Assert.Equal(new[] { "AuthWarning", "AuthFailed" }, byTime.Select(e => e.Operation));
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetCategories_returns_sorted_distinct_non_empty_categories()
    {
        // Arrange
        using var fixture = new EventLogFixture();
        fixture.WriteLines(
            Event("2026-09-07T10:00:00Z", "One", category: "Zeta"),
            Event("2026-09-07T10:01:00Z", "Two", category: "Alpha"),
            Event("2026-09-07T10:02:00Z", "Three", category: "Zeta"),
            Event("2026-09-07T10:03:00Z", "Four", category: ""));
        var reader = new EventLogReader(fixture.Paths);

        // Act
        var categories = reader.GetCategories(fixture.SessionId);

        // Assert
        Assert.Equal(new[] { "Alpha", "Zeta" }, categories);
    }

    [Fact]
    public void CalculateMetrics_counts_levels_categories_and_time_range()
    {
        // Arrange
        var events = new[]
        {
            CreateEvent("2026-09-07T10:00:00Z", 0, "Profile"),
            CreateEvent("2026-09-07T10:01:00Z", 1, "Profile"),
            CreateEvent("2026-09-07T10:02:00Z", 2, "Auth"),
            CreateEvent("2026-09-07T10:03:00Z", 3, "Auth"),
            CreateEvent("2026-09-07T10:04:00Z", 99, "Other")
        };
        var reader = new EventLogReader(new ObservabilityPaths());

        // Act
        var summary = reader.CalculateMetrics(events.ToList());

        // Assert
        Assert.Equal(5, summary.TotalEvents);
        Assert.Equal(1, summary.DebugCount);
        Assert.Equal(1, summary.InfoCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(2, summary.CategoryCounts["Profile"]);
        Assert.Equal(2, summary.CategoryCounts["Auth"]);
        Assert.Equal(1, summary.CategoryCounts["Other"]);
        Assert.Equal(events[0].Timestamp, summary.OldestEvent);
        Assert.Equal(events[4].Timestamp, summary.NewestEvent);
    }

    [Fact]
    public void CalculateMetrics_returns_empty_time_range_for_no_events()
    {
        // Arrange
        var reader = new EventLogReader(new ObservabilityPaths());

        // Act
        var summary = reader.CalculateMetrics(new List<ObservabilityEvent>());

        // Assert
        Assert.Equal(0, summary.TotalEvents);
        Assert.Null(summary.OldestEvent);
        Assert.Null(summary.NewestEvent);
    }

    [Fact]
    public void ObservabilityEvent_formats_context_values_by_json_kind()
    {
        // Arrange
        using var document = JsonDocument.Parse("{\"text\":\"value\",\"number\":12.5,\"yes\":true,\"no\":false,\"empty\":null,\"object\":{\"nested\":1}}");
        var context = document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        var evt = new ObservabilityEvent { Context = context };

        // Act
        var formatted = evt.FormatContext();

        // Assert
        Assert.Equal("value", evt.GetContextValue("text"));
        Assert.Equal("12.5", evt.GetContextValue("number"));
        Assert.Equal("true", evt.GetContextValue("yes"));
        Assert.Equal("false", evt.GetContextValue("no"));
        Assert.Equal("null", evt.GetContextValue("empty"));
        Assert.Equal("{\"nested\":1}", evt.GetContextValue("object"));
        Assert.Equal(string.Empty, evt.GetContextValue("missing"));
        Assert.Contains("text=value", formatted);
        Assert.Contains("object={\"nested\":1}", formatted);
    }

    [Fact]
    public void ObservabilityEvent_returns_empty_context_for_null_or_empty_context()
    {
        // Arrange
        var withoutContext = new ObservabilityEvent();
        var withEmptyContext = new ObservabilityEvent { Context = new Dictionary<string, JsonElement>() };

        // Act
        var withoutContextText = withoutContext.FormatContext();
        var withEmptyContextText = withEmptyContext.FormatContext();

        // Assert
        Assert.Equal(string.Empty, withoutContextText);
        Assert.Equal(string.Empty, withEmptyContextText);
    }

    private static ObservabilityEvent CreateEvent(string timestamp, int level, string category)
    {
        return JsonSerializer.Deserialize<ObservabilityEvent>(Event(timestamp, "Operation", level, category))!;
    }

    private static string Event(
        string timestamp,
        string operation,
        int level = 1,
        string category = "Category",
        string message = "Message",
        string? context = null)
    {
        var contextJson = context == null ? "{}" : $"{{\"details\":\"{context}\"}}";
        return $"{{\"timestamp\":\"{timestamp}\",\"level\":{level},\"category\":\"{category}\",\"event\":\"{operation}\",\"message\":\"{message}\",\"context\":{contextJson}}}";
    }

    private sealed class EventLogFixture : IDisposable
    {
        private readonly string _rootDirectory;

        public EventLogFixture()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "RouterPlusEventLogReaderTests", Guid.NewGuid().ToString("N"));
            var sessionsDirectory = Path.Combine(_rootDirectory, "sessions");
            Directory.CreateDirectory(sessionsDirectory);

            Paths = new ObservabilityPaths();
            SetReadOnlyProperty(Paths, nameof(ObservabilityPaths.RootDirectory), _rootDirectory);
            SetReadOnlyProperty(Paths, nameof(ObservabilityPaths.SessionsDirectory), sessionsDirectory);
            SessionId = $"event-log-reader-test-{Guid.NewGuid():N}";
            Directory.CreateDirectory(Paths.GetSessionDirectory(SessionId));
        }

        public ObservabilityPaths Paths { get; }
        public string SessionId { get; }

        public void WriteLines(params string[] lines)
        {
            File.WriteAllLines(Paths.GetEventsFilePath(SessionId), lines);
        }

        public void Dispose()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
        }

        private static void SetReadOnlyProperty(ObservabilityPaths paths, string propertyName, string value)
        {
            var field = typeof(ObservabilityPaths).GetField(
                $"<{propertyName}>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field!.SetValue(paths, value);
        }
    }
}
