# Changelog

Mọi thay đổi đáng chú ý của RouterPlus được ghi tại đây. Các release được tạo từ tag SemVer dạng `vMAJOR.MINOR.PATCH`.

## Unreleased

### Added

- First-run setup wizard now checks Node.js, npm, and 9Router separately.
- The wizard can install Node.js LTS with WinGet, open the official Node.js download page when WinGet is unavailable, install 9Router with npm, and launch 9Router.
- Chrome auto-detection in the wizard reuses the installation-selection dialog from Settings.

### Fixed

- The wizard keeps the application alive when it is skipped, so the main window opens instead of the process exiting.
- Long wizard content is placed in a vertical scroll viewer.
- First-run setup is now a single scrollable page; users configure 9Router and Chrome without step navigation.
- Windows setup detection now runs npm and 9Router through their `.cmd` shims, so installed Node.js/npm tools are detected correctly.

## [v0.1.0] - 2026-08-22

### Added

- Recent Profiles and Quick Launch: sidebar tracks mười Chrome profile dùng gần đây nhất (ghim trước, sau đó đến lần cuối) cùng số lần mở và thời gian dùng.
- RecentProfileRowViewModel hiển thị các profile dùng gần đây ngay trong sidebar.
- Quick Launch palette cho phép gõ tên profile để mở nhanh; hỗ trợ phím mũi tên, Esc đóng, Enter mở.
- Nút xoá danh sách recent ngay trong sidebar kèm bộ đếm N/10.
- BooleanToVisibilityConverter để overlay Quick Launch điều khiển Visibility.
- Unit test mới: MainViewModelRecentProfilesTests (8 ca) bao phủ slot indexing, filter, wrap selection, clear recents, render row.

- Windows GitHub Actions CI với test, build và self-contained artifact.
- Tag-triggered release với zip `win-x64` và SHA-256 checksum.
- User guide, privacy, troubleshooting, security policy và release checklist.
- GitHub issue/PR templates có yêu cầu loại bỏ dữ liệu nhạy cảm.
- Ảnh minh họa giao diện dùng dữ liệu demo đã sanitise.
- Help/About và self-update coordinator với GitHub release metadata, checksum SHA-256, HTTPS host validation, staging và rollback helper.

### Changed

- README được tổ chức lại theo hướng người dùng cuối: tải bản release, quick start và tài liệu hỗ trợ.
- Project bổ sung MIT License ở file `LICENSE`.
- Release workflow publish cả `RouterPlus.Updater.exe`; build và release không yêu cầu signing material.

### Fixed

- Release restore có runtime `win-x64` để self-contained publish có runtime pack đầy đủ.

### Security

- Không đưa email/profile/path thật hoặc API key vào ảnh minh họa phát hành.
- Preflight chỉ cho phép các ảnh asset đã được duyệt; screenshot mới phải được review riêng trước khi track.
- Path staging update từ chối reparse point; release build không tạo debug symbols và package fail-closed nếu `.pdb` hoặc memory dump xuất hiện.
- Updater health-check theo dõi đủ cửa sổ kiểm tra và chỉ dừng process mới khi khởi động thất bại hoặc bị hủy.
- Thông báo lỗi clipboard không còn hiển thị raw exception detail.

### Known limitations

- Build unsigned có thể kiểm tra/cài update sau khi người dùng xác nhận; updater chỉ chọn stable tag `v...`, checksum SHA-256 hợp lệ và archive layout an toàn.
- Stable release đầu tiên dành cho giai đoạn thử nghiệm public; build và release không yêu cầu chữ ký.
- Private vulnerability reporting được bật qua GitHub Security Advisories.

## [personal-v1.0.1] - 2026-08-21

### Added

- Bộ lọc provider trong sidebar cho phép lọc profile theo kết nối provider.
- Toggle button với icon và tên ngắn cho từng provider (Codex, Kiro, OpenRouter, Ollama, Kimchi).

### Fixed

- Khắc phục hiện tượng cửa sổ nháy/thay đổi kích thước khi mở ứng dụng. Window placement được load và áp dụng trước khi window hiển thị.

## Release entry format

Khi tạo phiên bản mới, thêm một mục trước `Unreleased`:

```markdown
## [0.2.0] - YYYY-MM-DD

### Added
- ...

### Changed
- ...

### Fixed
- ...

### Security
- ...

### Known limitations
- ...
```
