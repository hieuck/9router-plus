using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed class ProfileProviderFilterOption : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _profileCount;
    private int _profileCountHas;
    private int _profileCountNotHas;
    private ProviderFilterState _filterState;

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

    public string DisplayNameWithCount
    {
        get
        {
            var count = _filterState switch
            {
                ProviderFilterState.Has => _profileCountHas,
                ProviderFilterState.NotHas => _profileCountNotHas,
                _ => _profileCountHas // Default Off state shows "Has" count
            };

            return count > 0 ? $"{DisplayName} ({count})" : DisplayName;
        }
    }

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

    public ProviderFilterState FilterState
    {
        get => _filterState;
        set
        {
            if (_filterState == value)
            {
                return;
            }

            _filterState = value;
            _isSelected = value != ProviderFilterState.Off;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(ButtonBackground));
            OnPropertyChanged(nameof(ButtonBorderBrush));
            OnPropertyChanged(nameof(ButtonForeground));
            OnPropertyChanged(nameof(DisplayNameWithCount));
        }
    }

    public System.Windows.Media.Brush ButtonBackground
    {
        get
        {
            var app = System.Windows.Application.Current;
            return _filterState switch
            {
                ProviderFilterState.Has => app.FindResource("AccentSoftBrush") as System.Windows.Media.Brush,
                ProviderFilterState.NotHas => new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#40F7C873")!),
                _ => app.FindResource("PanelLightBrush") as System.Windows.Media.Brush
            } ?? System.Windows.Media.Brushes.Transparent;
        }
    }

    public System.Windows.Media.Brush ButtonBorderBrush
    {
        get
        {
            var app = System.Windows.Application.Current;
            return _filterState switch
            {
                ProviderFilterState.Has => app.FindResource("AccentBrush") as System.Windows.Media.Brush,
                ProviderFilterState.NotHas => app.FindResource("WarningBrush") as System.Windows.Media.Brush,
                _ => app.FindResource("BorderBrush") as System.Windows.Media.Brush
            } ?? System.Windows.Media.Brushes.Gray;
        }
    }

    public System.Windows.Media.Brush ButtonForeground
    {
        get
        {
            var app = System.Windows.Application.Current;
            return _filterState switch
            {
                ProviderFilterState.Has => app.FindResource("AccentContentBrush") as System.Windows.Media.Brush,
                ProviderFilterState.NotHas => app.FindResource("WarningBrush") as System.Windows.Media.Brush,
                _ => app.FindResource("TextBrush") as System.Windows.Media.Brush
            } ?? System.Windows.Media.Brushes.Black;
        }
    }

    public void CycleFilterState()
    {
        FilterState = _filterState switch
        {
            ProviderFilterState.Off => ProviderFilterState.Has,
            ProviderFilterState.Has => ProviderFilterState.NotHas,
            ProviderFilterState.NotHas => ProviderFilterState.Off,
            _ => ProviderFilterState.Off
        };
    }

    public void SetProfileCount(int count)
    {
        ProfileCount = Math.Max(0, count);
    }

    public void SetProfileCounts(int hasCount, int notHasCount)
    {
        if (_profileCountHas == hasCount && _profileCountNotHas == notHasCount)
        {
            return;
        }

        _profileCountHas = Math.Max(0, hasCount);
        _profileCountNotHas = Math.Max(0, notHasCount);
        OnPropertyChanged(nameof(DisplayNameWithCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
