using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed class ProfileRowViewModel : INotifyPropertyChanged
{
    private int _displayIndex;
    private bool _isSelected;

    public ProfileRowViewModel(
        ChromeProfile profile,
        IReadOnlyList<ProviderDefinition> providerDefinitions)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ArgumentNullException.ThrowIfNull(providerDefinitions);
        ProviderStatuses = new ObservableCollection<ProfileProviderStatusViewModel>(
            providerDefinitions.Select(definition => new ProfileProviderStatusViewModel(definition)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? SelectionChanged;

    public ChromeProfile Profile { get; }

    public string Name => Profile.Name;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : Name[..1].ToUpperInvariant();

    public string DirectoryName => Profile.DirectoryName;

    public int DisplayIndex => _displayIndex;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ObservableCollection<ProfileProviderStatusViewModel> ProviderStatuses { get; }

    public void SetDisplayIndex(int displayIndex)
    {
        var next = Math.Max(0, displayIndex);
        if (_displayIndex == next)
        {
            return;
        }

        _displayIndex = next;
        OnPropertyChanged(nameof(DisplayIndex));
    }

    public int ConnectedProviderCount => ProviderStatuses.Count(status => status.IsConnected);

    public string ConnectionSummary => ProviderStatuses.Any(status => !status.IsKnown)
        ? "Đang chờ đồng bộ provider…"
        : ConnectedProviderCount == 0
            ? "Chưa có provider được gán"
            : $"{ConnectedProviderCount}/{ProviderStatuses.Count} provider đã thêm";

    public void UpdateConnections(IEnumerable<ProviderConnection> connections)
    {
        var connectionList = connections.ToArray();
        var counts = ProfileConnectionMatcher.CountByProvider(Profile, connectionList);
        foreach (var status in ProviderStatuses)
        {
            var matchingConnections = connectionList
                .Where(connection =>
                    connection.Provider == status.Definition.Kind &&
                    string.Equals(connection.Name?.Trim(), Profile.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var errorConnection = matchingConnections.FirstOrDefault(connection => connection.IsActive && connection.HasError)
                ?? matchingConnections.FirstOrDefault(connection => connection.HasError);
            var healthState = ProviderHealthStateResolver.Resolve(true, matchingConnections);
            status.SetConnectionCount(
                counts[status.Definition.Kind],
                healthState,
                errorConnection?.TestStatus,
                errorConnection?.ErrorCode,
                errorConnection?.LastError,
                matchingConnections);
        }

        OnPropertyChanged(nameof(ConnectedProviderCount));
        OnPropertyChanged(nameof(ConnectionSummary));
    }

    public void MarkStatusUnknown()
    {
        foreach (var status in ProviderStatuses)
        {
            status.MarkUnknown();
        }

        OnPropertyChanged(nameof(ConnectedProviderCount));
        OnPropertyChanged(nameof(ConnectionSummary));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ProfileProviderStatusViewModel : INotifyPropertyChanged
{
    private int _connectionCount;
    private bool _isKnown;
    private ProviderHealthState _healthState = ProviderHealthState.Unknown;
    private string? _errorCode;
    private string? _lastError;
    private string? _testStatus;
    private IReadOnlyList<ProviderConnection> _connections = Array.Empty<ProviderConnection>();
    private bool _hasAutoLoginCredentials;

    public ProfileProviderStatusViewModel(ProviderDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProviderDefinition Definition { get; }

    public string ShortName => Definition.Kind switch
    {
        ProviderKind.OpenRouter => "OpenR",
        ProviderKind.Ollama => "Ollama",
        _ => Definition.DisplayName
    };

    public bool IsKnown => _isKnown;

    public bool IsConnected => _connectionCount > 0;

    public ProviderHealthState HealthState => _healthState;

    public bool IsHealthy => _healthState == ProviderHealthState.Healthy;

    public bool IsDisabled => _healthState == ProviderHealthState.Disabled;

    public bool HasError => _healthState == ProviderHealthState.Error;

    public string? ErrorCode => _errorCode;

    public string? LastError => _lastError;

    public string? TestStatus => _testStatus;

    public IReadOnlyList<ProviderConnection> Connections => _connections;

    public IReadOnlyList<ProviderQuota> QuotaRows => _connections
        .SelectMany(connection => connection.QuotaRows)
        .ToArray();

    public bool HasAutoLoginCredentials => _hasAutoLoginCredentials;

    public string DisplayLabel => $"{ShortName} {StatusMarker}";

    public string StatusMarker => _healthState switch
    {
        ProviderHealthState.Healthy => "✓",
        ProviderHealthState.Disabled => "!",
        ProviderHealthState.Error => "!",
        ProviderHealthState.Missing => "—",
        _ => "?"
    };

    public string ToolTip
    {
        get
        {
            var statusText = _healthState switch
            {
                ProviderHealthState.Unknown => $"{Definition.DisplayName}: chưa đồng bộ",
                ProviderHealthState.Missing => $"{Definition.DisplayName}: chưa có connection tên theo profile",
                ProviderHealthState.Healthy => $"{Definition.DisplayName}: OK · {_connectionCount} connection tên theo profile",
                ProviderHealthState.Disabled => $"{Definition.DisplayName}: có connection nhưng đang tắt",
                ProviderHealthState.Error => $"{Definition.DisplayName}: lỗi{FormatErrorDetails()}",
                _ => Definition.DisplayName
            };

            if (_hasAutoLoginCredentials)
            {
                statusText += " · 🔐 có auto-login";
            }

            return statusText;
        }
    }

    public void SetConnectionCount(
        int connectionCount,
        ProviderHealthState healthState = ProviderHealthState.Unknown,
        string? testStatus = null,
        string? errorCode = null,
        string? lastError = null,
        IReadOnlyList<ProviderConnection>? connections = null)
    {
        _connectionCount = Math.Max(0, connectionCount);
        _isKnown = true;
        _healthState = healthState;
        _testStatus = testStatus;
        _errorCode = errorCode;
        _lastError = SanitizeLastError(lastError);
        _connections = connections ?? Array.Empty<ProviderConnection>();
        RaiseStatusChanged();
    }

    public void SetHasAutoLoginCredentials(bool hasCredentials)
    {
        if (_hasAutoLoginCredentials == hasCredentials)
        {
            return;
        }

        _hasAutoLoginCredentials = hasCredentials;
        OnPropertyChanged(nameof(HasAutoLoginCredentials));
        OnPropertyChanged(nameof(ToolTip));
    }

    public void MarkUnknown()
    {
        _connectionCount = 0;
        _isKnown = false;
        _healthState = ProviderHealthState.Unknown;
        _testStatus = null;
        _errorCode = null;
        _lastError = null;
        _connections = Array.Empty<ProviderConnection>();
        RaiseStatusChanged();
    }

    private string FormatErrorDetails()
    {
        var status = string.IsNullOrWhiteSpace(TestStatus) ? null : $" [{TestStatus}]";
        var code = string.IsNullOrWhiteSpace(ErrorCode) ? null : $" ({ErrorCode})";
        var message = string.IsNullOrWhiteSpace(LastError) ? null : $": {LastError.Trim()}";
        return $"{status}{code}{message}";
    }

    private static string? SanitizeLastError(string? lastError) =>
        string.IsNullOrWhiteSpace(lastError) ? null : "provider trả về lỗi";

    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(IsKnown));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HealthState));
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorCode));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(TestStatus));
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(QuotaRows));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(StatusMarker));
        OnPropertyChanged(nameof(ToolTip));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
