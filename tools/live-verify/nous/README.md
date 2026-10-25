# Công cụ kiểm thử trực tiếp Nous OAuth

Thư mục này chứa các script PowerShell và Node.js để tự động hóa và quan sát quy trình OAuth của Nous (qua Google) trên 9Router cục bộ.

## Mục đích

- **start-browser.ps1**: Đăng nhập 9Router, yêu cầu device code, lưu JSON, in user code/URL, khởi chạy Chrome với profile thật (có CDP hoặc không).
- **poll-device-code.ps1**: Đăng nhập, poll `/api/oauth/nous/poll` cho đến khi được authorized, sau đó liệt kê connections `nous`.
- **watch-connections.ps1**: Baseline connections `nous` hiện có, poll `/api/providers` theo chu kỳ; log connection mới và `RENAMED_TO_PROFILE` khi tên khớp với profile mong đợi.
- **pw-cdp.js**: Driver Playwright kết nối CDP để thao tác trang (click, fill, snapshot...).

## Điều kiện tiên quyết

1. 9Router cục bộ đang chạy (Debug) tại `http://127.0.0.1:20128` (hoặc BaseUrl tuỳ chỉnh).
2. Mật khẩu dashboard.
3. Chrome profile đã đăng nhập Google + có session Nous (profile **phải đóng** trước khi chạy script CDP).
4. Node.js ≥ 18 + Playwright (`npm i playwright`).
5. PowerShell 5.1+ / PowerShell 7+.
6. **Lưu ý Kasada**: Nous portal có kiểm tra Kasada. Chạy `Mode Normal` (không CDP) để tránh bị chặn; chỉ dùng `Mode Cdp` khi cần thao tác tự động qua `pw-cdp.js`.

## Cài đặt Playwright

```powershell
npm i playwright
npx playwright install chromium
```

## Các lệnh (thay placeholder bằng giá trị thực)

### 1. Khởi chạy Chrome + lấy device code (CDP)
```powershell
.\start-browser.ps1 `
  -BaseUrl "http://127.0.0.1:20128" `
  -Password "<DASHBOARD_PASSWORD>" `
  -ChromePath "C:\Program Files\Google\Chrome\Application\chrome.exe" `
  -UserDataDir "C:\Users\<USER>\AppData\Local\Google\Chrome\User Data" `
  -ProfileDirectory "Profile 1" `
  -Mode Cdp
```
Kết quả: in `USER_CODE`, `URL`, `DEVICE_FILE`, `CDP_READY`.

### 2. Khởi chạy Chrome bình thường (không CDP, Kasada-friendly)
```powershell
.\start-browser.ps1 `
  -Password "<DASHBOARD_PASSWORD>" `
  -ChromePath "C:\Program Files\Google\Chrome\Application\chrome.exe" `
  -UserDataDir "C:\Users\<USER>\AppData\Local\Google\Chrome\User Data" `
  -ProfileDirectory "Profile 1" `
  -Mode Normal
```

### 3. Poll device code cho đến khi authorized
```powershell
.\poll-device-code.ps1 -Password "<DASHBOARD_PASSWORD>"
```
Mặc định đọc device file tại `$env:TEMP\routerplus-nous-live\nous-device.json`, log tại `$env:TEMP\routerplus-nous-live\nous-poll.log`.

### 4. Theo dõi connection mới + detect rename
```powershell
.\watch-connections.ps1 -Password "<DASHBOARD_PASSWORD>" -ProfileName "user@example.com"
```
Mặc định theo dõi 12 phút, poll 3 giây/lần. Thoát mã 0 khi thấy `RENAMED_TO_PROFILE`.

### 5. Thao tác trang qua Playwright CDP
```powershell
# Liệt kê tab
$env:CDP="http://127.0.0.1:9333"; node pw-cdp.js pages

# Snapshot trang Nous/Google hiện tại
node pw-cdp.js snapshot

# Click theo text
node pw-cdp.js click-text "Connect"

# Click account picker theo data-identifier
node pw-cdp.js click-account "user@example.com"

# Click link chứa chuỗi
node pw-cdp.js click-href "provider=googleoauth"

# Fill input
node pw-cdp.js fill "input[type=email]" "email@gmail.com"

# Đưa tab lên trước
node pw-cdp.js front

# Nhấn phím
node pw-cdp.js press "Enter"

# Điều hướng
node pw-cdp.js goto "https://portal.nousresearch.com"

# Eval JS tuỳ ý
node pw-cdp.js eval "document.title"
```

## Mapping với one-click flow của App

| Bước App | Script tương ứng |
|---|---|
| User bấm "Connect Nous" | `start-browser.ps1 -Mode Cdp` (mở Chrome profile thật, CDP ready) |
| App gọi `/api/oauth/nous/device-code` | `start-browser.ps1` (đã làm bên trong) |
| User quét mã/nhấn Allow trên Chrome | Thao tác thủ công hoặc `pw-cdp.js click-text/click-account` |
| App poll `/api/oauth/nous/poll` | `poll-device-code.ps1` |
| 9Router tạo connection, App rename | `watch-connections.ps1` (phát hiện NEW_NOUS + RENAMED_TO_PROFILE) |

## Cảnh báo bảo mật

- **KHÔNG commit mật khẩu thật, tên profile thật, email, đường dẫn cá nhân**.
- Mọi giá trị nhạy cảm phải truyền qua tham số `-Password`, `-ProfileDirectory`, `-UserDataDir` hoặc biến môi trường.
- File JSON device code và log nằm trong `$env:TEMP\routerplus-nous-live\` (bị xóa khi reboot hoặc có thể xóa thủ công).
- Xem lại `.gitignore` để chắc chắn thư mục `tools/live-verify/nous/` không bị commit nhầm file chứa secret.