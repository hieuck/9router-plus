using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace RouterPlus.App.ViewModels;

public enum ToastType
{
    Success,
    Error,
    Info,
    Warning
}

public sealed class ToastNotification : INotifyPropertyChanged
{
    private bool _isVisible;
    private readonly DispatcherTimer _timer;

    public ToastNotification(string message, ToastType type, TimeSpan duration)
    {
        Message = message;
        Type = type;
        Duration = duration;

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (s, e) =>
        {
            IsVisible = false;
            _timer.Stop();
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Message { get; }
    public ToastType Type { get; }
    public TimeSpan Duration { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            OnPropertyChanged();

            if (value)
            {
                _timer.Start();
            }
        }
    }

    public void Show()
    {
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
        _timer.Stop();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
