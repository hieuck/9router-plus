# 9Router Profile Tool

[![CI](https://github.com/hieuck/9router-plus/actions/workflows/ci.yml/badge.svg)](https://github.com/hieuck/9router-plus/actions/workflows/ci.yml)

Công cụ Windows hỗ trợ mở Chrome profile và thêm connection vào 9Router.

## Tải bản phát hành

Mở [Latest Releases](https://github.com/hieuck/9router-plus/releases/latest) và tải asset `RouterPlus-vX.Y.Z-win-x64.zip`. Đây là bản self-contained cho Windows x64, không yêu cầu cài .NET 8 Runtime.

1. Tải zip và file `.sha256` cạnh nó.
2. Kiểm tra checksum trước khi chạy.
3. Giải nén vào thư mục riêng.
4. Mở `RouterPlus.exe`.

Bản zip chưa có installer. Menu `Trợ giúp` có About, hướng dẫn và kiểm tra cập nhật; tự cài đặt chỉ được bật khi executable hiện tại, manifest và package đều có chữ ký hợp lệ. Build unsigned hiện tại sẽ hiển thị tự cập nhật bị vô hiệu hóa và Windows có thể cảnh báo SmartScreen.

## Bắt đầu nhanh

1. Mở `⚙ Cài đặt` và chọn `chrome.exe` cùng thư mục Chrome User Data.
2. Kiểm tra dashboard URL, mặc định `http://localhost:20128`, rồi nhấn `Lưu cài đặt`.
3. Chọn Chrome profile cần dùng.
4. Nhấn `Đồng bộ` hoặc nháy đúp profile để mở dashboard.
5. Thêm provider bằng OAuth/device code hoặc API key theo [User Guide](docs/user-guide.md).

## Tài liệu cho người dùng

- [User Guide](docs/user-guide.md) — cài đặt, profile, provider, settings và gỡ dữ liệu.
- [Privacy](docs/privacy.md) — dữ liệu local, DPAPI và network behavior.
- [Troubleshooting](docs/troubleshooting.md) — các lỗi thường gặp và cách xử lý.
- [Security Policy](SECURITY.md) — cách gửi báo lỗi an toàn.
- [Changelog](CHANGELOG.md) — thay đổi theo release.
- [License](LICENSE) — MIT License.

![RouterPlus workspace với dữ liệu demo](docs/assets/9router-profile-workspace.png)

## Tính năng hiện tại

- Tự tìm `chrome.exe` và Chrome User Data Directory.
- Đọc danh sách profile từ file `Local State`.
- Khi tìm kiếm không trùng tên, có thể thêm profile mới; tool tự cấp thư mục `Profile <tên>` theo tên profile và lưu mapping riêng trong settings.
- Nhấp chuột phải trên profile để:
  - đăng nhập Google bằng đúng Chrome profile đang chọn;
  - mở trực tiếp thư mục profile;
  - sao chép tên profile;
  - xóa profile sau khi xác nhận; chỉ thư mục profile được xóa, thư mục `User Data` vẫn được giữ lại.
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
- Menu `Trợ giúp` có About, hướng dẫn bảo mật, kiểm tra update và cài update có xác nhận; request update không gửi profile, email, API key, OAuth state hoặc machine identifier.

## Chạy từ source

Máy chưa có .NET SDK có thể dùng SDK cục bộ trong repo:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap-dotnet.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

Build script sẽ restore, test, build và publish vào `artifacts\publish`.

## CI và phát hành

- Pull request vào `master` và push lên `master` tự động chạy restore, test, build và publish.
- CI tạo artifact self-contained `win-x64` để tải từ trang Actions; artifact CI được giữ trong 14 ngày.
- Dev test local có thể chạy:

  ```powershell
  .\scripts\sign-local-release.ps1 -Version 0.0.0-dev
  ```

  Script tạo self-signed certificate tạm thời, ký hai executable, tạo ZIP và checksum trong `artifacts\local`. Chứng thư này chỉ dùng trên máy dev; không dùng cho public release.
- Personal release trên GitHub Actions: mở `Actions` → `Personal Release` → `Run workflow`, nhập version như `0.1.0`. Workflow tạo release tag `personal-v...` với ZIP unsigned và `.sha256`; bản này là download-only và self-update vẫn bị vô hiệu hóa.
- Production release dùng tag SemVer, ví dụ:

  ```powershell
  git tag v1.0.0
  git push origin v1.0.0
  ```

- Tag `v1.0.0-rc.1` tạo prerelease; tag `v1.0.0` tạo release ổn định.
- Project sử dụng MIT License. Trước khi tag stable, maintainer vẫn phải chuyển repository sang public và cấu hình một kênh báo cáo bảo mật private thực tế; workflow production sẽ chặn repository private.
- Production release tự động đính kèm ZIP `win-x64`, `.sha256` và manifest đã ký; workflow dừng nếu thiếu public repository, kênh security private, certificate hoặc manifest signing key.
- Bản phát hành self-contained không yêu cầu cài .NET 8 Runtime.
- Executable unsigned có thể chạy sau khi người dùng xác nhận cảnh báo Windows, nhưng self-update fail-closed cho đến khi executable, manifest và package được ký hợp lệ. Xem thêm [release checklist](docs/release-checklist.md).
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
