# RouterPlus User Guide

RouterPlus giúp mở Chrome profile đúng ngữ cảnh và quản lý connection/provider trong 9Router.

![Ảnh minh họa giao diện với dữ liệu demo](assets/9router-profile-workspace.png)

> Ảnh trên chỉ dùng dữ liệu demo. Không dùng email, đường dẫn Chrome hoặc API key thật trong issue, screenshot hay log.

## 1. Yêu cầu

- Windows x64.
- Chrome hoặc Chromium đã cài trên máy.
- 9Router đang chạy nếu bạn muốn đồng bộ hoặc mở dashboard.
- Bản phát hành self-contained không yêu cầu cài .NET 8 Runtime.

## 2. Tải và chạy lần đầu

1. Mở [GitHub Releases](https://github.com/hieuck/9router-plus/releases) và chọn release stable có tag `vX.Y.Z`.
2. Tải file `RouterPlus-vX.Y.Z-win-x64.zip`.
3. Tùy chọn: tải file `.sha256` cạnh zip và kiểm tra checksum bằng PowerShell:

   ```powershell
   $archive = '.\RouterPlus-vX.Y.Z-win-x64.zip'
   $expected = (Get-Content '.\RouterPlus-vX.Y.Z-win-x64.zip.sha256').Split()[0]
   $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
   if ($actual -ne $expected) { throw 'Checksum không khớp.' }
   'Checksum hợp lệ.'
   ```

4. Giải nén vào thư mục bạn muốn giữ ứng dụng.
5. Mở `RouterPlus.exe`.
6. Nếu Windows SmartScreen cảnh báo, chỉ tiếp tục khi bạn đã tải đúng asset từ release chính thức và checksum hợp lệ. Build local có thể chưa được code-sign bởi CA; đây là hành vi dự kiến trong giai đoạn phát triển.

## 3. Thiết lập lần đầu

Khi chạy lần đầu, RouterPlus mở wizard thiết lập. Wizard không yêu cầu máy phải có sẵn Node.js, npm hoặc 9Router.

### 3.1. Cài Node.js, npm và 9Router

Tất cả nội dung nằm trên một trang cuộn; không cần chuyển trang hoặc nhấn `Tiếp tục`.

1. Nhấn `Kiểm tra Node.js / npm / 9Router` để xem trạng thái từng thành phần.
2. Nếu thiếu Node.js:
   - Khi có WinGet, nhấn `Cài Node.js LTS`; wizard chạy `winget install --id OpenJS.NodeJS.LTS --exact`.
   - Khi không có WinGet, wizard mở trang tải Node.js chính thức. Cài Node.js LTS xong, quay lại wizard và nhấn kiểm tra lại.
3. Khi npm đã sẵn sàng nhưng chưa có 9Router, nhấn `Cài 9Router bằng npm`. Lệnh được dùng là `npm install --global 9router`.
4. Nếu 9Router đã cài nhưng chưa chạy, nhấn `Khởi chạy 9Router`.
5. Sau khi cài và khởi chạy 9Router, nhập hoặc kiểm tra `Dashboard URL` (mặc định `http://localhost:20128`).
6. Nhấn `Kiểm tra 9Router`. Khi dashboard phản hồi thành công, hoàn thiện phần Chrome ngay bên dưới.

Wizard chỉ cài Node.js LTS và 9Router. Nó không tự cài Python, Visual Studio Build Tools hoặc Chocolatey. Các công cụ build native chỉ cần nếu npm báo package yêu cầu biên dịch C/C++.

Nếu không muốn thiết lập ngay, nhấn `Bỏ qua`; RouterPlus vẫn mở để bạn cấu hình thủ công sau. Có thể chạy lại wizard trong `Trợ giúp` → `Chạy lại thiết lập ban đầu...`.

### 3.2. Cấu hình Chrome

Phần cấu hình Chrome nằm trên cùng một trang với phần 9Router; không cần chuyển sang trang hoặc nhấn `Tiếp tục`:

1. Ở `Chrome executable`, chọn đúng file `chrome.exe`.
2. Ở `Chrome User Data`, chọn thư mục User Data của Chrome/Chromium, không chọn thư mục `Profile 1` riêng lẻ.
3. Nếu có nhiều bản Chrome/Chromium, nhấn `⚡ Tự động phát hiện Chrome`, chọn installation hợp lệ từ danh sách, rồi bấm `Chọn`.
4. Kiểm tra các dấu ✓ của executable và User Data.
5. Khi 9Router đã được kiểm tra thành công và Chrome hợp lệ, nhấn `Lưu cài đặt và bắt đầu` ở cuối wizard.

Nếu Chrome được cài ở vị trí chuẩn, app có thể tự tìm executable và User Data. Đường dẫn có dấu ✓ khi file/thư mục tồn tại; nếu không hợp lệ, sửa lại trước khi lưu. Bạn vẫn có thể chọn lại thủ công trong settings.

## 4. Quản lý Chrome profile

### Chọn, tìm và lọc profile

- Chọn profile ở thanh bên để làm profile hiện hành.
- Dùng ô tìm kiếm theo tên hoặc thư mục.
- Dùng `Chưa có provider` để chỉ hiện profile chưa có connection.
- Dùng các tag provider để lọc profile có connection tương ứng; số trên tag cho biết có bao nhiêu profile khớp.
- Có thể bật nhiều bộ lọc provider cùng lúc. Nhấn lại tag đang chọn để bỏ lọc.

### Recent Profiles và Quick Launch

- Sidebar ghi nhớ tối đa 10 profile dùng gần đây; profile được ghim sẽ nằm trước các profile còn lại.
- Nhấn nút ghim bên cạnh profile để ghim/bỏ ghim.
- Nháy đúp hoặc bấm vào profile để mở dashboard bằng profile đó.
- Dùng nút `✕` để xóa danh sách Recent; thao tác này không xóa Chrome profile.

### Tạo profile do RouterPlus quản lý

1. Nhập tên chưa có vào ô tìm kiếm.
2. Chọn thao tác thêm profile mới.
3. Xác nhận tên profile.
4. App tạo mapping riêng và cấp thư mục profile tương ứng.
5. Chọn profile mới để tiếp tục thêm provider.

### Mở dashboard

- Nháy đúp profile hoặc nhấn `Mở dashboard`.
- App mở dashboard bằng đúng Chrome profile đã chọn.
- Nếu dashboard không mở, kiểm tra 9Router đang chạy và URL trong settings.

### Context menu

Nhấp chuột phải profile để:

- đăng nhập Google bằng đúng Chrome profile;
- mở thư mục profile;
- sao chép tên profile;
- xóa profile sau khi xác nhận.

Xóa profile chỉ xóa thư mục profile được chọn; không xóa toàn bộ Chrome User Data. Hãy đóng Chrome trước khi xóa để tránh file đang được khóa.

## 5. Đồng bộ, kiểm tra và quản lý connection

1. Nhấn `Đồng bộ` hoặc `F5` để đọc connection từ 9Router.
2. Mỗi profile hiển thị badge provider tương ứng; thẻ provider hiển thị workflow, trạng thái và quota.
3. Connection được match theo tên profile.
4. Nút `↻` trên thẻ provider kiểm tra lại connection của provider đó cho profile đang chọn.
5. Nút `×` xóa connection của provider đó sau khi xác nhận; thao tác này không xóa Chrome profile.
6. Nếu connection cũ chưa được đổi tên theo profile, app có thể không nhận diện đúng. Đổi tên connection trong 9Router rồi đồng bộ lại.

### Quota Tracker và tự động bật lại

- `Quota Tracker` hiển thị mức sử dụng, phần trăm và thời điểm reset của từng quota nếu 9Router cung cấp dữ liệu.
- App tự làm mới quota theo chu kỳ phù hợp với trạng thái connection; khi cửa sổ app bị thu nhỏ, polling được tạm dừng và tiếp tục khi mở lại.
- Khi Kiro hết quota, app có thể tự tắt connection để tránh tiếp tục gửi request lỗi.
- Sau khi quota đã reset, app hiển thị gợi ý `Bật lại`. Chỉ nhấn nút này sau khi bạn xác nhận muốn bật connection; app sẽ đồng bộ lại trạng thái.
- Nếu chưa có dữ liệu quota, thẻ hiển thị thông báo tương ứng thay vì suy đoán mức sử dụng.

## 6. Thêm provider OAuth/device code

### Codex

1. Chọn profile.
2. Nhấn `Thêm` ở card Codex.
3. App mở luồng đăng nhập trong Chrome profile hiện hành.
4. Hoàn tất đăng nhập và xác nhận trên trang provider.
5. Quay lại app; app chờ connection mới và đổi tên theo profile.

### Kiro

1. Chọn profile.
2. Nhấn `Thêm` ở card Kiro.
3. Mở trang device code theo hướng dẫn trong trình duyệt.
4. Nhập/xác nhận code trên trang provider.
5. Chờ app nhận connection mới và đồng bộ tên profile.

### Kimchi

1. Chọn profile.
2. Nhấn `Thêm` ở card Kimchi.
3. Hoàn tất OAuth trong Chrome.
4. Quay lại app để app nhận và đổi tên connection.

Nếu OAuth/device code timeout, không tạo lại liên tục; xem [Troubleshooting](troubleshooting.md), kiểm tra Chrome profile và thử lại sau khi đóng luồng cũ.

## 7. Thêm OpenRouter/Ollama bằng API key

1. Chọn profile trước khi nhập key.
2. Dán key vào card provider tương ứng hoặc dùng nút `Paste`.
3. Kiểm tra trạng thái hiển thị `Đã lưu cục bộ`/`DPAPI`.
4. Nhấn `Thêm vào 9Router`.
5. App đặt tên connection theo profile, đặt priority ở cuối danh sách và tự chạy Test Connection để đồng bộ trạng thái.
6. Nếu key hợp lệ, card chuyển sang `Online`; nếu kiểm tra thất bại, card hiển thị lỗi thay vì báo Online giả.
7. Ô key không tự xóa để bạn có thể đối chiếu; key vẫn được mask. Chỉ nhấn `Hiện key` khi cần kiểm tra trên máy riêng.

Không gửi API key vào issue, chat, screenshot, clipboard log hoặc file backup không mã hóa. Chi tiết lưu trữ xem [Privacy](privacy.md).

## 8. About, Help và tự cập nhật

- Mở menu `Trợ giúp` để xem `Giới thiệu`, hướng dẫn, chính sách bảo mật hoặc trang release cố định của dự án. About chỉ hiển thị tên app, version, MIT License và link công khai; không bind vào profile, log, settings hay secret.
- Chọn `Kiểm tra cập nhật` để đọc release stable từ repository cố định. Request này không gửi profile, email, API key, OAuth state, Chrome path hoặc machine identifier.
- Nếu có bản mới, app chỉ cho cài sau khi metadata release stable `v...`, host HTTPS được allow, checksum SHA-256 và archive layout được xác minh. Người dùng phải xác nhận trước khi app đóng.
- Build unsigned vẫn hỗ trợ self-update; chỉ release stable có tag `v...` và checksum hợp lệ mới được chọn làm bản cập nhật, không có fallback tải executable từ URL tùy ý.
- Updater riêng đổi live directory sang backup, đưa staging vào vị trí live và rollback nếu bản mới không khởi động được. Settings và DPAPI secrets nằm ngoài package update.
## 9. Theme, font và settings

- Nhấn `⚙ Cài đặt` để mở/thu gọn settings.
- Chọn theme sáng/tối.
- Chọn font scale phù hợp màn hình.
- Nhấn `Lưu cài đặt` sau khi đổi đường dẫn hoặc dashboard URL.
- Vị trí cửa sổ được lưu khi đóng app và áp dụng lại lần sau nếu còn hợp lệ.

## 10. Dữ liệu cục bộ và chuyển máy

RouterPlus lưu:

- `%LOCALAPPDATA%\9RouterPlus\settings.json`: đường dẫn, dashboard URL, mapping profile và tùy chọn giao diện.
- `%LOCALAPPDATA%\9RouterPlus\secrets.json`: API key đã mã hóa bằng DPAPI cho Windows user hiện tại.

Khi chuyển sang Windows user khác, `secrets.json` không tự giải mã được. Nếu cần chuyển máy, hãy tạo key mới trên user đích thay vì copy secrets như file văn bản thông thường.

## 11. Gỡ ứng dụng

1. Đóng RouterPlus và Chrome.
2. Xóa thư mục đã giải nén của RouterPlus.
3. Nếu muốn giữ settings/key cho lần cài sau, giữ lại `%LOCALAPPDATA%\9RouterPlus`.
4. Nếu muốn xóa toàn bộ dữ liệu local, xóa thư mục `%LOCALAPPDATA%\9RouterPlus`; thao tác này sẽ xóa mapping, cài đặt và key đã lưu.
5. Chrome User Data và profile Chrome không bị xóa khi gỡ RouterPlus.

## Tài liệu liên quan

- [Privacy và dữ liệu](privacy.md)
- [Troubleshooting](troubleshooting.md)
- [Security policy](../SECURITY.md)
- [Changelog](../CHANGELOG.md)
