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

Không mở public issue cho lỗ hổng bảo mật. Trước khi phân phối rộng rãi, chủ dự án cần bật GitHub Security Advisories hoặc công bố một kênh liên hệ bảo mật private thực tế tại đây.

Repository hiện chưa công bố email/kênh private cụ thể, vì vậy không nên gửi secret thật qua issue hoặc commit để “báo lỗi”. Nếu phát hiện vấn đề, hãy báo cho maintainer qua kênh private đã được chủ dự án cấu hình trước khi public release.

## Phạm vi bảo mật

- RouterPlus lưu API key bằng Windows DPAPI `CurrentUser`.
- User Windows khác không tự giải mã được encrypted secrets.
- Bản phát hành hiện chưa code-sign; hãy kiểm tra nguồn tải và checksum.
- Không chạy executable từ release mirror không xác định.

## Khi gửi bản vá

- Thêm test nếu thay đổi logic.
- Không thêm secret thật vào fixture hoặc screenshot.
- Ghi rõ tác động và cách kiểm tra.
- Chờ maintainer xác nhận trước khi công bố chi tiết lỗ hổng chưa được xử lý.
