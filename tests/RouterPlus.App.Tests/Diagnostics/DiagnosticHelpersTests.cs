using RouterPlus.App.Diagnostics;
using RouterPlus.Core.Observability;

namespace RouterPlus.App.Tests.Diagnostics;

[Collection("Observability")]
public sealed class DiagnosticHelpersTests
{
    [Fact]
    public void DiagnosticCategories_ExposeStableCategoryNames()
    {
        // Arrange
        var expected = new[]
        {
            "UI", "UX", "ViewModel", "Navigation", "Commands", "DataBinding",
            "Chrome", "Providers", "Storage", "Security", "Performance", "Startup", "Updates"
        };

        // Act
        var actual = new[]
        {
            DiagnosticCategories.UI,
            DiagnosticCategories.UX,
            DiagnosticCategories.ViewModel,
            DiagnosticCategories.Navigation,
            DiagnosticCategories.Commands,
            DiagnosticCategories.DataBinding,
            DiagnosticCategories.Chrome,
            DiagnosticCategories.Providers,
            DiagnosticCategories.Storage,
            DiagnosticCategories.Security,
            DiagnosticCategories.Performance,
            DiagnosticCategories.Startup,
            DiagnosticCategories.Updates
        };

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task UIEventLogger_RecordsAllSupportedEventsWithSyntheticContext()
    {
        // Arrange
        var writer = new InMemoryObservabilityWriter();
        var hub = ObservabilityHub.Instance;
        hub.SetWriter(writer);

        // Act
        UIEventLogger.LogClick("SyntheticButton", "synthetic-click");
        UIEventLogger.LogRightClick("SyntheticRow", "synthetic-right-click");
        UIEventLogger.LogDoubleClick("SyntheticItem", "synthetic-double-click");
        UIEventLogger.LogSelection("SyntheticPicker", "synthetic-selection");
        UIEventLogger.LogTextInput("SyntheticSearch", 17);
        UIEventLogger.LogContextMenuOpen("SyntheticContextMenu");
        UIEventLogger.LogDialogOpen("SyntheticDialog");
        UIEventLogger.LogDialogClose("SyntheticDialog", true);
        UIEventLogger.LogDialogClose("SyntheticDialog", false);
        UIEventLogger.LogDialogClose("SyntheticDialog");
        await hub.FlushAsync();

        // Assert
        var events = writer.Events;
        Assert.Equal(10, events.Count);
        Assert.Equal("CLICK SyntheticButton", EventByName(events, "Click").Message);
        Assert.Equal("synthetic-click", Context(EventByName(events, "Click"))["details"]);
        Assert.Equal("RIGHT-CLICK SyntheticRow", EventByName(events, "RightClick").Message);
        Assert.Equal("DOUBLE-CLICK SyntheticItem", EventByName(events, "DoubleClick").Message);
        Assert.Equal("SELECTION SyntheticPicker", EventByName(events, "Selection").Message);
        Assert.Equal("synthetic-selection", Context(EventByName(events, "Selection"))["selected_value"]);
        Assert.Equal("TEXT-INPUT SyntheticSearch", EventByName(events, "TextInput").Message);
        Assert.Equal(17, Context(EventByName(events, "TextInput"))["length"]);
        Assert.Equal("CONTEXT-MENU-OPEN SyntheticContextMenu", EventByName(events, "ContextMenuOpen").Message);
        Assert.Equal("DIALOG-OPEN SyntheticDialog", EventByName(events, "DialogOpen").Message);
        Assert.Equal(
            new[] { "OK", "Cancel", "Closed" },
            events.Where(e => e.Event == "DialogClose").Select(e => Context(e)["result"]).ToArray());
    }

    [Fact]
    public async Task UIEventLogger_LogSelection_UsesExplicitNullMarker()
    {
        // Arrange
        var writer = new InMemoryObservabilityWriter();
        var hub = ObservabilityHub.Instance;
        hub.SetWriter(writer);

        // Act
        UIEventLogger.LogSelection("SyntheticPicker", null);
        await hub.FlushAsync();

        // Assert
        Assert.Equal("null", Context(EventByName(writer.Events, "Selection"))["selected_value"]);
    }

    [Fact]
    public async Task ViewModelLogger_RecordsSupportedOperations()
    {
        // Arrange
        var writer = new InMemoryObservabilityWriter();
        var hub = ObservabilityHub.Instance;
        hub.SetWriter(writer);

        // Act
        ViewModelLogger.LogPropertyChanged("SyntheticViewModel", "SyntheticProperty");
        ViewModelLogger.LogCommandExecute("SyntheticViewModel", "SyntheticCommand", "synthetic-parameter");
        ViewModelLogger.LogCommandCanExecuteChanged("SyntheticViewModel", "SyntheticCommand", false);
        ViewModelLogger.LogDataLoad("SyntheticViewModel", "SyntheticItems", 7);
        await hub.FlushAsync();

        // Assert
        var events = writer.Events;
        Assert.Equal(4, events.Count);
        Assert.Equal("SyntheticViewModel.SyntheticProperty changed", EventByName(events, "PropertyChanged").Message);
        Assert.Equal("synthetic-parameter", Context(EventByName(events, "CommandExecute"))["parameter"]);
        Assert.False((bool)Context(EventByName(events, "CommandCanExecuteChanged"))["can_execute"]!);
        Assert.Equal(7, Context(EventByName(events, "DataLoaded"))["count"]);
    }

    [Fact]
    public async Task ChromeLogger_RecordsSupportedOperationsAndLevels()
    {
        // Arrange
        var writer = new InMemoryObservabilityWriter();
        var hub = ObservabilityHub.Instance;
        hub.SetWriter(writer);

        // Act
        ChromeLogger.LogProfileScan(3, 42);
        ChromeLogger.LogProfileLaunch("Synthetic Profile");
        ChromeLogger.LogProfileLaunchSuccess("Synthetic Profile", 84);
        ChromeLogger.LogProfileLaunchFailed("Synthetic Profile", "synthetic failure");
        await hub.FlushAsync();

        // Assert
        var events = writer.Events;
        Assert.Equal(4, events.Count);
        Assert.Equal(LogLevel.Info, EventByName(events, "ProfileScanCompleted").Level);
        Assert.Equal(3, Context(EventByName(events, "ProfileScanCompleted"))["profile_count"]);
        Assert.Equal(42L, Context(EventByName(events, "ProfileScanCompleted"))["elapsed_ms"]);
        Assert.Equal(LogLevel.Info, EventByName(events, "ProfileLaunchStarted").Level);
        Assert.Equal(LogLevel.Info, EventByName(events, "ProfileLaunchSuccess").Level);
        Assert.Equal(LogLevel.Error, EventByName(events, "ProfileLaunchFailed").Level);
        Assert.Equal("Profile launch failed: synthetic failure", EventByName(events, "ProfileLaunchFailed").Message);
        Assert.Equal("synthetic failure", Context(EventByName(events, "ProfileLaunchFailed"))["reason"]);
    }

    private static LogEvent EventByName(IReadOnlyList<LogEvent> events, string eventName) =>
        Assert.Single(events.Where(e => e.Event == eventName));

    private static Dictionary<string, object?> Context(LogEvent logEvent) =>
        Assert.IsType<Dictionary<string, object?>>(logEvent.Context);

    private sealed class InMemoryObservabilityWriter : IObservabilityWriter
    {
        private readonly List<LogEvent> _events = new();
        private readonly object _sync = new();

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_sync)
                {
                    return _events.ToArray();
                }
            }
        }

        public Task WriteEventsAsync(IEnumerable<LogEvent> events)
        {
            lock (_sync)
            {
                _events.AddRange(events);
            }

            return Task.CompletedTask;
        }

        public Task WriteSnapshotsAsync(IEnumerable<StateSnapshot> snapshots) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
