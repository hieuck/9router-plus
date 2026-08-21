# Changelog

Mọi thay đổi đáng chú ý của RouterPlus được ghi tại đây. Các release được tạo từ tag SemVer dạng `vMAJOR.MINOR.PATCH`.

## Unreleased

### Added

- Windows GitHub Actions CI với test, build và self-contained artifact.
- Tag-triggered release với zip `win-x64` và SHA-256 checksum.
- User guide, privacy, troubleshooting, security policy và release checklist.
- GitHub issue/PR templates có yêu cầu loại bỏ dữ liệu nhạy cảm.
- Ảnh minh họa giao diện dùng dữ liệu demo đã sanitise.

### Changed

- README được tổ chức lại theo hướng người dùng cuối: tải bản release, quick start và tài liệu hỗ trợ.

### Fixed

- Release restore có runtime `win-x64` để self-contained publish có runtime pack đầy đủ.

### Security

- Không đưa email/profile/path thật hoặc API key vào ảnh minh họa phát hành.

### Known limitations

- Bản zip chưa có installer, auto-update hoặc code signing.
- Project chưa chọn LICENSE và chưa bật private security reporting channel.

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
