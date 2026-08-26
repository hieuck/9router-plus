namespace RouterPlus.App.E2E;

public static class LiveActionPolicy
{
    private static readonly HashSet<string> AllowedMenuItems = new(StringComparer.Ordinal)
    {
        "Đăng nhập Google bằng Chrome",
        "Tự động đăng nhập Google",
        "Mở thư mục profile",
        "Sao chép tên profile"
    };

    public static bool IsAllowed(string menuItemText) =>
        AllowedMenuItems.Contains(menuItemText);
}
