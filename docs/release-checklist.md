# Release Checklist

Dùng checklist này trước và sau mỗi tag release. Nếu một hard gate chưa có bằng chứng, không public repository, không push stable tag và không tuyên bố release sẵn sàng.

## Hard gates trước public release

- [ ] Repository đã chuyển sang public và kiểm tra visibility bằng GitHub API.
- [x] Project đã thêm `LICENSE` MIT; chủ dự án đã xác nhận quyền chọn license.
- [ ] GitHub Security Advisories hoặc một kênh báo cáo bảo mật private thực tế đã được cấu hình và thử nghiệm.
- [ ] Repository không chứa email, profile thật, Chrome path thật, token, key, OAuth state hoặc ảnh debug cá nhân.

## Trước khi tag

- [ ] Không còn ảnh raw `ui-*.png` chứa dữ liệu cá nhân trong workspace.
- [ ] Ảnh trong `docs/assets/` chỉ dùng dữ liệu demo.
- [ ] README có link tải release, About/Help và behavior self-update qua GitHub + checksum.
- [ ] `docs/user-guide.md`, `docs/privacy.md`, `docs/troubleshooting.md` và `SECURITY.md` khớp với code hiện tại.
- [ ] `CHANGELOG.md` có entry cho thay đổi chuẩn bị phát hành.
- [ ] `SECURITY.md` có kênh private thật, không phải placeholder.
- [ ] Chạy restore với runtime `win-x64`.
- [ ] Test Release pass.
- [ ] Build Release pass với 0 warning/error.
- [ ] Self-contained publish tạo được cả `RouterPlus.exe` và `RouterPlus.Updater.exe`.
- [ ] Vulnerability scan không có package vulnerable chưa được chấp nhận.
- [ ] Preflight public repository và security channel pass.

## Dev và personal release

- [ ] Dev build local chạy `scripts\\build.ps1`; build unsigned không cần certificate.
- [ ] Không upload `RouterPlus-Dev-Test.cer` hoặc ZIP dev như stable production release.
- [ ] Personal GitHub workflow được chạy thủ công từ `Actions` → `Personal Release` với tag `personal-v...`.
- [ ] Personal release ghi rõ unsigned/checksum; không được chứa secret hoặc dữ liệu cá nhân.
- [ ] Production release vẫn chỉ đi qua workflow tag `v...` và các hard gate bên dưới.
## Tạo package

- [ ] Tag đúng format `vMAJOR.MINOR.PATCH` hoặc prerelease.
- [ ] Release workflow chạy đúng commit/tag.
- [ ] File zip có tên `RouterPlus-vX.Y.Z-win-x64.zip`.
- [ ] File `.sha256` khớp đúng zip.
- [ ] Zip giải nén được và có `RouterPlus.exe`, `RouterPlus.Updater.exe`.
- [ ] Không có `.pdb`, `artifacts`, `work`, raw screenshots hoặc secrets trong archive.
- [ ] Release notes generated không chứa dữ liệu nhạy cảm.

## Smoke test sau khi phát hành

- [ ] Tải zip từ GitHub Release bằng Windows user sạch.
- [ ] Kiểm tra checksum của archive.
- [ ] Giải nén vào thư mục mới và mở `RouterPlus.exe`.
- [ ] Mở About/Help; xác nhận không có profile, email, path hoặc secret.
- [ ] Chạy check update không có update và kiểm tra request/log đã sanitized.
- [ ] Từ một bản cũ, stage package tải từ GitHub; xác nhận user confirmation trước restart.
- [ ] Xác nhận settings và DPAPI secrets còn nguyên sau update.
- [ ] Ép health-check thất bại; xác nhận live bản cũ được rollback.
- [ ] Kiểm tra target bị khóa, updater chạy song song và parent process chưa thoát.
- [ ] Thử ít nhất một OAuth/device-code flow và API key flow bằng dữ liệu test.
- [ ] Không chụp hoặc commit screenshot/log có dữ liệu từ máy smoke test.

## Sau release

- [ ] Xác nhận GitHub Release ở trạng thái đúng stable/prerelease.
- [ ] Xác nhận assets zip và checksum tải được.
- [ ] Cập nhật changelog nếu có hotfix.
- [ ] Theo dõi rollback/update failure không chứa response body hoặc secret.
