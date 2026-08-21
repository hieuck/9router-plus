# Privacy và dữ liệu cục bộ

RouterPlus là ứng dụng Windows local-first. Tài liệu này mô tả dữ liệu app đọc, lưu và gửi trong các luồng hiện tại.

## Dữ liệu app đọc trên máy

- Chrome executable và Chrome User Data do người dùng chọn hoặc app tự phát hiện.
- File Chrome `Local State` để đọc danh sách profile.
- Metadata/thư mục profile khi người dùng chọn thao tác mở, sao chép hoặc xóa.
- Settings của RouterPlus để khôi phục cấu hình và mapping profile.

App không nhập mật khẩu Google, không giải CAPTCHA và không tự chấp nhận điều khoản của bên thứ ba.

## Dữ liệu app lưu

RouterPlus dùng thư mục:

```text
%LOCALAPPDATA%\9RouterPlus\
```

### `settings.json`

Có thể chứa:

- Dashboard URL.
- Đường dẫn Chrome executable và Chrome User Data.
- Mapping profile do RouterPlus quản lý.
- Theme, font scale và vị trí cửa sổ.

### `secrets.json`

Có thể chứa API key theo key profile/provider. Giá trị được mã hóa bằng Windows DPAPI với `DataProtectionScope.CurrentUser` và entropy riêng của RouterPlus.

- User Windows khác không tự giải mã được file này.
- Đổi Windows account hoặc khôi phục file trên user khác có thể làm key không đọc được.
- Không coi `secrets.json` là file backup có thể chia sẻ.
- Không đưa file này vào Git, issue, chat hoặc archive công khai.

## Network và trình duyệt

- Request tới dashboard mặc định `http://localhost:20128` để đọc/sync connection.
- Các trang provider/OAuth mở trong Chrome profile người dùng đã chọn.
- API key được gửi tới 9Router khi người dùng nhấn thao tác thêm/sync tương ứng.
- RouterPlus không ghi API key vào log hoặc status text.
- Khi xử lý lỗi, app chỉ ghi loại lỗi an toàn; không ghi exception message, stack trace, path hoặc dữ liệu tùy ý từ server vào log/status.
- Lỗi provider trả về từ 9Router được tóm tắt trước khi hiển thị; mã lỗi và trạng thái kiểm tra được giữ lại khi có.
- App không tự động gửi telemetry hoặc upload Chrome profile lên server của RouterPlus.

Các provider bên thứ ba có thể thay đổi URL, OAuth flow hoặc chính sách riêng. Người dùng cần tự xác nhận trang đang mở đúng domain trước khi đăng nhập hoặc dán key.

## Người dùng cần tự bảo vệ

- Không chụp screenshot có email, Chrome path, cookie, OAuth code hoặc API key.
- Không gửi log nguyên bản nếu chưa kiểm tra secret/path.
- Không chạy bản zip từ nguồn không xác định.
- Kiểm tra checksum release trước khi chạy nếu file được tải qua mirror.
- Đóng Chrome trước thao tác xóa profile.

## Xóa dữ liệu

Xóa `%LOCALAPPDATA%\9RouterPlus` sẽ xóa settings, mapping và encrypted secrets của app. Thao tác này không xóa Chrome User Data. Muốn xóa Chrome profile, dùng context menu của profile trong app và đọc kỹ hộp xác nhận.
