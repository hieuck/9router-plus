using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed class ProfileRowViewModel : INotifyPropertyChanged
{
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

    public ChromeProfile Profile { get; }

    public string Name => Profile.Name;

    public string Initial => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : Name[..1].ToUpperInvariant();

    public string DirectoryName => Profile.DirectoryName;

    public ObservableCollection<ProfileProviderStatusViewModel> ProviderStatuses { get; }

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
            var errorConnection = matchingConnections.FirstOrDefault(connection => connection.HasError);
            status.SetConnectionCount(
                counts[status.Definition.Kind],
                errorConnection?.HasError == true,
                errorConnection?.ErrorCode,
                errorConnection?.LastError);
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
    private bool _hasError;
    private string? _errorCode;
    private string? _lastError;

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

    public bool HasError => _hasError;

    public string? ErrorCode => _errorCode;

    public string? LastError => _lastError;

    public string DisplayLabel => $"{ShortName} {StatusMarker}";

    public string StatusMarker => !_isKnown ? "?" : IsConnected ? "✓" : "—";

    public string ToolTip => !_isKnown
        ? $"{Definition.DisplayName}: chưa đồng bộ"
        : HasError
            ? $"{Definition.DisplayName}: lỗi{FormatErrorDetails()}"
            : IsConnected
                ? $"{Definition.DisplayName}: {_connectionCount} connection tên theo profile"
                : $"{Definition.DisplayName}: chưa có connection tên theo profile";

    public void SetConnectionCount(
        int connectionCount,
        bool hasError = false,
        string? errorCode = null,
        string? lastError = null)
    {
        _connectionCount = Math.Max(0, connectionCount);
        _isKnown = true;
        _hasError = hasError;
        _errorCode = errorCode;
        _lastError = lastError;
        RaiseStatusChanged();
    }

    public void MarkUnknown()
    {
        _connectionCount = 0;
        _isKnown = false;
        _hasError = false;
        _errorCode = null;
        _lastError = null;
        RaiseStatusChanged();
    }

    private string FormatErrorDetails()
    {
        var code = string.IsNullOrWhiteSpace(ErrorCode) ? null : $" ({ErrorCode})";
        var message = string.IsNullOrWhiteSpace(LastError) ? null : $": {LastError.Trim()}";
        return $"{code}{message}";
    }

    private void RaiseStatusChanged()
    {
        OnPropertyChanged(nameof(IsKnown));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(ErrorCode));
        OnPropertyChanged(nameof(LastError));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(StatusMarker));
        OnPropertyChanged(nameof(ToolTip));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
