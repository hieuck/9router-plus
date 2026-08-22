# Security Policy

## Không đưa secret vào public issue

Không đăng các dữ liệu sau trong issue, pull request, screenshot, log hoặc changelog:

- API key hoặc access token.
- OAuth/device code, cookie hoặc session data.
- Email/profile thật nếu không cần thiết.
- Chrome User Data path, username Windows hoặc thông tin máy cá nhân.
- `%LOCALAPPDATA%\9RouterPlus\secrets.json`.

Dùng dữ liệu mẫu như `demo.user@example.com`, `C:\Users\demo\...` và `masked-demo-key` khi mô tả lỗi.

## Báo cáo lỗ hổng

Không mở public issue cho lỗ hổng bảo mật. Kênh báo cáo private chính thức là [GitHub Security Advisories](https://github.com/hieuck/9router-plus/security/advisories/new).

Private vulnerability reporting đã được bật cho repository. Không gửi secret thật qua issue hoặc commit để “báo lỗi”; dùng kênh private ở trên.

## Phạm vi bảo mật

- RouterPlus lưu API key bằng Windows DPAPI `CurrentUser`.
- User Windows khác không tự giải mã được encrypted secrets.
- Request kiểm tra update chỉ tới repository GitHub cố định và không gửi profile, email, API key, OAuth state, Chrome path hoặc machine identifier.
- Package update lấy metadata và asset từ repository GitHub cố định qua HTTPS; chỉ staging sau khi checksum SHA-256 khớp, host được allow và archive/path validation pass. Không dùng manifest signature hoặc Authenticode gate.
- Updater helper dùng backup/swap/health-check/rollback; settings và DPAPI secrets nằm ngoài thư mục package.
- Build unsigned vẫn có thể kiểm tra/cài update sau khi người dùng xác nhận. SHA-256 kiểm tra toàn vẹn package nhưng không thay thế publisher signature; SmartScreen warning vẫn có thể xuất hiện.
- Không chạy executable từ release mirror không xác định.

## Khi gửi bản vá

- Thêm test nếu thay đổi logic.
- Không thêm secret thật vào fixture hoặc screenshot.
- Ghi rõ tác động và cách kiểm tra.
- Chờ maintainer xác nhận trước khi công bố chi tiết lỗ hổng chưa được xử lý.
