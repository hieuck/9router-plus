using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RouterPlus.Core.Security;

public sealed class ProviderApiKeyState : INotifyPropertyChanged
{
    private string _value = string.Empty;
    private string _savedValue = string.Empty;
    private bool _isVisible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value => _value;

    public bool IsVisible => _isVisible;

    public bool HasSavedKey => !string.IsNullOrWhiteSpace(_value)
                               && string.Equals(_value, _savedValue, StringComparison.Ordinal);

    public string ToggleText => _isVisible ? "Ẩn key" : "Hiện key";

    public string StatusText => string.IsNullOrWhiteSpace(_value)
        ? "Chưa có key"
        : HasSavedKey
            ? "Đã lưu cục bộ"
            : "Key chưa lưu";

    public void LoadSaved(string? value)
    {
        _value = value ?? string.Empty;
        _savedValue = _value;
        _isVisible = false;
        RaiseAllChanged();
    }

    public void SetValue(string? value)
    {
        var nextValue = value ?? string.Empty;
        if (string.Equals(_value, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        _value = nextValue;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(HasSavedKey));
        OnPropertyChanged(nameof(StatusText));
    }

    public void MarkSaved()
    {
        if (string.Equals(_savedValue, _value, StringComparison.Ordinal))
        {
            return;
        }

        _savedValue = _value;
        OnPropertyChanged(nameof(HasSavedKey));
        OnPropertyChanged(nameof(StatusText));
    }

    public void ToggleVisibility()
    {
        _isVisible = !_isVisible;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(ToggleText));
    }

    private void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(HasSavedKey));
        OnPropertyChanged(nameof(ToggleText));
        OnPropertyChanged(nameof(StatusText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
