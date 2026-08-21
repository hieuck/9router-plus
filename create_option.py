import os
path = r'E:\GitHub\9router-plus\src\RouterPlus.App\ViewModels\ProfileProviderFilterOption.cs'
os.makedirs(os.path.dirname(path), exist_ok=True)
content = '''using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed class ProfileProviderFilterOption : INotifyPropertyChanged
{
    private bool _isSelected;

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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
'''
with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
print('Written:', path)
