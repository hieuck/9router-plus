using System.Reflection;
using System.Text.Json;
using RouterPlus.App;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for DiagnosticsViewModel
/// Test-first approach: Write tests before implementation
/// </summary>
public sealed class DiagnosticsViewModelTests : IDisposable
{
    private readonly string _testDirectory;
    private DiagnosticsViewModel? _viewModel;

    public DiagnosticsViewModelTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DiagnosticsVMTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        _viewModel = null;
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        _viewModel = new DiagnosticsViewModel();

        // Assert
        Assert.NotNull(_viewModel.Events);
        Assert.NotNull(_viewModel.EventsView);
        Assert.NotNull(_viewModel.Categories);
        Assert.NotNull(_viewModel.Levels);
        Assert.Contains("All", _viewModel.Categories);
        Assert.Contains("All", _viewModel.Levels);
        Assert.Equal("All", _viewModel.SelectedCategory);
        Assert.Equal("All", _viewModel.SelectedLevel);
        Assert.Empty(_viewModel.SearchText);
    }

    [Fact]
    public void Constructor_InitializesCommands()
    {
        // Arrange & Act
        _viewModel = new DiagnosticsViewModel();

        // Assert
        Assert.NotNull(_viewModel.RefreshCommand);
        Assert.NotNull(_viewModel.ExportCommand);
        Assert.NotNull(_viewModel.ClearLogsCommand);
        Assert.NotNull(_viewModel.OpenLogFolderCommand);
        Assert.NotNull(_viewModel.CopyEventCommand);
    }

    [Fact]
    public void SessionId_WhenApplicationSessionIsUnavailable_ReturnsUnknown()
    {
        // Arrange
        var sessionIdProperty = typeof(App).GetProperty(
            nameof(App.CurrentSessionId),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(sessionIdProperty);
        var originalSessionId = App.CurrentSessionId;

        try
        {
            sessionIdProperty!.SetValue(null, null);
            _viewModel = new DiagnosticsViewModel();

            // Act
            var sessionId = _viewModel.SessionId;

            // Assert
            Assert.Equal("unknown", sessionId);
        }
        finally
        {
            sessionIdProperty!.SetValue(null, originalSessionId);
        }
    }

    [Fact]
    public void SelectedCategory_WhenChanged_FiltersEvents()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        _viewModel.SelectedCategory = "Chrome";

        // Assert
        Assert.Equal("Chrome", _viewModel.SelectedCategory);
        // EventsView should be filtered (tested by checking if Filter is applied)
        Assert.NotNull(_viewModel.EventsView.Filter);
    }

    [Fact]
    public void SelectedLevel_WhenChanged_FiltersEvents()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        _viewModel.SelectedLevel = "Error";

        // Assert
        Assert.Equal("Error", _viewModel.SelectedLevel);
        Assert.NotNull(_viewModel.EventsView.Filter);
    }

    [Fact]
    public void SearchText_WhenChanged_FiltersEvents()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        _viewModel.SearchText = "test query";

        // Assert
        Assert.Equal("test query", _viewModel.SearchText);
        Assert.NotNull(_viewModel.EventsView.Filter);
    }

    [Fact]
    public void SelectedCategory_WhenSetToSameValue_DoesNotRaisePropertyChanged()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var propertyChangedCount = 0;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DiagnosticsViewModel.SelectedCategory))
                propertyChangedCount++;
        };

        // Act
        _viewModel.SelectedCategory = "All";

        // Assert
        Assert.Equal(0, propertyChangedCount);
    }

    [Fact]
    public void SearchText_WhenWhitespace_ShowsAllEvents()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var evt = new ObservabilityEvent
        {
            Category = "Diagnostics",
            Operation = "Refresh",
            Message = "Loaded events"
        };
        _viewModel.Events.Add(evt);

        // Act
        _viewModel.SearchText = "   ";

        // Assert
        Assert.Same(evt, Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>()));
    }

    [Fact]
    public void SearchText_MatchesContextValue()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var evt = new ObservabilityEvent
        {
            Category = "Diagnostics",
            Operation = "Refresh",
            Message = "Loaded events",
            Context = new Dictionary<string, JsonElement>
            {
                ["session_id"] = JsonSerializer.SerializeToElement("session-123")
            }
        };
        _viewModel.Events.Add(evt);

        // Act
        _viewModel.SearchText = "SESSION-123";

        // Assert
        Assert.Same(evt, Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>()));
    }

    [Fact]
    public void Filters_ExcludeEventsByCategoryAndLevel()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var matching = new ObservabilityEvent { Category = "Diagnostics", LevelInt = 3, Message = "match" };
        var wrongCategory = new ObservabilityEvent { Category = "Other", LevelInt = 3, Message = "wrong category" };
        var wrongLevel = new ObservabilityEvent { Category = "Diagnostics", LevelInt = 1, Message = "wrong level" };
        _viewModel.Events.Add(matching);
        _viewModel.Events.Add(wrongCategory);
        _viewModel.Events.Add(wrongLevel);

        // Act
        _viewModel.SelectedCategory = "Diagnostics";
        _viewModel.SelectedLevel = "Error";

        // Assert
        Assert.Same(matching, Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>()));
    }

    [Fact]
    public void SearchText_MatchesOperationAndCategory()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var operationEvent = new ObservabilityEvent { Category = "Unique", Operation = "RefreshNow" };
        var categoryEvent = new ObservabilityEvent { Category = "Diagnostics", Operation = "Different" };
        _viewModel.Events.Add(operationEvent);
        _viewModel.Events.Add(categoryEvent);

        // Act
        _viewModel.SearchText = "refreshnow";

        // Assert
        Assert.Same(operationEvent, Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>()));

        // Act
        _viewModel.SearchText = "diagnostics";

        // Assert
        Assert.Same(categoryEvent, Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>()));
    }

    [Fact]
    public void SelectedEvent_CanBeSetAndRetrieved()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var testEvent = new ObservabilityEvent
        {
            Timestamp = DateTime.UtcNow,
            LevelInt = 1, // Info
            Category = "Test",
            Operation = "TestEvent",
            Message = "Test message"
        };

        // Act
        _viewModel.SelectedEvent = testEvent;

        // Assert
        Assert.Equal(testEvent, _viewModel.SelectedEvent);
    }

    [Fact]
    public void EventsViewFilter_RejectsNonEventsAndMismatchedCategoryAndLevel()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var testEvent = CreateEvent(category: "Chrome", level: 3);
        _viewModel.Events.Add(testEvent);
        var filter = _viewModel.EventsView.Filter!;

        // Act & Assert
        Assert.False(filter(new object()));

        _viewModel.SelectedCategory = "Firefox";
        Assert.False(filter(testEvent));

        _viewModel.SelectedCategory = "All";
        _viewModel.SelectedLevel = "Info";
        Assert.False(filter(testEvent));

        _viewModel.SelectedLevel = "error";
        Assert.True(filter(testEvent));
    }

    [Fact]
    public void EventsViewFilter_SearchesMessageOperationCategoryAndContext()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var testEvent = CreateEvent(
            category: "Chrome",
            operation: "ConnectProfile",
            message: "Network request failed",
            contextValue: "trace-token");
        _viewModel.Events.Add(testEvent);
        var filter = _viewModel.EventsView.Filter!;

        // Act & Assert
        _viewModel.SearchText = "request";
        Assert.True(filter(testEvent));

        _viewModel.SearchText = "profile";
        Assert.True(filter(testEvent));

        _viewModel.SearchText = "chrome";
        Assert.True(filter(testEvent));

        _viewModel.SearchText = "trace-token";
        Assert.True(filter(testEvent));

        _viewModel.SearchText = "not-present";
        Assert.False(filter(testEvent));
    }

    [Fact]
    public void EventsViewFilter_AllowsWhitespaceSearch()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var testEvent = CreateEvent(category: "Chrome");
        _viewModel.Events.Add(testEvent);

        // Act
        _viewModel.SearchText = "   ";

        // Assert
        Assert.True(_viewModel.EventsView.Filter!(testEvent));
    }

    [Fact]
    public void SelectedCategory_SameValue_DoesNotRaisePropertyChangedOrRecalculateMetrics()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(CreateEvent(category: "Chrome"));
        _viewModel.SelectedCategory = "Chrome";
        var metricsBefore = _viewModel.Metrics;
        var propertyChangedCount = 0;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DiagnosticsViewModel.SelectedCategory))
                propertyChangedCount++;
        };

        // Act
        _viewModel.SelectedCategory = "Chrome";

        // Assert
        Assert.Equal(0, propertyChangedCount);
        Assert.Same(metricsBefore, _viewModel.Metrics);
    }

    [Fact]
    public void SelectedLevel_SameValue_DoesNotRaisePropertyChangedOrRecalculateMetrics()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(CreateEvent(level: 1));
        _viewModel.SelectedLevel = "Info";
        var metricsBefore = _viewModel.Metrics;
        var propertyChangedCount = 0;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DiagnosticsViewModel.SelectedLevel))
                propertyChangedCount++;
        };

        // Act
        _viewModel.SelectedLevel = "Info";

        // Assert
        Assert.Equal(0, propertyChangedCount);
        Assert.Same(metricsBefore, _viewModel.Metrics);
    }

    [Fact]
    public void SearchText_SameValue_DoesNotRaisePropertyChangedOrRecalculateMetrics()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(CreateEvent(message: "network request"));
        _viewModel.SearchText = "network";
        var metricsBefore = _viewModel.Metrics;
        var propertyChangedCount = 0;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DiagnosticsViewModel.SearchText))
                propertyChangedCount++;
        };

        // Act
        _viewModel.SearchText = "network";

        // Assert
        Assert.Equal(0, propertyChangedCount);
        Assert.Same(metricsBefore, _viewModel.Metrics);
    }

    [Fact]
    public void Metrics_UpdateWhenCategoryFilterChanges()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(CreateEvent(category: "Chrome"));
        _viewModel.Events.Add(CreateEvent(category: "Chrome", level: 3));
        _viewModel.Events.Add(CreateEvent(category: "Firefox"));

        // Act
        _viewModel.SelectedCategory = "Chrome";

        // Assert
        Assert.NotNull(_viewModel.Metrics);
        Assert.Equal(2, _viewModel.Metrics!.TotalEvents);
        Assert.Equal(1, _viewModel.Metrics.ErrorCount);

        // Act
        _viewModel.SelectedCategory = "All";

        // Assert
        Assert.Equal(3, _viewModel.Metrics.TotalEvents);
    }

    [Fact]
    public void Metrics_UpdateWhenLevelFilterChanges()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(CreateEvent(level: 1));
        _viewModel.Events.Add(CreateEvent(level: 3));
        _viewModel.Events.Add(CreateEvent(level: 2));

        // Act
        _viewModel.SelectedLevel = "Error";

        // Assert
        Assert.NotNull(_viewModel.Metrics);
        Assert.Equal(1, _viewModel.Metrics!.TotalEvents);
        Assert.Equal(1, _viewModel.Metrics.ErrorCount);

        // Act
        _viewModel.SelectedLevel = "All";

        // Assert
        Assert.Equal(3, _viewModel.Metrics.TotalEvents);
    }

    [Fact]
    public void Metrics_UpdateWhenSearchTextChanges()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(CreateEvent(operation: "ConnectProfile", message: "Connected"));
        _viewModel.Events.Add(CreateEvent(operation: "DisconnectProfile", message: "Disconnected"));
        _viewModel.Events.Add(CreateEvent(operation: "Refresh", message: "Updated"));

        // Act
        _viewModel.SearchText = "profile";

        // Assert
        Assert.NotNull(_viewModel.Metrics);
        Assert.Equal(2, _viewModel.Metrics!.TotalEvents);
        Assert.Equal(2, _viewModel.Metrics.InfoCount);

        // Act
        _viewModel.SearchText = string.Empty;

        // Assert
        Assert.Equal(3, _viewModel.Metrics.TotalEvents);
    }

    private static ObservabilityEvent CreateEvent(
        string category = "Test",
        int level = 1,
        string operation = "TestOperation",
        string message = "Test message",
        string? contextValue = null)
    {
        return new ObservabilityEvent
        {
            Timestamp = DateTime.UtcNow,
            LevelInt = level,
            Category = category,
            Operation = operation,
            Message = message,
            Context = contextValue is null
                ? null
                : new Dictionary<string, JsonElement>
                {
                    ["token"] = JsonSerializer.SerializeToElement(contextValue)
                }
        };
    }

    [Fact]
    public void IsLoading_InitiallyFalse()
    {
        // Arrange & Act
        _viewModel = new DiagnosticsViewModel();

        // Assert
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public void StatusMessage_InitiallySet()
    {
        // Arrange & Act
        _viewModel = new DiagnosticsViewModel();

        // Assert
        Assert.NotNull(_viewModel.StatusMessage);
        Assert.NotEmpty(_viewModel.StatusMessage);
    }

    [Fact]
    public void PropertyChanged_RaisedWhenSelectedCategoryChanges()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(DiagnosticsViewModel.SelectedCategory))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.SelectedCategory = "Chrome";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void PropertyChanged_RaisedWhenSelectedLevelChanges()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(DiagnosticsViewModel.SelectedLevel))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.SelectedLevel = "Error";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void PropertyChanged_RaisedWhenSearchTextChanges()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(DiagnosticsViewModel.SearchText))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.SearchText = "test";

        // Assert
        Assert.True(propertyChangedRaised);
    }

    [Fact]
    public void ExportCommand_CanExecute_FalseWhenNoEvents()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        var canExecute = _viewModel.ExportCommand.CanExecute(null);

        // Assert
        Assert.False(canExecute);
    }

    [Fact]
    public async Task RefreshCommand_CanExecute_AlwaysTrue()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Give time for initial load to complete
        await Task.Delay(100);

        // Act
        var canExecute = _viewModel.RefreshCommand.CanExecute(null);

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public void ClearLogsCommand_CanExecute_AlwaysTrue()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        var canExecute = _viewModel.ClearLogsCommand.CanExecute(null);

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public void OpenLogFolderCommand_CanExecute_AlwaysTrue()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        var canExecute = _viewModel.OpenLogFolderCommand.CanExecute(null);

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public void CopyEventCommand_CanExecute_AlwaysTrue()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        var canExecute = _viewModel.CopyEventCommand.CanExecute(null);

        // Assert
        Assert.True(canExecute);
    }

    [Fact]
    public void EventsView_FiltersByCategoryLevelAndSearchContext()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(new ObservabilityEvent
        {
            LevelInt = 3,
            Category = "Chrome",
            Operation = "Launch",
            Message = "Browser failed",
            Context = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["profile"] = System.Text.Json.JsonDocument.Parse("\"work\"").RootElement.Clone()
            }
        });
        _viewModel.Events.Add(new ObservabilityEvent
        {
            LevelInt = 1,
            Category = "Network",
            Operation = "Connect",
            Message = "Connected",
        });

        // Act / Assert: category and level conditions both participate in filtering.
        _viewModel.SelectedCategory = "Chrome";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());
        _viewModel.SelectedLevel = "Error";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());

        // Context is one of the searchable fields.
        _viewModel.SearchText = "work";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());

        _viewModel.SearchText = "missing";
        Assert.Empty(_viewModel.EventsView.Cast<ObservabilityEvent>());
    }

    [Fact]
    public void EventsView_SearchMatchesOperationCategoryAndMessage()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(new ObservabilityEvent
        {
            LevelInt = 1,
            Category = "Network",
            Operation = "Connect",
            Message = "Connected"
        });

        // Act / Assert: exercise each searchable field and the no-search path.
        _viewModel.SearchText = "connected";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());
        _viewModel.SearchText = "connect";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());
        _viewModel.SearchText = "network";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());
        _viewModel.SearchText = "   ";
        Assert.Single(_viewModel.EventsView.Cast<ObservabilityEvent>());
    }

    [Fact]
    public void Filter_ReturnsFalseForNonObservabilityObject()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        var included = _viewModel.EventsView.Filter!(new object());

        // Assert
        Assert.False(included);
    }

    [Fact]
    public void Metrics_ReflectFilteredEventsAndTotalCount()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(new ObservabilityEvent { LevelInt = 3, Category = "Chrome", Message = "failure" });
        _viewModel.Events.Add(new ObservabilityEvent { LevelInt = 2, Category = "Chrome", Message = "warning" });
        _viewModel.Events.Add(new ObservabilityEvent { LevelInt = 1, Category = "Network", Message = "info" });

        // Act
        _viewModel.SelectedCategory = "Chrome";

        // Assert
        Assert.Equal(3, _viewModel.TotalEventCount);
        Assert.NotNull(_viewModel.Metrics);
        Assert.Equal(2, _viewModel.Metrics!.TotalEvents);
        Assert.Equal(1, _viewModel.Metrics.ErrorCount);
        Assert.Equal(1, _viewModel.Metrics.WarningCount);
        Assert.Equal(2, _viewModel.Metrics.CategoryCounts["Chrome"]);
    }

    [Fact]
    public void ExportCommand_CanExecute_TrueWhenEventsExist()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        _viewModel.Events.Add(new ObservabilityEvent());

        // Act / Assert
        Assert.True(_viewModel.ExportCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshCommand_ReportsNotInitializedWithoutSession()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        _viewModel.RefreshCommand.Execute(null);
        await Task.Delay(25);

        // Assert
        Assert.Equal("Observability not initialized", _viewModel.StatusMessage);
        Assert.False(_viewModel.IsLoading);
    }

    [Fact]
    public async Task ClearLogsCommand_ReportsNotInitializedWithoutSession()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        _viewModel.ClearLogsCommand.Execute(null);
        await Task.Delay(25);

        // Assert
        Assert.Equal("Observability not initialized", _viewModel.StatusMessage);
    }

    [Fact]
    public void OpenLogFolderCommand_ReportsNotInitializedWithoutSession()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();

        // Act
        _viewModel.OpenLogFolderCommand.Execute(null);

        // Assert
        Assert.Equal("Observability not initialized", _viewModel.StatusMessage);
    }

    [Fact]
    public void CopyEventCommand_DoesNothingWithoutSelectedEvent()
    {
        // Arrange
        _viewModel = new DiagnosticsViewModel();
        var status = _viewModel.StatusMessage;

        // Act
        _viewModel.CopyEventCommand.Execute(null);

        // Assert
        Assert.Equal(status, _viewModel.StatusMessage);
    }
}
