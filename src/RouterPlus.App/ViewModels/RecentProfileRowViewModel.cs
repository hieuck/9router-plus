using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.ViewModels;

public sealed class RecentProfileRowViewModel : INotifyPropertyChanged
{
    private bool _isPinned;

    public RecentProfileRowViewModel(RecentProfile recent, ChromeProfile profile, int slotIndex)
    {
        Recent = recent ?? throw new ArgumentNullException(nameof(recent));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        SlotIndex = slotIndex;
        _isPinned = recent.IsPinned;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RecentProfile Recent { get; }

    public ChromeProfile Profile { get; }

    public int SlotIndex { get; }

    public string Name => Profile.Name;

    public string LaunchCountText => Recent.LaunchCount <= 1
        ? "1 lần"
        : $"{Recent.LaunchCount} lần";

    public string LastUsedText
    {
        get
        {
            var local = Recent.LastUsedUtc.ToLocalTime();
            var delta = DateTime.Now - local;
            if (delta.TotalSeconds < 60) return "vừa xong";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes} phút trước";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours} giờ trước";
            if (delta.TotalDays < 7) return $"{(int)delta.TotalDays} ngày trước";
            return local.ToString("dd/MM HH:mm");
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinGlyph));
        }
    }

    public string PinGlyph => _isPinned ? "📌" : "📍";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
