using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// Row model for one configurable keyboard shortcut in the Settings UI.
/// </summary>
public sealed class ShortcutBindingRowViewModel : INotifyPropertyChanged
{
    private string _gesture;
    private string? _errorMessage;

    public ShortcutBindingRowViewModel(
        KeyboardShortcutEntry entry,
        string gesture,
        string? errorMessage = null)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _gesture = gesture ?? entry.DefaultGesture;
        _errorMessage = errorMessage;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KeyboardShortcutEntry Entry { get; }

    public string ActionId => Entry.ActionId;

    public string DisplayName => Entry.DisplayName;

    public string Gesture
    {
        get => _gesture;
        set
        {
            if (string.Equals(_gesture, value, StringComparison.Ordinal)) return;
            _gesture = value ?? string.Empty;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (string.Equals(_errorMessage, value, StringComparison.Ordinal)) return;
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Holds the catalog of configurable shortcuts, exposed rows, and the
/// saved user overrides. Persistence targeting happens in MainViewModel.
/// </summary>
public sealed class ShortcutBindingsViewModel
{
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);

    public ShortcutBindingsViewModel()
    {
        Rows = new ObservableCollection<ShortcutBindingRowViewModel>(
            KeyboardShortcutEntry.All.Select(entry =>
                new ShortcutBindingRowViewModel(entry, entry.DefaultGesture)));
    }

    public ObservableCollection<ShortcutBindingRowViewModel> Rows { get; }

    public IReadOnlyDictionary<string, string> Overrides => _overrides;

    public void Load(IReadOnlyDictionary<string, string>? overrides)
    {
        Rows.Clear();
        _overrides.Clear();
        if (overrides is not null)
        {
            foreach (var (actionId, gesture) in overrides)
            {
                _overrides[actionId] = gesture;
            }
        }

        foreach (var entry in KeyboardShortcutEntry.All)
        {
            Rows.Add(new ShortcutBindingRowViewModel(
                entry,
                _overrides.TryGetValue(entry.ActionId, out var gesture) ? gesture : entry.DefaultGesture));
        }
    }

    public void ResetAll()
    {
        _overrides.Clear();
        Rows.Clear();
        foreach (var entry in KeyboardShortcutEntry.All)
        {
            Rows.Add(new ShortcutBindingRowViewModel(entry, entry.DefaultGesture));
        }
    }

    public string? ValidateAndApply(string actionId, string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return "Phím tắt không được để trống.";
        }

        if (!KeyboardShortcutService.TryParse(gesture, out _))
        {
            return "Chuỗi phím tắt không hợp lệ. Ví dụ: Ctrl+Alt+1, F5, Ctrl+Shift+K.";
        }

        var normalized = gesture.Trim();
        var duplicate = Rows.FirstOrDefault(row =>
            row.ActionId != actionId &&
            string.Equals(row.Gesture, normalized, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            return $"Phím tắt đã được dùng cho \"{duplicate.DisplayName}\".";
        }

        var row = Rows.FirstOrDefault(r => r.ActionId == actionId);
        if (row is not null)
        {
            row.Gesture = normalized;
            row.ErrorMessage = null;
        }

        _overrides[actionId] = normalized;
        return null;
    }

    public void ClearOverride(string actionId)
    {
        var entry = KeyboardShortcutEntry.All.FirstOrDefault(e => e.ActionId == actionId);
        if (entry is null) return;

        _overrides.Remove(actionId);
        var row = Rows.FirstOrDefault(r => r.ActionId == actionId);
        if (row is not null)
        {
            row.Gesture = entry.DefaultGesture;
            row.ErrorMessage = null;
        }
    }

    public Dictionary<string, string>? BuildSettingsDictionary() =>
        _overrides.Count > 0 ? new(_overrides, StringComparer.Ordinal) : null;
}
