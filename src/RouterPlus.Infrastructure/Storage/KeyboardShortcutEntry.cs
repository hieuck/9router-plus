namespace RouterPlus.Infrastructure.Storage;

/// <summary>
/// Catalog entry for a single configurable keyboard shortcut. The gesture is
/// stored as a display string such as "Ctrl+Alt+1" so settings stay readable.
/// </summary>
public sealed record KeyboardShortcutEntry(
    string ActionId,
    string DisplayName,
    string DefaultGesture)
{
    public static IReadOnlyList<KeyboardShortcutEntry> All { get; } =
    [
        new("SaveSettings", "Lưu cài đặt", "Ctrl+S"),
        new("OpenProviderCodex", "Mở Codex cho profile đang chọn", "Ctrl+1"),
        new("OpenProviderKiro", "Mở Kiro cho profile đang chọn", "Ctrl+2"),
        new("OpenProviderOpenRouter", "Mở OpenRouter cho profile đang chọn", "Ctrl+3"),
        new("OpenProviderOllama", "Mở Ollama cho profile đang chọn", "Ctrl+4"),
        new("OpenProviderKimchi", "Mở Kimchi cho profile đang chọn", "Ctrl+5"),
        new("OpenQuickLaunch", "Mở Quick Launch", "Ctrl+Shift+K"),
        new("ClearRecent", "Xoá danh sách Recent", "Ctrl+Shift+R"),
        new("RefreshProfiles", "Làm mới danh sách profile", "F5")
    ];
}
