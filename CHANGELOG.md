# Changelog

Mọi thay đổi đáng chú ý của RouterPlus được ghi tại đây. Các release được tạo từ tag SemVer dạng `vMAJOR.MINOR.PATCH`.

## [personal-v1.0.1] - 2026-08-21

### Added

- Bộ lọc provider trong sidebar cho phép lọc profile theo kết nối provider.
- Toggle button với icon và tên ngắn cho từng provider (Codex, Kiro, OpenRouter, Ollama, Kimchi).

### Fixed

- Khắc phục hiện tượng cửa sổ nháy/thay đổi kích thước khi mở ứng dụng. Window placement được load và áp dụng trước khi window hiển thị.

## Unreleased

### Added

- Recent Profiles and Quick Launch: sidebar tracks mười Chrome profile dùng gần đây nhất (ghim trước, sau đó đến lần cuối) cùng số lần mở và thời gian dùng.
- RecentProfileRowViewModel với hint phím tắt (Ctrl+1..9, Ctrl+0) hiển thị ngay trong sidebar.
- Quick Launch palette (Ctrl+Shift+K) cho phép gõ tên profile để mở nhanh; hỗ trợ phím mũi tên, Esc đóng, Enter mở.
- Phím tắt mới: Ctrl+6..9, Ctrl+0 (slot 10), Ctrl+Shift+K (Quick Launch), Ctrl+Shift+R (xoá recent), F5 (làm mới), Escape đóng palette.
- Nút xoá danh sách recent ngay trong sidebar (Ctrl+Shift+R) kèm bộ đếm N/10.
- BooleanToVisibilityConverter để overlay Quick Launch điều khiển Visibility.
- Unit test mới: MainViewModelRecentProfilesTests (8 ca) bao phủ slot indexing, filter, wrap selection, clear recents, render row.
- Keyboard Shortcuts có thể bật/tắt trong Settings (mặc định tắt), cho phép gán lại từng phím tắt và khôi phục mặc định; phím tắt toàn cục chỉ được kích hoạt khi bật.

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
- Public release vẫn bị chặn cho đến khi repository public, kênh báo cáo private và clean-machine smoke test được xác nhận.
- Project chưa bật private security reporting channel.

## Release entry format

Khi tạo phiên bản mới, thêm một mục trước `Unreleased`:

``markdown
## [1.2.3] - YYYY-MM-DD

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
``
