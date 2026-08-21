# Troubleshooting

Làm theo từng mục từ trên xuống dưới. Khi cần báo lỗi, hãy xóa email, đường dẫn máy, API key, OAuth code và cookie khỏi screenshot/log.

## Chrome không được tìm thấy

**Triệu chứng:** app báo chưa tìm thấy Chrome hoặc danh sách profile trống.

1. Mở `⚙ Cài đặt`.
2. Chọn đúng file `chrome.exe` tại `Chrome executable`.
3. Chọn đúng thư mục User Data tại `Chrome User Data`.
4. Không chọn thư mục `Profile 1` thay cho thư mục User Data gốc.
5. Nhấn `Lưu cài đặt`, sau đó refresh profile.
6. Đóng Chrome hoàn toàn nếu file profile đang bị khóa.

## Chọn sai Chrome User Data

**Triệu chứng:** app mở được Chrome nhưng profile không đúng hoặc không có profile mong muốn.

- Mở Chrome profile cần dùng và kiểm tra thư mục User Data đang được cài đặt.
- Nếu dùng Chromium/Cent Browser hoặc bản portable, chọn đúng User Data của bản đó.
- Đảm bảo executable và User Data thuộc cùng một cài đặt browser.
- Không gửi đường dẫn thật trong issue công khai; dùng `C:\Users\demo\...` khi chụp minh họa.

## 9Router không kết nối

**Triệu chứng:** sync lỗi, dashboard không mở hoặc trạng thái connection không tải được.

1. Xác nhận 9Router đang chạy.
2. Mở dashboard URL trong browser để kiểm tra local service.
3. Kiểm tra URL trong settings, mặc định `http://localhost:20128`.
4. Nếu đổi port, cập nhật URL rồi nhấn `Lưu cài đặt`.
5. Tắt VPN/firewall rule chỉ khi bạn hiểu tác động; RouterPlus không tự sửa cấu hình mạng.

## Provider không match với profile

**Triệu chứng:** connection tồn tại nhưng badge profile vẫn trống.

- Connection match theo tên profile.
- Đổi tên connection trong 9Router theo đúng profile.
- Nhấn `Đồng bộ` lại.
- Kiểm tra đang chọn đúng Chrome profile trước khi thêm provider.

## OAuth hoặc device code timeout

**Triệu chứng:** Codex/Kiro/Kimchi không tạo connection mới.

1. Đóng tab OAuth/device code cũ.
2. Kiểm tra Chrome profile đang mở có đăng nhập đúng tài khoản.
3. Nhấn `Thêm` lại một lần.
4. Hoàn tất xác nhận trong thời gian provider cho phép.
5. Không copy OAuth code vào issue hoặc chat.
6. Nếu vẫn lỗi, ghi lại provider, version release và bước cuối cùng đã làm; không gửi token.

## API key bị từ chối

**Triệu chứng:** OpenRouter/Ollama báo lỗi khi thêm key.

- Kiểm tra key không có khoảng trắng đầu/cuối.
- Dùng đúng key của provider tương ứng.
- Kiểm tra provider account còn hoạt động.
- Dán lại key trong card của profile đang chọn.
- Không paste key vào status, log hoặc bug report.

## Không đọc được key sau khi đổi Windows user

**Triệu chứng:** key trước đó hiển thị `Chưa có key` sau khi chuyển account.

Đây là hành vi dự kiến của DPAPI `CurrentUser`. Tạo key mới trên Windows user hiện tại; không cố sửa `secrets.json` bằng text editor và không gửi file đó cho người khác.

## Profile bị khóa hoặc xóa không thành công

- Đóng toàn bộ cửa sổ Chrome dùng profile đó.
- Chờ Chrome background process thoát.
- Thử lại từ context menu.
- Chỉ thư mục profile được chọn bị xóa; không chọn nhầm User Data gốc.

## Cần gửi báo lỗi

Dùng [bug report template](../.github/ISSUE_TEMPLATE/bug_report.md). Trước khi gửi:

- Xóa email/profile thật.
- Thay path bằng `C:\Users\demo\...`.
- Xóa API key, OAuth code, cookie và token.
- Ghi version/tag release, Windows version và bước tái hiện.
