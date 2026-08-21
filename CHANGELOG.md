# Changelog

Mọi thay đổi đáng chú ý của RouterPlus được ghi tại đây. Các release được tạo từ tag SemVer dạng `vMAJOR.MINOR.PATCH`.

## Unreleased

### Added

- Windows GitHub Actions CI với test, build và self-contained artifact.
- Tag-triggered release với zip `win-x64` và SHA-256 checksum.
- User guide, privacy, troubleshooting, security policy và release checklist.
- GitHub issue/PR templates có yêu cầu loại bỏ dữ liệu nhạy cảm.
- Ảnh minh họa giao diện dùng dữ liệu demo đã sanitise.
- Help/About và self-update coordinator với checksum, manifest signature, Authenticode gate, staging và rollback helper.

### Changed

- README được tổ chức lại theo hướng người dùng cuối: tải bản release, quick start và tài liệu hỗ trợ.
- Project bổ sung MIT License ở file `LICENSE`.
- Release workflow publish cả `RouterPlus.Updater.exe` và fail-closed khi thiếu signing material.

### Fixed

- Release restore có runtime `win-x64` để self-contained publish có runtime pack đầy đủ.

### Security

- Không đưa email/profile/path thật hoặc API key vào ảnh minh họa phát hành.
- Preflight chỉ cho phép các ảnh asset đã được duyệt; screenshot mới phải được review riêng trước khi track.
- Path staging update từ chối reparse point; release build không tạo debug symbols và package fail-closed nếu `.pdb` hoặc memory dump xuất hiện.
- Updater health-check theo dõi đủ cửa sổ kiểm tra và chỉ dừng process mới khi khởi động thất bại hoặc bị hủy.
- Thông báo lỗi clipboard không còn hiển thị raw exception detail.

### Known limitations

- Build unsigned không tự cài update; self-update chỉ được bật trên executable đã được ký và package có manifest signature hợp lệ.
- Public release vẫn bị chặn cho đến khi repository public, kênh báo cáo private và clean-machine smoke test được xác nhận.
- Project chưa bật private security reporting channel.

## Release entry format

Khi tạo phiên bản mới, thêm một mục trước `Unreleased`:

```markdown
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
```
