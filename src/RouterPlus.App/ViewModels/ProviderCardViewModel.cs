using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;

namespace RouterPlus.App.ViewModels;

public sealed class ProviderCardViewModel : INotifyPropertyChanged
{
    private readonly ProviderApiKeyState _apiKeyState = new();
    private ProviderHealthState _healthState = ProviderHealthState.Unknown;
    private string _statusTooltip = "Provider status is waiting for synchronization.";
    private IReadOnlyList<ProviderConnection> _connections = Array.Empty<ProviderConnection>();

    public ProviderCardViewModel(ProviderDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _apiKeyState.PropertyChanged += ApiKeyState_OnPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProviderDefinition Definition { get; }

    public ProviderKind Kind => Definition.Kind;

    public string DisplayName => Definition.DisplayName;

    public string ShortCode => Kind switch
    {
        ProviderKind.Codex => "CX",
        ProviderKind.Kiro => "KI",
        ProviderKind.OpenRouter => "OR",
        ProviderKind.Ollama => "OL",
        ProviderKind.Kimchi => "KM",
        _ => "9R"
    };

    public WorkflowKind Workflow => Definition.Workflow;

    public string ApiKeyValue
    {
        get => _apiKeyState.Value;
        set => _apiKeyState.SetValue(value);
    }

    public bool IsApiKeyVisible => _apiKeyState.IsVisible;

    public bool HasSavedApiKey => _apiKeyState.HasSavedKey;

    public string ApiKeyToggleText => _apiKeyState.ToggleText;

    public string ApiKeyStatusText => _apiKeyState.StatusText;

    public ProviderHealthState HealthState => _healthState;

    public string StatusLabel => ProviderDisplayStatus.From(_healthState).Label;

    public string StatusTooltip => _statusTooltip;

    public bool IsHealthy => _healthState == ProviderHealthState.Healthy;

    public bool IsDisabled => _healthState == ProviderHealthState.Disabled;

    public bool HasError => _healthState == ProviderHealthState.Error;

    public bool IsMissing => _healthState == ProviderHealthState.Missing;

    public bool IsUnknown => _healthState == ProviderHealthState.Unknown;

    public IReadOnlyList<ProviderConnection> Connections => _connections;

    public IReadOnlyList<ProviderQuota> QuotaRows => _connections
        .SelectMany(connection => connection.QuotaRows)
        .ToArray();

    public void LoadSavedApiKey(string? value) => _apiKeyState.LoadSaved(value);

    public void MarkApiKeySaved() => _apiKeyState.MarkSaved();

    public void ToggleApiKeyVisibility() => _apiKeyState.ToggleVisibility();

    public void UpdateProviderStatus(ProfileProviderStatusViewModel? status)
    {
        _healthState = status?.HealthState ?? ProviderHealthState.Unknown;
        _statusTooltip = status?.ToolTip ?? "Select a profile to inspect provider status.";
        _connections = status?.Connections ?? Array.Empty<ProviderConnection>();
        OnPropertyChanged(nameof(HealthState));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusTooltip));
        OnPropertyChanged(nameof(IsHealthy));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsUnknown));
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(QuotaRows));
    }

    private void ApiKeyState_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ProviderApiKeyState.Value):
                OnPropertyChanged(nameof(ApiKeyValue));
                break;
            case nameof(ProviderApiKeyState.IsVisible):
                OnPropertyChanged(nameof(IsApiKeyVisible));
                break;
            case nameof(ProviderApiKeyState.HasSavedKey):
                OnPropertyChanged(nameof(HasSavedApiKey));
                break;
            case nameof(ProviderApiKeyState.ToggleText):
                OnPropertyChanged(nameof(ApiKeyToggleText));
                break;
            case nameof(ProviderApiKeyState.StatusText):
                OnPropertyChanged(nameof(ApiKeyStatusText));
                break;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
