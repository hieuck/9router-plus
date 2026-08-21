# Release Checklist

Dùng checklist này trước và sau mỗi tag release.

## Hard gates trước public release

Không push tag stable nếu còn bất kỳ mục nào sau đây:

- [ ] Repository đã chuyển sang public.
- [ ] Project đã thêm `LICENSE` do chủ dự án lựa chọn.
- [ ] GitHub Security Advisories hoặc một kênh báo cáo bảo mật private thực tế đã được cấu hình.
- [ ] Chủ dự án đã xác nhận package unsigned/SmartScreen limitation và có kế hoạch code signing nếu cần.

## Trước khi tag

- [ ] Không còn ảnh raw `ui-*.png` chứa dữ liệu cá nhân trong workspace.
- [ ] Ảnh trong `docs/assets/` chỉ dùng dữ liệu demo.
- [ ] README có link tải release và quick start.
- [ ] `docs/user-guide.md`, `docs/privacy.md` và `docs/troubleshooting.md` khớp với code hiện tại.
- [ ] `CHANGELOG.md` có entry cho thay đổi chuẩn bị phát hành.
- [ ] `SECURITY.md` không hứa một kênh private chưa cấu hình.
- [ ] Chạy restore với runtime `win-x64`.
- [ ] Test Release pass.
- [ ] Build Release pass với 0 warning/error.
- [ ] Self-contained publish tạo được `RouterPlus.exe`.
- [ ] Không có secret/email/path thật trong docs, templates hoặc screenshot.

## Tạo package

- [ ] Tag đúng format `vMAJOR.MINOR.PATCH` hoặc prerelease.
- [ ] Release workflow chạy đúng commit/tag.
- [ ] File zip có tên `RouterPlus-vX.Y.Z-win-x64.zip`.
- [ ] File `.sha256` khớp đúng zip.
- [ ] Zip giải nén được và có `RouterPlus.exe`.
- [ ] Release notes generated không chứa dữ liệu nhạy cảm.
- [ ] Ghi rõ package chưa code-sign nếu chưa có certificate.

## Smoke test sau khi phát hành

- [ ] Tải zip từ GitHub Release bằng máy/Windows user test riêng.
- [ ] Kiểm tra checksum.
- [ ] Giải nén vào thư mục mới và mở `RouterPlus.exe`.
- [ ] Thiết lập Chrome executable/User Data.
- [ ] Mở dashboard và kiểm tra sync.
- [ ] Thử ít nhất một OAuth/device-code flow.
- [ ] Thử API key flow bằng key test hợp lệ, không dùng key production trong screenshot/log.
- [ ] Kiểm tra README và các link tài liệu.
- [ ] Ghi lại known limitation hoặc rollback nếu phát hiện lỗi.

## Sau release

- [ ] Xác nhận GitHub Release ở trạng thái đúng stable/prerelease.
- [ ] Xác nhận assets và checksum tải được.
- [ ] Cập nhật changelog nếu có hotfix.
- [ ] Không commit screenshot/log có dữ liệu từ máy smoke test.
