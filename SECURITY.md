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

Không mở public issue cho lỗ hổng bảo mật. Trước khi public release, chủ dự án phải bật GitHub Security Advisories hoặc công bố một kênh liên hệ bảo mật private thực tế tại đây.

Repository hiện chưa công bố email/kênh private cụ thể, vì vậy không nên gửi secret thật qua issue hoặc commit để “báo lỗi”. Đây là hard blocker: không public repository và không tạo stable release cho đến khi kênh private được cấu hình và kiểm tra.

## Phạm vi bảo mật

- RouterPlus lưu API key bằng Windows DPAPI `CurrentUser`.
- User Windows khác không tự giải mã được encrypted secrets.
- Request kiểm tra update chỉ tới repository GitHub cố định và không gửi profile, email, API key, OAuth state, Chrome path hoặc machine identifier.
- Package update phải qua checksum, manifest signature dùng public key pin, Authenticode publisher verification và archive/path validation trước khi staging.
- Updater helper dùng backup/swap/health-check/rollback; settings và DPAPI secrets nằm ngoài thư mục package.
- Build unsigned không tự cài update. Chỉ release có chữ ký hợp lệ mới được bật automatic install; nếu thiếu chữ ký, app fail closed.
- Không chạy executable từ release mirror không xác định.

## Khi gửi bản vá

- Thêm test nếu thay đổi logic.
- Không thêm secret thật vào fixture hoặc screenshot.
- Ghi rõ tác động và cách kiểm tra.
- Chờ maintainer xác nhận trước khi công bố chi tiết lỗ hổng chưa được xử lý.
