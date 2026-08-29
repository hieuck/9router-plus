using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Chrome;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// Tracks login progress for a single profile in batch auto-login.
/// Phase 3 - Batch Progress UI
/// </summary>
public sealed class BatchLoginProgressRow : INotifyPropertyChanged
{
    private BatchLoginState _state = BatchLoginState.Waiting;
    private string _statusMessage = "Đang chờ";
    private TimeSpan _duration;

    public BatchLoginProgressRow(ChromeProfile profile)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ChromeProfile Profile { get; }

    public string ProfileName => Profile.Name;

    public BatchLoginState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusIcon));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationText));
        }
    }

    public string DurationText => _duration.TotalSeconds > 0
        ? $"{_duration.TotalSeconds:F1}s"
        : "";

    public string StatusIcon => _state switch
    {
        BatchLoginState.Waiting => "⏸",
        BatchLoginState.InProgress => "⏳",
        BatchLoginState.Success => "✅",
        BatchLoginState.Failed => "❌",
        BatchLoginState.Skipped => "⊘",
        _ => "?"
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// State of a profile during batch auto-login.
/// </summary>
public enum BatchLoginState
{
    Waiting,
    InProgress,
    Success,
    Failed,
    Skipped
}
