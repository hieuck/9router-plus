# GitHub Actions CI và Release-Ready Design

## Mục tiêu

Thiết lập quy trình GitHub Actions cho RouterPlus để mọi pull request và thay đổi trên nhánh `master` được kiểm tra tự động, đồng thời hỗ trợ phát hành phiên bản ổn định bằng cách push tag SemVer.

## Phạm vi

### CI workflow

- Tạo `.github/workflows/ci.yml`.
- Chạy khi push lên `master`.
- Chạy khi pull request nhắm vào `master`.
- Dùng Windows runner vì solution chứa ứng dụng WPF.
- Cài .NET SDK theo `global.json`.
- Restore solution with runtime `win-x64` so the self-contained runtime packs are available.
- Chạy toàn bộ test ở cấu hình `Release`.
- Build solution ở cấu hình `Release`.
- Publish ứng dụng self-contained cho runtime `win-x64`.
- Nén thư mục publish thành zip và upload làm GitHub Actions artifact.
- Upload file kết quả test `.trx` khi có để hỗ trợ chẩn đoán lỗi.
- Artifact CI có thời hạn lưu 14 ngày.

### Release workflow

- Tạo `.github/workflows/release.yml`.
- Chạy khi push tag bắt đầu bằng `v`.
- Chấp nhận tag dạng `vMAJOR.MINOR.PATCH` và prerelease dạng `vMAJOR.MINOR.PATCH-IDENTIFIER`.
- Từ chối tag không đúng định dạng trước khi đóng gói.
- Chạy lại restore, test và build trước khi phát hành.
- Dùng version lấy từ tag để ghi metadata assembly/informational version.
- Publish self-contained cho `win-x64`, không yêu cầu người dùng cài .NET 8 Runtime.
- Tạo artifact phát hành:
  - `RouterPlus-vX.Y.Z-win-x64.zip`.
  - `RouterPlus-vX.Y.Z-win-x64.zip.sha256`.
- Tạo GitHub Release tự động với generated release notes.
- Đính kèm zip và checksum vào GitHub Release.
- Dùng quyền tối thiểu `contents: write` và `GITHUB_TOKEN` mặc định; không thêm secret mới.

### Tài liệu

- Thêm CI badge vào `README.md`.
- Thêm mục hướng dẫn tạo tag để phát hành.
- Ghi rõ artifact release là bản self-contained `win-x64`.
- Không tự chọn hoặc thêm `LICENSE` vì đây là quyết định pháp lý của dự án.
- Ghi nhận code signing là công việc tiếp theo; workflow hiện tại tạo artifact unsigned vì repo chưa có certificate/secrets.

## Ngoài phạm vi

- Không thay đổi hành vi ứng dụng.
- Không thêm GitHub Actions cho deployment, installer MSI/MSIX hoặc Microsoft Store.
- Không ký số executable.
- Không tự động cập nhật version trong source bằng commit ngược về repository.
- Không thêm matrix build cho hệ điều hành khác vì ứng dụng WPF chỉ phát hành cho Windows.

## Luồng CI

1. Checkout commit đang được kiểm tra.
2. Cài SDK từ `global.json`.
3. Restore `RouterPlus.sln` with runtime `win-x64`.
4. Chạy test Release và ghi kết quả TRX.
5. Build solution Release.
6. Publish `RouterPlus.App` với runtime `win-x64` và `SelfContained=true`.
7. Nén output publish.
8. Upload zip; upload TRX nếu có.

CI không cấp quyền ghi repository.

## Luồng release

1. Nhận tag push và checkout đúng tag.
2. Kiểm tra tag bằng SemVer.
3. Lấy version không có tiền tố `v` để truyền vào MSBuild.
4. Cài SDK từ `global.json`.
5. Restore với runtime `win-x64`, test và build lại từ đầu.
6. Publish self-contained `win-x64` với version từ tag.
7. Tạo zip và checksum SHA-256.
8. Tạo GitHub Release theo tag với generated notes.
9. Upload zip và checksum làm release assets.

Release workflow chỉ cấp `contents: write`; không sử dụng quyền ghi khác.

## Đặt tên và version

- Tag hợp lệ: `v1.2.3`, `v1.2.3-rc.1`.
- Release title: `RouterPlus vX.Y.Z`.
- Zip release: `RouterPlus-vX.Y.Z-win-x64.zip`.
- Checksum chứa hash SHA-256 của đúng zip release.
- Version assembly được truyền từ tag tại thời điểm build, không sửa file source.

## Tiêu chí nghiệm thu

- Workflow CI được GitHub nhận diện và trigger đúng trên push/PR vào `master`.
- CI fail nếu restore, test, build hoặc publish fail.
- CI upload được zip self-contained và kết quả test.
- Tag hợp lệ tạo đúng một GitHub Release với zip và checksum.
- Tag không hợp lệ không tạo release.
- Checksum có thể kiểm tra lại bằng công cụ SHA-256 trên Windows.
- Artifact có thể giải nén và chứa `RouterPlus.exe` cùng runtime files cần thiết.
- README mô tả đúng cách chạy CI và phát hành.
- Không cần secret ngoài `GITHUB_TOKEN` mặc định.

## Rủi ro và quyết định

- Self-contained làm artifact lớn hơn framework-dependent nhưng giảm yêu cầu cài runtime cho người dùng.
- `windows-latest` là image động; workflow bám SDK từ `global.json` để giữ tính nhất quán.
- Release chưa được code-sign; nên bổ sung certificate và quy trình ký trước khi phân phối rộng rãi.
- GitHub Release dùng generated notes, vì repository hiện chưa có changelog chuẩn hóa.
