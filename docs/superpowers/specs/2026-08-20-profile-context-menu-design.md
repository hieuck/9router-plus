# Profile Context Menu

## Goal

Add a right-click context menu for each Chrome profile row with profile-scoped Google login, direct profile-folder access, name copying, and confirmed profile-directory deletion.

## Behavior

- Right-clicking a profile row selects that row before the menu opens.
- `Đăng nhập Google bằng Chrome` opens `https://accounts.google.com/` through the existing Chrome launcher using the selected profile.
- `Mở thư mục profile` opens the selected profile directory (for example, `Default` or an app-managed `Profile <tên>`) in Explorer.
- `Sao chép tên profile` copies the selected display name to the clipboard.
- `Xóa profile…` asks for confirmation and deletes only the selected profile directory, never the Chrome User Data root.
- After deletion, the managed-profile mapping is removed, the profile list is refreshed, and another available profile is selected.

## Safety

- Deletion validates that the target is an immediate child of the configured Chrome User Data directory.
- A missing profile directory is treated as already deleted, but its managed mapping is still removed.
- If Chrome or another process locks the directory, the existing error/status path reports the failure and settings are not saved.
- Chrome `Local State` and the 9Router database remain read-only.

## Architecture

- `ChromeProfileDeleter` owns filesystem validation and recursive deletion.
- `MainViewModel` owns Google-login launching, managed-profile persistence, selection, refresh, and status reporting.
- `MainWindow` owns the WPF context-menu event wiring, Explorer launch, clipboard access, and destructive confirmation dialog, matching the existing UI patterns.
