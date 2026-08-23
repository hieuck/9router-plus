using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed class ProfileProviderFilterOption : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _profileCount;

    public ProfileProviderFilterOption(ProviderKind? kind, string displayName, string glyph, string tooltip)
    {
        Kind = kind;
        DisplayName = displayName;
        Glyph = glyph;
        Tooltip = tooltip;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProviderKind? Kind { get; }

    public string DisplayName { get; }

    public string Glyph { get; }

    public string Tooltip { get; }

    public int ProfileCount
    {
        get => _profileCount;
        private set
        {
            if (_profileCount == value)
            {
                return;
            }

            _profileCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayNameWithCount));
        }
    }

    public string DisplayNameWithCount => ProfileCount > 0
        ? $"{DisplayName} ({ProfileCount})"
        : DisplayName;

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
        }
    }

    public void SetProfileCount(int count)
    {
        ProfileCount = Math.Max(0, count);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
