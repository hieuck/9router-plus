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
    private ProfileHealthStatus? _healthStatus;
    private bool _isCheckingHealth;
    private bool _isNameAmbiguous;

    public ProfileRowViewModel(
        ChromeProfile profile,
        IReadOnlyList<ProviderDefinition> providerDefinitions)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        ArgumentNullException.ThrowIfNull(providerDefinitions);
        ProviderStatuses = new ObservableCollection<ProfileProviderStatusViewModel>(
            providerDefinitions.Select(definition => new ProfileProviderStatusViewModel(definition)));
    }

    /// <summary>
    /// Whether this profile's name is ambiguous (shared by another profile).
    /// When true, no connections should be matched for this profile.
    /// </summary>
    public bool IsNameAmbiguous
    {
        get => _isNameAmbiguous;
        internal set
        {
            if (_isNameAmbiguous == value) return;
            _isNameAmbiguous = value;
            OnPropertyChanged();
        }
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

    /// <summary>
    /// Current health status of this profile.
    /// Null if health check has never been performed.
    /// </summary>
    public ProfileHealthStatus? HealthStatus
    {
        get => _healthStatus;
        set
        {
            if (_healthStatus == value) return;
            _healthStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HealthStatusIcon));
            OnPropertyChanged(nameof(HealthStatusText));
            OnPropertyChanged(nameof(HasHealthIssues));
        }
    }

    /// <summary>
    /// Icon representing health status: "✓" (healthy), "⚠" (warning), "✗" (error), "?" (unknown)
    /// </summary>
    public string HealthStatusIcon => HealthStatus?.Level switch
    {
        HealthLevel.Healthy => "✓",
        HealthLevel.Warning => "⚠",
        HealthLevel.Error => "✗",
        _ => "?"
    };

    /// <summary>
    /// Text summary of health status.
    /// Example: "Profile healthy" / "2 warning(s) detected"
    /// </summary>
    public string HealthStatusText => HealthStatus?.Message ?? "Unknown";

    /// <summary>
    /// Whether this profile has any health issues.
    /// </summary>
    public bool HasHealthIssues => HealthStatus?.Issues.Count > 0;

    /// <summary>
    /// Whether a health check is currently in progress for this profile.
    /// </summary>
    public bool IsCheckingHealth
    {
        get => _isCheckingHealth;
        set
        {
            if (_isCheckingHealth == value) return;
            _isCheckingHealth = value;
            OnPropertyChanged();
        }
    }

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

    public string ConnectedProviderSummary => $"{ConnectedProviderCount} / {ProviderStatuses.Count}";

    public string ConnectionSummary
    {
        get
        {
            var baseSummary = ProviderStatuses.Any(status => !status.IsKnown)
                ? "Đang chờ đồng bộ provider…"
                : ConnectedProviderCount == 0
                    ? "Chưa có provider được gán"
                    : $"{ConnectedProviderCount}/{ProviderStatuses.Count} provider đã thêm";

            var lostSuffix = string.Concat(ProviderStatuses
                .Where(status => status.HasLostConnection)
                .Select(status => $" · Mất {status.ShortName} — thêm lại"));

            return baseSummary + lostSuffix;
        }
    }

    public void UpdateConnections(IEnumerable<ProviderConnection> connections, IEnumerable<ChromeProfile>? allProfiles = null)
    {
        var connectionList = connections.ToArray();

        // If name is ambiguous, don't match any connections
        if (IsNameAmbiguous)
        {
            foreach (var status in ProviderStatuses)
            {
                status.SetConnectionCount(
                    0,
                    ProviderHealthState.Missing,
                    null,
                    null,
                    null,
                    Array.Empty<ProviderConnection>());
            }

            OnPropertyChanged(nameof(ConnectedProviderCount));
            OnPropertyChanged(nameof(ConnectionSummary));
            OnPropertyChanged(nameof(ConnectedProviderSummary));
            return;
        }

        // Use the new matcher with all profiles for ambiguity detection
        var matches = ProfileConnectionMatcher.MatchAllProviders(
            Profile,
            connectionList,
            allProfiles ?? [Profile]);

        foreach (var status in ProviderStatuses)
        {
            var matchingConnections = matches.GetValueOrDefault(status.Definition.Kind, Array.Empty<ProviderConnection>());
            var errorConnection = matchingConnections.FirstOrDefault(connection => connection.IsActive && connection.HasError)
                ?? matchingConnections.FirstOrDefault(connection => connection.HasError);
            var healthState = ProviderHealthStateResolver.Resolve(true, matchingConnections);
            status.SetConnectionCount(
                matchingConnections.Count,
                healthState,
                errorConnection?.TestStatus,
                errorConnection?.ErrorCode,
                errorConnection?.LastError,
                matchingConnections);
        }

        OnPropertyChanged(nameof(ConnectedProviderCount));
        OnPropertyChanged(nameof(ConnectionSummary));
        OnPropertyChanged(nameof(ConnectedProviderSummary));
    }

    public void MarkStatusUnknown()
    {
        foreach (var status in ProviderStatuses)
        {
            status.MarkUnknown();
        }

        OnPropertyChanged(nameof(ConnectedProviderCount));
        OnPropertyChanged(nameof(ConnectionSummary));
        OnPropertyChanged(nameof(ConnectedProviderSummary));
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
    private bool _hasLostConnection;
    private bool _pendingLostConnection;

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

    public bool HasLostConnection => _hasLostConnection;

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
        var hadPreviousConnections = _connections.Count > 0;
        _connectionCount = Math.Max(0, connectionCount);
        _isKnown = true;
        _healthState = healthState;
        _testStatus = testStatus;
        _errorCode = errorCode;
        _lastError = SanitizeLastError(lastError);
        _connections = connections ?? Array.Empty<ProviderConnection>();

        if (_connections.Count > 0)
        {
            _pendingLostConnection = false;
            _hasLostConnection = false;
        }
        else if (_hasLostConnection)
        {
            // Keep lost state while the connection stays missing.
        }
        else if (_pendingLostConnection)
        {
            _hasLostConnection = true;
            _pendingLostConnection = false;
        }
        else if (hadPreviousConnections)
        {
            _pendingLostConnection = true;
            _hasLostConnection = false;
        }
        else
        {
            _pendingLostConnection = false;
            _hasLostConnection = false;
        }

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
        _hasLostConnection = false;
        _pendingLostConnection = false;
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
        OnPropertyChanged(nameof(HasLostConnection));
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
