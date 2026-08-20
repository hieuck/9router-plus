# 9Router Profile Tool

Công cụ Windows hỗ trợ mở Chrome profile và thêm connection vào 9Router.

## Tính năng hiện tại

- Tự tìm `chrome.exe` và Chrome User Data Directory.
- Đọc danh sách profile từ file `Local State`.
- Nháy đúp profile để mở dashboard 9Router bằng đúng profile đó.
- Bảng profile hiển thị trạng thái từng provider bằng badge `✓`/`—` và có nút đồng bộ với 9Router.
- Nút `Thêm` cho Codex, Kiro và Kimchi; OpenRouter/Ollama có ô API key ngay trong card provider.
- Nút mở nhanh:
  - OpenRouter API key: `https://openrouter.ai/settings/keys`
  - Ollama API key: `https://ollama.com/settings/keys`
  - Kimchi login: `https://app.kimchi.dev/`
- API-key workflow đặt tên connection theo profile và đặt priority ở cuối danh sách.
- API key được mã hóa bằng Windows DPAPI trong user profile hiện tại.
- Khi chọn lại profile, key OpenRouter/Ollama đã lưu được nạp lại vào card ở trạng thái ẩn; nút `Hiện key`/`Ẩn key` dùng để so sánh và ô nhập không bị xóa sau khi thêm.
- Codex, Kiro và Kimchi tự khởi động OAuth/device-code, tự chờ connection mới và đổi tên theo profile sau khi người dùng xác nhận.
- Settings được thu gọn mặc định; dùng nút `⚙ Cài đặt` để ẩn/hiện khi cần.

## Chạy từ source

Máy chưa có .NET SDK có thể dùng SDK cục bộ trong repo:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-dotnet.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Build script sẽ restore, test, build và publish vào `artifacts\publish`.

## Chạy ứng dụng

```powershell
& .\artifacts\publish\RouterPlus.exe
```

Ở lần đầu mở app:

1. Kiểm tra `Chrome executable` và `Chrome User Data`.
2. Nhấn `Lưu cài đặt`.
3. Chọn profile rồi nháy đúp để mở dashboard.
4. Với Codex/Kiro/Kimchi, nhấn `Thêm` và hoàn tất OAuth/device-code trong Chrome; tool tự chờ và đổi tên connection.
5. Với OpenRouter/Ollama, dán key trực tiếp vào card provider rồi bấm `Thêm vào 9Router`; profile hiện tại được dùng làm tên và priority tự thêm cuối danh sách.

## Bảo mật và giới hạn

- Tool không nhập mật khẩu Google, không giải CAPTCHA và không tự chấp nhận điều khoản bên thứ ba.
- API key không được ghi vào log hoặc status text.
- Secret vault dùng DPAPI `CurrentUser`; dữ liệu không tự giải mã được bởi Windows user khác.
- Dashboard base URL mặc định là `http://localhost:20128` và có thể chỉnh trong giao diện.
- Đối chiếu profile dùng tên connection trong 9Router; các connection cũ chưa đổi tên theo profile sẽ cần đổi tên để được nhận diện.

## Cấu trúc

- `src/RouterPlus.Core`: model, provider catalog, parser và quyết định priority.
- `src/RouterPlus.Infrastructure`: Chrome launcher, local 9Router API client, settings và DPAPI vault.
- `src/RouterPlus.App`: WPF UI.
- `tests/RouterPlus.Core.Tests`: test core.
