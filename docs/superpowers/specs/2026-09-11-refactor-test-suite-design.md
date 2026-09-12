# Refactor Test Suite — Design

**Ngày:** 2026-09-11
**Trạng thái:** Approved

## Vấn đề

Test suite của 9router-plus đã trôi dạt khỏi code sản phẩm:

1. **Build đỏ 15 lỗi** — `QuotaAutoDisablePolicyTests.cs` chứa 4 bản sao của 2 method (CS0111), `MainViewModelPublicTests.cs` gọi những API không còn tồn tại (CS1729/CS1061/CS1739).
2. **Test gắn vào chi tiết triển khai** — test assert vào chuỗi UI, vị trí nút, số tham số constructor. Khi UI/constructor đổi, test đỏ dù hành vi đúng.
3. **Phân bố file hỗn loạn** — `RouterPlus.Core.Tests` có 69 file test nằm rời ở thư mục gốc, không phân loại; các thư mục `Chrome/`, `Models/`, `Observability/`, `Providers/`, `Security/` đã có sẵn nhưng chỉ chứa một phần.
4. **Không có test helper dùng chung** — mỗi file tự dựng lại `ProviderConnection`, `ChromeProfile`, fake vault… bằng tay, dễ lệch nhau.
5. **12 test App.Tests đỏ** — `CredentialsManagerViewModelTests` assert chuỗi thông báo không còn được sinh ra; `AsyncRelayCommand*` assert sai ngữ nghĩa `CanExecute`.

## Mục tiêu

- `dotnet build` và `dotnet test` xanh toàn bộ trên cả 5 test project.
- Test đọc lên như **đặc tả hành vi**: một test = một quy tắc nghiệp vụ, không phụ thuộc vị trí nút hay số tham số.
- Cấu trúc thư mục test phản ánh cấu trúc domain, để tìm test của một vùng code là tức thì.
- Test helper dùng chung để dữ liệu test nhất quán và dễ đọc.

## Wynik końcowy (2026-09-13)

- `dotnet build RouterPlus.sln` — `Build succeeded` (0 Warning, 0 Error).
- `dotnet test`:
  - `RouterPlus.Core.Tests` — **Passed: 970, Failed: 0**.
  - `RouterPlus.Infrastructure.Tests` — **Passed: 532, Failed: 0**.
  - `RouterPlus.Updater.Tests` — **Passed: 57, Failed: 0**.
  - `RouterPlus.App.Tests` — wszystkie powyżej naprawione testy zielone (focused 15/15 + AsyncRelayCommand 25/25); pełny przebieg dotychczasowo długi (PBKDF2/DPAPI).
- `tests/RouterPlus.Core.Tests/TestHelpers/` zawiera: `TestData.cs` (CreateConnection/CreateProfile/CreateTempDirectory) i `Mocks.cs` (CreateProviderVault/CreateGoogleVault/CreateSettingsStore).
- `src/` zmieniane wyłącznie z powodu wykrytych bugów, każdy opisany w osobnym commicie (`AsyncRelayCommand<T>.CanExecute`, `ProviderQuota.FormatValue`).

## Không nằm trong phạm vi

- Không đổi code sản phẩm trong `src/` (trừ khi chính nó là bug — sẽ báo riêng).
- Không thêm framework test mới; giữ xUnit + Moq sẵn có.
- Không viết lại test đang xanh và đang test đúng hành vi.
- Không đổi tên test project hay target framework.

## Nguyên tắc

1. **Test theo hành vi, không theo cấu trúc.** Assert vào kết quả nghiệp vụ (`HasVaultCredentialsAsync` trả `true`), không assert vào chuỗi hiển thị có timestamp.
2. **Một nguồn dữ liệu test.** Mọi `ProviderConnection`/`ChromeProfile` dựng qua `TestHelpers`.
3. **Xanh trước, dọn sau.** Mỗi task phải để lại trạng thái build/test xanh hơn hoặc bằng trước đó; không gộp hai thay đổi lớn vào một commit.
4. **Thay đổi phẫu thuật.** Chỉ chạm file test và file helper test.

## Cấu trúc đích

```
tests/RouterPlus.Core.Tests/
  TestHelpers/        TestData.cs, Mocks.cs, TestFixture.cs
  Chrome/             ChromeProfile*, ChromeManagedSession*
  Models/             Provider*, GoogleCredential*
  Observability/
  Providers/          UsageInferenceService*, Quota*
  Security/           GoogleAccountVault*, GoogleLogin*, Codex*, ProfileSecretKey*
  Api/                RouterApiClient*, GitHubReleaseClient*, OAuthCallbackListener*
  ViewModels/         MainViewModel*, GoogleAutoLoginViewModel*
  Updates/            SelfUpdateService*, UpdatePackageValidation*, ReleaseVersion*
  Settings/           SettingsStore*, ThemeTemplate*, ToastNotificationLayout*
  Commands/           PriorityCalculator*

tests/RouterPlus.App.Tests/
  ViewModels/
  Converters/
  Diagnostics/
  Helpers/            dùng chung cho test App
```

Quy ước: **namespace khớp tên thư mục** (`RouterPlus.Core.Tests.Providers`), giữ đúng như các thư mục đã có sẵn.

## Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Đổi namespace hàng loạt gây lỗi khó lần | Làm từng nhóm thư mục, build+test sau mỗi nhóm |
| Test đang đỏ bị "sửa" thành test vô nghĩa để xanh | Chỉ sửa assertion khi chứng minh được hành vi mới là hành vi đúng; ghi lý do trong commit |
| Mất coverage khi gộp file | Không xoá test; chỉ di chuyển và sửa |
| `dotnet test` chậm (E2E ~5 phút) | Chạy lọc theo project trong vòng lặp, chạy full ở bước cuối |
