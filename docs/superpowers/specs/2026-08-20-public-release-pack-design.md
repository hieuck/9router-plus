# Public Release Pack Design

## Mục tiêu

Đưa RouterPlus từ trạng thái có CI/release workflow sang một gói phát hành có thể đưa cho người dùng cuối: không để lộ dữ liệu cá nhân trong ảnh/tài liệu, có hướng dẫn theo luồng sử dụng, có thông tin privacy/security và có checklist phát hành lặp lại được.

## Phạm vi

### Data hygiene và ảnh minh họa

- Xóa các ảnh raw `ui-*.png` đang lộ email/profile thật và đường dẫn máy cục bộ khỏi workspace.
- Không đưa ảnh raw vào Git hoặc release artifact.
- Tạo một ảnh minh họa đã sanitise trong `docs/assets/` bằng dữ liệu demo:
  - Email/profile: `demo.user@example.com`.
  - Đường dẫn Chrome: `C:\Program Files\Google\Chrome\Application\chrome.exe`.
  - Chrome User Data: `C:\Users\demo\AppData\Local\Google\Chrome\User Data`.
  - API key chỉ hiển thị dạng mask.
- README chỉ nhúng ảnh đã sanitise, không nhúng ảnh từ workspace cá nhân.

### Tài liệu người dùng

- Tạo `docs/user-guide.md` với các luồng:
  1. Tải và giải nén bản self-contained.
  2. Thiết lập Chrome executable và Chrome User Data lần đầu.
  3. Chọn profile, tìm profile và tạo profile mới.
  4. Mở dashboard 9Router bằng đúng Chrome profile.
  5. Đồng bộ và đọc trạng thái provider.
  6. Thêm Codex/Kiro/Kimchi qua OAuth hoặc device code.
  7. Thêm OpenRouter/Ollama bằng API key, kiểm tra trạng thái lưu cục bộ và DPAPI.
  8. Dùng context menu để đăng nhập Google, mở thư mục, sao chép tên hoặc xóa profile.
  9. Đổi theme/font và lưu settings.
  10. Gỡ ứng dụng và tùy chọn xóa dữ liệu cục bộ.
- Ghi rõ bản release self-contained không yêu cầu cài .NET 8 Runtime.
- Ghi rõ yêu cầu Windows, Chrome/Chromium và 9Router đang chạy tại dashboard URL.

### Privacy và troubleshooting

- Tạo `docs/privacy.md` mô tả:
  - settings tại `%LOCALAPPDATA%\9RouterPlus\settings.json`;
  - secrets tại `%LOCALAPPDATA%\9RouterPlus\secrets.json`;
  - API key được mã hóa Windows DPAPI `CurrentUser`;
  - app đọc Chrome `Local State` và profile metadata;
  - app gửi request tới 9Router local dashboard và mở các trang OAuth/provider do người dùng chọn;
  - app không ghi API key vào log/status text;
  - xóa profile là thao tác người dùng xác nhận và chỉ xóa thư mục profile được chọn.
- Tạo `docs/troubleshooting.md` cho các lỗi:
  - không tìm thấy Chrome;
  - chọn sai Chrome User Data;
  - 9Router chưa chạy hoặc sai dashboard URL;
  - OAuth/device code timeout hoặc callback lỗi;
  - API key rỗng/sai;
  - secrets không đọc được sau khi đổi Windows user;
  - profile/connection không match theo tên.

### Release metadata và hỗ trợ

- Tạo `CHANGELOG.md` với mục `Unreleased` và format entry cho release tag.
- Tạo `SECURITY.md` hướng dẫn không đăng secret/screenshot nhạy cảm và cách báo cáo lỗ hổng.
- Tạo `docs/release-checklist.md` gồm hygiene, test/build/publish, manual smoke test, checksum, release notes và post-release verification.
- Tạo GitHub issue templates cho bug và feature request; yêu cầu người dùng xóa secret/email/path trước khi gửi ảnh/log.
- Tạo pull request template nhắc kiểm tra tests, docs, screenshots và secret hygiene.
- Cập nhật README thành trang bắt đầu cho người dùng cuối, có link tải release/latest, quick start, link user guide, privacy, troubleshooting và security.

## Ngoài phạm vi

- Không tạo installer MSI/MSIX.
- Không code-sign executable.
- Không thêm auto-update.
- Không tự chọn LICENSE hoặc publisher identity.
- Không thay đổi hành vi ứng dụng hoặc thêm onboarding UI trong app.
- Không đưa ảnh raw hiện tại vào tài liệu mới.

## Tiêu chí nghiệm thu

- Không còn ảnh raw chứa email/profile/path thật trong workspace sau khi cleanup.
- Ảnh trong `docs/assets/` chỉ chứa dữ liệu demo và không có API key thật.
- README có quick start và đường dẫn đến các tài liệu người dùng.
- User guide mô tả được toàn bộ luồng chính từ tải app đến gỡ dữ liệu.
- Privacy và troubleshooting khớp với code hiện tại, đặc biệt đường dẫn LocalAppData và DPAPI.
- Changelog, security policy, issue templates, PR template và release checklist tồn tại.
- `git diff --check` sạch.
- Existing test suite, Release build và self-contained publish vẫn pass.
- Không thêm secret, email thật hoặc đường dẫn máy thật vào file mới.

## Rủi ro và giới hạn còn lại

- Zip self-contained vẫn chưa có installer hoặc signature nên Windows SmartScreen có thể cảnh báo.
- Không có LICENSE là thiếu sót pháp lý cần chủ dự án quyết định trước khi phân phối rộng.
- Hướng dẫn provider phụ thuộc vào trang OAuth/API của bên thứ ba; nội dung sẽ mô tả luồng app, không hứa giao diện bên thứ ba cố định.
