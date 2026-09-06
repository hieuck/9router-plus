using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Observability;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// ViewModel for Diagnostics Panel window
/// </summary>
public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly EventLogReader _logReader;
    private readonly ObservabilityPaths _paths;
    private readonly string? _sessionId;

    private ObservableCollection<ObservabilityEvent> _events;
    private ICollectionView _eventsView;
    private string _selectedCategory;
    private string _selectedLevel;
    private string _searchText;
    private EventMetricsSummary? _metrics;
    private bool _isLoading;
    private string _statusMessage;
    private ObservabilityEvent? _selectedEvent;

    public DiagnosticsViewModel()
    {
        _paths = new ObservabilityPaths();
        _logReader = new EventLogReader(_paths);
        _sessionId = App.CurrentSessionId;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "Diagnostics",
            "ViewModelCreated",
            $"DiagnosticsViewModel created with sessionId: {_sessionId ?? "NULL"}",
            new { session_id = _sessionId ?? "null", has_value = !string.IsNullOrEmpty(_sessionId) });

        _events = new ObservableCollection<ObservabilityEvent>();
        _eventsView = CollectionViewSource.GetDefaultView(_events);
        _eventsView.Filter = FilterEvent;

        _selectedCategory = "All";
        _selectedLevel = "All";
        _searchText = string.Empty;
        _statusMessage = "Ready";

        // Commands
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => _events.Any());
        ClearLogsCommand = new AsyncRelayCommand(ClearLogsAsync);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        CopyEventCommand = new RelayCommand(CopySelectedEvent);

        // Categories and levels
        Categories = new ObservableCollection<string> { "All" };
        Levels = new ObservableCollection<string> { "All", "Debug", "Info", "Warning", "Error" };

        // Load initial data
        _ = LoadInitialDataAsync();
    }

    public ObservableCollection<ObservabilityEvent> Events
    {
        get => _events;
        private set
        {
            _events = value;
            OnPropertyChanged(nameof(Events));
        }
    }

    public ICollectionView EventsView => _eventsView;

    public ObservableCollection<string> Categories { get; }
    public ObservableCollection<string> Levels { get; }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (_selectedCategory != value)
            {
                _selectedCategory = value;
                OnPropertyChanged(nameof(SelectedCategory));
                _eventsView.Refresh();
                UpdateMetrics();
            }
        }
    }

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (_selectedLevel != value)
            {
                _selectedLevel = value;
                OnPropertyChanged(nameof(SelectedLevel));
                _eventsView.Refresh();
                UpdateMetrics();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                _eventsView.Refresh();
                UpdateMetrics();
            }
        }
    }

    public EventMetricsSummary? Metrics
    {
        get => _metrics;
        private set
        {
            _metrics = value;
            OnPropertyChanged(nameof(Metrics));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public ObservabilityEvent? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            _selectedEvent = value;
            OnPropertyChanged(nameof(SelectedEvent));
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand CopyEventCommand { get; }

    private async Task LoadInitialDataAsync()
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "Diagnostics",
            "RefreshStarted",
            $"RefreshAsync called with sessionId: {_sessionId ?? "NULL"}",
            new { session_id = _sessionId ?? "null" });

        if (string.IsNullOrEmpty(_sessionId))
        {
            StatusMessage = "Observability not initialized";
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Diagnostics",
                "RefreshFailed",
                "SessionId is null or empty",
                new { });
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading events...";

        try
        {
            // Add timeout to prevent infinite hang
            var readTask = Task.Run(() =>
            {
                var eventsPath = _paths.GetEventsFilePath(_sessionId);
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "Diagnostics",
                    "ReadingEvents",
                    $"About to read events from session: {_sessionId}, path: {eventsPath}",
                    new { session_id = _sessionId, events_path = eventsPath, file_exists = System.IO.File.Exists(eventsPath) });

                var events = _logReader.ReadEventsFromSession(_sessionId, maxCount: 1000);

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "Diagnostics",
                    "EventsRead",
                    $"Read {events.Count} events from session",
                    new { session_id = _sessionId, event_count = events.Count });

                return events;
            });

            if (await Task.WhenAny(readTask, Task.Delay(5000)) == readTask)
            {
                // Read completed within timeout
                var events = await readTask;

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _events.Clear();
                    foreach (var evt in events)
                    {
                        _events.Add(evt);
                    }

                    // Update categories
                    var categories = _logReader.GetCategories(_sessionId);
                    Categories.Clear();
                    Categories.Add("All");
                    foreach (var cat in categories)
                    {
                        Categories.Add(cat);
                    }

                    UpdateMetrics();
                });

                StatusMessage = $"Loaded {_events.Count} events";
            }
            else
            {
                // Timeout - EventLogReader is hanging
                StatusMessage = "Timeout: Event reader is not responding";
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error,
                    "Diagnostics",
                    "ReadTimeout",
                    "EventLogReader.ReadEventsFromSession() timed out after 5 seconds",
                    new { session_id = _sessionId });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading events: {ex.Message}";
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "Diagnostics",
                "RefreshError",
                $"Exception in RefreshAsync: {ex.GetType().Name}: {ex.Message}",
                new { session_id = _sessionId, exception_type = ex.GetType().FullName, stack_trace = ex.StackTrace });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool FilterEvent(object obj)
    {
        if (obj is not ObservabilityEvent evt)
            return false;

        // Category filter
        if (SelectedCategory != "All" &&
            !evt.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        // Level filter
        if (SelectedLevel != "All" &&
            !evt.Level.Equals(SelectedLevel, StringComparison.OrdinalIgnoreCase))
            return false;

        // Search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            return evt.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                   evt.Operation.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                   evt.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                   evt.FormatContext().Contains(search, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private void UpdateMetrics()
    {
        var filtered = _eventsView.Cast<ObservabilityEvent>().ToList();
        Metrics = _logReader.CalculateMetrics(filtered);
    }

    private async Task ExportAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "json",
            FileName = $"diagnostics-export-{DateTime.Now:yyyyMMdd-HHmmss}"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Exporting...";

                var filtered = _eventsView.Cast<ObservabilityEvent>().ToList();

                await Task.Run(() =>
                {
                    if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        ExportToCsv(filtered, dialog.FileName);
                    }
                    else
                    {
                        ExportToJson(filtered, dialog.FileName);
                    }
                });

                StatusMessage = $"Exported {filtered.Count} events to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private void ExportToJson(List<ObservabilityEvent> events, string path)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(events, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(path, json);
    }

    private void ExportToCsv(List<ObservabilityEvent> events, string path)
    {
        using var writer = new StreamWriter(path);

        // Header
        writer.WriteLine("Timestamp,Level,Category,Operation,Message,Context");

        // Rows
        foreach (var evt in events)
        {
            writer.WriteLine($"{evt.Timestamp:O},{CsvEscape(evt.Level)},{CsvEscape(evt.Category)},{CsvEscape(evt.Operation)},{CsvEscape(evt.Message)},{CsvEscape(evt.FormatContext())}");
        }
    }

    private string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private Task ClearLogsAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            StatusMessage = "Observability not initialized";
            return Task.CompletedTask;
        }

        var result = System.Windows.MessageBox.Show(
            "This will delete all events in the current session. Continue?",
            "Clear Logs",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                var eventsPath = _paths.GetEventsFilePath(_sessionId);
                if (File.Exists(eventsPath))
                {
                    File.Delete(eventsPath);
                }

                _events.Clear();
                UpdateMetrics();
                StatusMessage = "Logs cleared";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to clear logs: {ex.Message}";
            }
        }

        return Task.CompletedTask;
    }

    private void OpenLogFolder()
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            StatusMessage = "Observability not initialized";
            return;
        }

        var sessionDir = _paths.GetSessionDirectory(_sessionId);
        if (Directory.Exists(sessionDir))
        {
            Process.Start("explorer.exe", sessionDir);
        }
    }

    private void CopySelectedEvent()
    {
        if (SelectedEvent == null)
            return;

        var text = $"[{SelectedEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{SelectedEvent.Level}] [{SelectedEvent.Category}] {SelectedEvent.Operation}: {SelectedEvent.Message}";
        if (!string.IsNullOrEmpty(SelectedEvent.FormatContext()))
        {
            text += $"\nContext: {SelectedEvent.FormatContext()}";
        }

        System.Windows.Clipboard.SetText(text);
        StatusMessage = "Event copied to clipboard";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
