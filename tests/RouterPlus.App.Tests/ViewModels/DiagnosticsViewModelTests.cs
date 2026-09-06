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
}
