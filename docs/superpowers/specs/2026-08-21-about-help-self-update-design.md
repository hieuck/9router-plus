# About, Help và Secure Self-Update Design

Ngày: 2026-08-21  
Trạng thái: Draft để maintainer review  
Phạm vi: RouterPlus desktop app, release channel Windows x64

## Mục tiêu

Bổ sung ba khả năng trước public release:

- About: hiển thị danh tính ứng dụng, phiên bản, license và liên kết chính thức.
- Help: mở tài liệu sử dụng, troubleshooting, privacy/security policy và trang báo lỗi.
- Check for updates: ứng dụng tự phát hiện bản release mới, tải gói cập nhật vào vùng tạm, xác minh trước khi cài và rollback nếu thay thế thất bại.

Self-update phải fail closed: nếu không xác minh được nguồn, checksum hoặc chữ ký thì không được cài.

## Bối cảnh hiện tại

- App là WPF net8.0-windows, phát hành self-contained win-x64 dưới dạng ZIP.
- Release workflow tạo RouterPlus-vX.Y.Z-win-x64.zip và file .sha256.
- App chưa có installer, updater helper, code signing hoặc menu About/Help.
- Version release được truyền vào build từ tag SemVer trong GitHub Actions.
- API key và profile data chỉ được lưu local; update flow không được gửi các dữ liệu này.

## Thiết kế được chọn

### 1. Điểm vào giao diện

Thêm một nút/menu Trợ giúp ở vùng header, cạnh Đồng bộ và Cài đặt. Menu gồm:

- Hướng dẫn sử dụng — mở tài liệu chính thức.
- Troubleshooting — mở hướng dẫn xử lý lỗi.
- Privacy & Security — mở chính sách privacy/security.
- Kiểm tra cập nhật — chạy update check thủ công.
- Giới thiệu — mở About dialog.

Các link public phải trỏ tới repository/release/docs chính thức; không hiển thị đường dẫn Chrome, email/profile hoặc secret local.

### 2. About dialog

About dialog hiển thị:

- Tên: 9Router Profile Tool.
- Version từ AssemblyInformationalVersion hoặc metadata release hiện hành.
- Kênh stable/prerelease nếu xác định được từ version.
- MIT License và liên kết LICENSE.
- Repository, release notes và security policy.
- Ghi chú rằng bản ZIP yêu cầu Windows x64 và có thể bị SmartScreen cảnh báo nếu chưa code-sign.

Dialog không đọc hoặc hiển thị profile, dashboard URL, Chrome path, API key, OAuth state, log hoặc dữ liệu từ 9Router.

### 3. Update check

Updater dùng repository/owner cố định: hieuck/9router-plus và chỉ chấp nhận metadata từ GitHub qua HTTPS.

Mặc định:

- Kiểm tra bản stable mới nhất khi app khởi động thành công, cooldown 24 giờ.
- Cho phép chạy lại bằng Kiểm tra cập nhật.
- Không coi prerelease là bản cập nhật nếu bản hiện tại là stable.
- Không gửi telemetry, profile name, email, Chrome path, dashboard URL, API key hoặc machine identifier.
- Chỉ gửi request đọc metadata; không cần GitHub token.

Nếu có bản mới, UI hiển thị version, release notes ngắn, kích thước gói và nút cài đặt. Update được tải nền nhưng chỉ restart sau khi người dùng xác nhận và không có workflow đang chạy.

### 4. Release metadata và asset

Release phải có các asset theo quy ước:

- RouterPlus-vX.Y.Z-win-x64.zip
- RouterPlus-vX.Y.Z-win-x64.zip.sha256
- Manifest có version, channel, asset name, SHA-256 và chữ ký xác minh được.

Updater chỉ chọn asset đúng runtime win-x64, kiểm tra version SemVer và từ chối asset name/URL bất thường.

SHA-256 là lớp kiểm tra toàn vẹn, không phải lớp xác thực nguồn độc lập. Public self-update chỉ được bật khi release artifact hoặc manifest có chữ ký số hợp lệ.

### 5. Xác minh bảo mật

Trước khi cài, updater phải kiểm tra:

1. Request chỉ dùng HTTPS và endpoint/host nằm trong allowlist GitHub cần thiết.
2. Response metadata đúng schema, version cao hơn version hiện tại và không phải downgrade.
3. Asset tải đủ, kích thước hợp lý và hash khớp file .sha256/manifest.
4. Executable chính và updater helper có chữ ký Authenticode hợp lệ, đúng publisher đã cấu hình.
5. Gói giải nén không chứa path traversal, symlink bất thường hoặc file ngoài staging directory.
6. App target đúng thư mục cài đặt hiện tại; không thay đổi %LOCALAPPDATA%\\9RouterPlus\\secrets.json hoặc settings local.

Nếu bất kỳ bước nào thất bại, updater không thay đổi bản đang chạy và hiển thị lỗi có thể xử lý.

### 6. Staging, swap và rollback

- Tải vào %LOCALAPPDATA%\\9RouterPlus\\updates\\<version>\\.
- Giải nén vào staging directory mới, không giải nén đè lên app đang chạy.
- Chạy helper updater riêng, chờ app đóng hoàn toàn rồi mới swap.
- Đổi thư mục hiện tại thành backup, chuyển staging vào vị trí live và khởi động bản mới.
- Nếu copy, start hoặc health check thất bại, phục hồi backup và báo lỗi.
- Dọn staging/backup cũ theo chính sách giới hạn dung lượng; không dọn secrets.
- Nếu thư mục cài đặt không có quyền ghi, không tự yêu cầu elevation mù quáng; hiển thị hướng dẫn hoặc fallback mở release page.

Updater helper phải có single-instance guard để không chạy song song hai lần.

### 7. Hành vi khi thiếu code signing

Trong giai đoạn hiện tại, executable đang unsigned. Do đó:

- Không bật silent install trong production build unsigned.
- Nếu release thiếu chữ ký hợp lệ, nút update hiển thị lý do bị vô hiệu hóa hoặc mở trang release để người dùng tự kiểm tra.
- Public stable release chỉ đánh dấu self-update là enabled sau khi code-signing và verification pipeline đạt.

## Phương án không làm

- Không dùng installer tự tải từ URL do release metadata trả về mà không allowlist.
- Không coi tên file, HTTP status hoặc SHA-256 sidecar không ký là đủ để xác thực nguồn.
- Không sửa trực tiếp file trong thư mục app khi chưa staging/backup.
- Không cập nhật 9Router server, Chrome hoặc provider connections.
- Không gửi telemetry hoặc secret để quyết định update.
- Không tự update prerelease vào stable channel.

## Thay đổi dự kiến

### App UI

- Header menu/button trong MainWindow.xaml.
- About dialog riêng, không phụ thuộc vào dữ liệu profile.
- Status text/log chỉ ghi version và kết quả update, không ghi URL có query secret hay nội dung response thô.

### Core/infrastructure

- Value object cho release metadata và update state.
- Service tách biệt để fetch metadata, validate asset, stage package và invoke helper.
- Version comparison dùng SemVer, không so sánh chuỗi tự do.
- Path validation dùng canonical absolute paths trong workspace cài đặt.

### Release workflow

- Thêm code signing cho RouterPlus.exe và updater helper.
- Tạo manifest có chữ ký và asset checksum.
- Đính kèm helper/manifest vào release ZIP hoặc phát hành theo layout updater đã định nghĩa.
- Thêm release preflight để fail nếu asset/manifest/signature thiếu hoặc repository private.

## Kiểm thử và nghiệm thu

### Unit tests

- Parse và validate release metadata.
- So sánh version stable/prerelease/downgrade.
- Chọn đúng asset win-x64.
- Parse checksum và từ chối hash sai.
- Từ chối host/URL không allowlist.
- Từ chối path traversal trong ZIP.
- Không ghi đè secrets/settings.
- Rollback khi swap hoặc health check thất bại.
- Update request không chứa profile/secret fields.

### Integration/manual tests

- Fake GitHub response cho latest, no-update, malformed metadata, hash mismatch và signature failure.
- Update từ bản cũ sang bản mới trên Windows user không có quyền admin.
- Update khi app đang có workflow, khi app đóng đột ngột và khi target directory bị khóa.
- Restart thành công và rollback thành công.
- Kiểm tra clean Windows account từ ZIP release.
- Kiểm tra About/Help không hiển thị dữ liệu cá nhân.
- Kiểm tra release asset có checksum, manifest và signature đúng.

## Tiêu chí chấp thuận

Chỉ gọi self-update là sẵn sàng public khi:

- Tất cả unit/integration tests pass.
- Release artifact và updater helper có chữ ký hợp lệ.
- Hash, manifest và signature được kiểm tra trước swap.
- Rollback đã được test trên Windows sạch.
- Không có secret/PII trong source, history, docs, screenshots hoặc package.
- SECURITY.md, release checklist và README mô tả đúng behavior thực tế.
- Không có update request nào chứa dữ liệu profile hoặc secret.

## Rủi ro còn lại

Code signing certificate, quyền ghi vào thư mục cài đặt và thay đổi chính sách GitHub Releases là các phụ thuộc ngoài code. Nếu chưa giải quyết, updater phải fail closed và không được tuyên bố public-ready.
