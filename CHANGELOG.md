# Changelog

Mọi thay đổi đáng chú ý của RouterPlus được ghi tại đây. Các release được tạo từ tag SemVer dạng `vMAJOR.MINOR.PATCH`.

## Unreleased

## [v0.2.0] - 2026-08-29

### Added

- **Observability System**: ObservabilityHub thay thế DebugConsole với structured JSON logging, privacy scrubbing tự động, và session-based log files.
- **Diagnostics Panel UI**: Real-time log viewer với filter theo level, search, view structured context, export logs, và session selector (menu `Trợ giúp` → `Diagnostics Panel`).
- **Google Auto-Login**: Encrypted vault system với DPAPI cho Google credentials, tự động đăng nhập Google trong Chrome profile từ context menu.
- **Credentials Manager**: Dialog UI để quản lý Google accounts với vault integration, inline editing, và remove functionality.
- **Batch Operations**: Multi-select mode cho profiles, bulk actions bar, batch auto-login với progress UI và orchestration.
- **Select All/Deselect All**: Checkbox cho batch operations và bulk credential management.
- **Provider Quota Tracking**: Real quota data từ 9Router API và SQLite database với auto-disable khi hết quota, quota reset re-enable flow.
- **Provider Filters**: Three-state provider profile filter (All/Only/None) với provider count badges trên mỗi tag.
- **OAuth Automation**: Tự động hóa OAuth flow cho Codex, GitHub, OpenRouter; device code flow cho Kiro/AWS Builder ID.
- **Direct Login Automation**: Tự động login cho Codex, GitHub, OpenRouter với TOTP support.
- **Chrome Selection Dialog**: Auto-detect Chrome installations với validation và installation picker.
- **First-run Setup Wizard**: Kiểm tra Node.js, npm, và 9Router riêng biệt; cài Node.js LTS bằng WinGet hoặc mở trang download; cài 9Router bằng npm; khởi chạy 9Router.
- Chrome auto-detection trong wizard reuses installation-selection dialog từ Settings.
- FlaUI-based E2E test harness cho developer testing với isolated process harness.
- Debug diagnostics và comprehensive logging cho troubleshooting.

### Changed

- **Style System Refactor**: Centralize tất cả WPF styles vào structure mới trong `Styles/` directory, integrate vào `App.xaml`.
- **Vault Architecture**: `GoogleLoginVault` → `GoogleAccountVault`, provider connection vault với DPAPI encryption.
- **OAuth Flow**: Base classes `GoogleOAuthFlowAutomation` và `DirectLoginAutomation` cho code reuse giữa các providers.
- **Window Placement**: Load và apply trước khi window hiển thị để tránh nháy/resize.
- **Repository Cleanup**: `.gitignore` restructured, `.gitattributes` added cho consistent line endings.

### Fixed

- **Wizard Issues**: Keeps application alive khi skipped; long content trong vertical scroll viewer; single scrollable page không cần step navigation; npm/9Router detection qua `.cmd` shims.
- **Codex OAuth**: Fix consent button not clicked; remove redundant Google login step gây profile conflict.
- **Chrome Automation**: Use real mouse events cho confirmidentifier Continue button; improve account picker click reliability.
- **Google Login**: Fix credentials disappearing bug; relax validation; auto-unlock remembered vault.
- **OAuth Flow**: Remove duplicate browser launch gây callback conflicts; fix timeout issues.
- **Profile Management**: Close browser và clean Local State khi xóa Chrome profile.
- **Quota Tracking**: Database-first priority cho token expiration; prevent usage override.
- **UI Cleanup**: Remove unused multi-select toggle và Select All button; fix button alignment.
- **Test Coverage**: Fix ThemeTemplateTests, GoogleLoginStateMachineTests, AutoLoginOrchestrator tests; achieve 100% functional coverage (310/310 tests).
- **CI**: Exclude E2E tests từ release workflow.

### Security

- Observability logs tự động redact sensitive data: email, password, TOTP codes, API keys trước khi ghi file.
- Vault encryption với Windows DPAPI `CurrentUser` scope và entropy riêng của RouterPlus.
- Reset Vault button cho forgotten passwords với confirmation dialog.
- Password/TOTP visibility toggles thay vì hiển thị plain text.

### Documentation

- Added `observability-system.md` thay thế outdated `debug-logging.md`.
- Diagnostics Panel sections trong `user-guide.md` và `troubleshooting.md`.
- Archive outdated session/phase reports từ Aug 2026 vào `docs/archive/2026-08/`.
- Style system refactor documentation và completion status.
- Comprehensive audit report updates với style system improvements.

### Known Limitations

- E2E tests require manual FlaUI harness setup và không chạy trong CI pipeline.
- Batch auto-login chỉ hỗ trợ Google-based OAuth providers (Codex, GitHub, OpenRouter).
- Quota tracking yêu cầu 9Router version có `/api/usage/` endpoint.

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
