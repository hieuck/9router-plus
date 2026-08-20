using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;

namespace RouterPlus.App.ViewModels;

public sealed class ProviderCardViewModel : INotifyPropertyChanged
{
    private readonly ProviderApiKeyState _apiKeyState = new();

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

    public void LoadSavedApiKey(string? value) => _apiKeyState.LoadSaved(value);

    public void MarkApiKeySaved() => _apiKeyState.MarkSaved();

    public void ToggleApiKeyVisibility() => _apiKeyState.ToggleVisibility();

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
