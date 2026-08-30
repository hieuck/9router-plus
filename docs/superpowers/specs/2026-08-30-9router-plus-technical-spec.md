# Đặc tả kỹ thuật 9Router Plus

- **Ngày:** 2026-08-30
- **Trạng thái:** Đề xuất kỹ thuật
- **Phạm vi:** Hoàn thiện ứng dụng Windows hiện có
- **Đối tượng:** Người dùng cá nhân/kỹ sư quản lý nhiều Chrome profile và nhiều AI/provider connection
- **Nền tảng:** Windows 10/11, .NET 8, WPF

## 1. Tóm tắt

9Router Plus là một **local AI profile control center** cho Windows. Ứng dụng giúp người dùng quản lý nhiều Chrome profile, gắn từng profile với các provider như Codex, Kiro, GitHub và OpenRouter, lưu credentials cục bộ dưới dạng mã hóa, thực hiện login có kiểm soát và theo dõi trạng thái kết nối.

Ứng dụng không phải là một dashboard analytics độc lập và không xây backend tài khoản/cloud trong phạm vi này. Đơn vị trung tâm của sản phẩm là **Chrome profile**; provider connection, credential và trạng thái health được hiển thị theo profile.

Mục tiêu kỹ thuật là biến các capability đang có thành một sản phẩm nhất quán, có behavior rõ ràng, có thể kiểm thử deterministic và có khả năng khôi phục khi lỗi.

## 2. Bối cảnh và hiện trạng

Solution hiện có các project:

- `RouterPlus.Core`: model, provider rules, profile logic, security-domain model, update model.
- `RouterPlus.Infrastructure`: Chrome/CDP, Router API, vault storage, settings, update service và login orchestration.
- `RouterPlus.App`: WPF/WinForms UI, ViewModel, dialogs, diagnostics.
- `RouterPlus.Updater`: executable thực hiện update transaction.
- Các test project tương ứng và `RouterPlus.App.E2E` dùng FlaUI/UIA3.

Capability đã tồn tại ở nhiều mức độ:

- Phát hiện, lọc, tìm kiếm và mở Chrome profile.
- Recent profile, ghim profile và profile state.
- Provider catalog cho Codex, Kiro, GitHub và OpenRouter.
- Router API, connection test và quota/usage model.
- Google account vault mã hóa AES-GCM, remembered key bằng Windows DPAPI.
- Provider connection vault bằng DPAPI.
- Google login state machine và một phần OAuth/direct-login automation.
- Credentials Manager và synthetic FlaUI harness.
- Settings theme, font scale, Chrome paths.
- Self-update với staging, health check, rollback và mutex.

Các phần cần hoàn thiện:

- Credentials Manager còn handler dạng `Feature coming soon` cho add/edit/provider configuration.
- Batch login hiện chưa kết nối orchestration thật và còn mô phỏng.
- Provider vault và DPAPI vault thiếu integration tests đầy đủ.
- `AutoLoginOrchestrator` và các automation class thiếu test behavior chính.
- Một số E2E settings đang kiểm tra contract chưa thống nhất với UI hiện tại.
- E2E desktop đang nằm trong solution test chung, chưa có gating rõ ràng cho môi trường headless.

## 3. Mục tiêu sản phẩm

### 3.1 Mục tiêu chính

1. Người dùng chọn đúng Chrome profile trong vài thao tác.
2. Người dùng biết provider nào đã cấu hình và đang hoạt động trên profile đó.
3. Credentials được bảo vệ, không hiển thị hoặc ghi log ngoài ý muốn.
4. Login automation có trạng thái, timeout, cancellation và manual intervention rõ ràng.
5. Mọi thay đổi quan trọng có thể kiểm chứng bằng test deterministic.
6. Update an toàn và có rollback.
7. Lỗi được trình bày theo hướng có thể hành động, không nuốt lỗi im lặng.

### 3.2 Không phải mục tiêu

- Không xây backend hoặc cloud synchronization.
- Không xây hệ thống tài khoản người dùng riêng.
- Không tự động vượt CAPTCHA, MFA hoặc các cơ chế bảo vệ của provider.
- Không lưu credentials plaintext.
- Không chạy live Google/OAuth/Chrome automation mặc định trong CI.
- Không tạo dashboard analytics độc lập chỉ để hiển thị URL hoặc số liệu.
- Không xây hệ thống plugin provider động trong phiên bản đầu.
- Không retry login vô hạn.

## 4. Thuật ngữ và nguyên tắc

| Thuật ngữ | Ý nghĩa |
|---|---|
| Profile | Một Chrome profile độc lập, có đường dẫn dữ liệu và identity riêng |
| Provider | Dịch vụ đích như Codex, Kiro, GitHub hoặc OpenRouter |
| Provider connection | Cấu hình provider gắn với một profile |
| Google credential | Email, password và TOTP secret dùng cho Google login |
| Direct credential | Credential riêng của provider |
| Vault | Kho dữ liệu credentials được mã hóa cục bộ |
| Remembered unlock | Khóa unlock được bọc bằng Windows DPAPI để mở vault trên cùng thiết bị |
| Manual intervention | Trạng thái yêu cầu người dùng hoàn thành CAPTCHA/MFA/consent hoặc thao tác tương tự |
| Live test | Test dùng Chrome, Router, Google hoặc provider thật; chỉ chạy opt-in |

Nguyên tắc thiết kế:

- **Profile-first:** mọi provider operation phải có profile context.
- **Secure by default:** secret bị che, log bị làm sạch, session được dispose.
- **Explicit state:** không dùng trạng thái mơ hồ như chỉ có `true/false` cho workflow dài.
- **Deterministic by default:** test không phụ thuộc internet, tài khoản thật hoặc cửa sổ desktop thật nếu không cần.
- **No silent fallback:** fallback phải được mô hình hóa và hiển thị trong kết quả.
- **Safe recovery:** thao tác lưu và update phải nguyên tử hoặc có rollback.
- **Minimal scope:** chỉ xây capability phục vụ profile/provider workflow.

## 5. Phạm vi chức năng

### 5.1 Profile workspace

Ứng dụng phải:

- Liệt kê Chrome profile đã phát hiện.
- Hiển thị tên, trạng thái và provider status tóm tắt.
- Tìm kiếm theo tên profile.
- Lọc theo provider có kết nối, provider chưa có kết nối hoặc unassigned.
- Chọn một profile làm context hiện tại.
- Mở profile bằng Chrome launcher.
- Ghim/bỏ ghim profile.
- Hiển thị recent profiles.
- Hỗ trợ add profile từ profile search nếu profile tồn tại trong Chrome user-data directory.
- Hỗ trợ xóa profile chỉ khi có xác nhận rõ ràng và có guard chống xóa profile đang hoạt động.
- Không hiển thị một profile hai lần sau khi refresh.
- Giữ selection hợp lệ khi danh sách refresh; nếu profile bị mất thì clear selection an toàn.

### 5.2 Provider connection

Provider được hỗ trợ trong phiên bản đầu:

- Codex
- Kiro
- GitHub
- OpenRouter

Mỗi connection phải có:

- `ProfileName` hoặc profile identity ổn định.
- `ProviderKind`.
- `PreferredMethod`: Google OAuth hoặc Direct.
- Google account liên kết, nếu có.
- Trạng thái có direct credential, nếu có.
- Trạng thái connection gần nhất.
- Thời điểm kiểm tra gần nhất.
- Lỗi an toàn gần nhất, không chứa secret.

Trạng thái connection đề xuất:

```text
NotConfigured
Configured
Checking
Connected
ManualInterventionRequired
AuthenticationFailed
Unavailable
Unknown
```

Behavior bắt buộc:

- Mỗi profile/provider chỉ có tối đa một connection hiện hành.
- Save cùng profile/provider là upsert, không tạo bản ghi trùng.
- Xóa connection không xóa Google account dùng chung nếu account còn được profile/provider khác tham chiếu.
- `Test connection` không sửa credential nếu chỉ kiểm tra thất bại.
- Lỗi mạng không được biến thành `NotConfigured`.
- Provider chưa hỗ trợ workflow phải hiển thị `NotConfigured` hoặc `Unavailable` rõ ràng, không hiển thị như đã kết nối.

### 5.3 Credentials Manager

Credentials Manager gồm hai vùng:

1. Google accounts.
2. Provider connections/direct credentials.

#### Unlock

- Khi vault chưa được mở, app hiển thị trạng thái locked và nút `Unlock`.
- Nếu có remembered key hợp lệ, app có thể tự mở vault.
- Nếu remembered key không hợp lệ, app xóa hoặc vô hiệu hóa key hỏng theo chính sách và giữ vault locked.
- Password rỗng hoặc chỉ chứa whitespace bị từ chối trước khi truy cập file.
- Password sai trả thông báo an toàn như `Invalid vault password`; không hiển thị exception chi tiết hoặc password thật.
- Password box phải được clear sau khi dialog đóng, dù unlock thành công hay thất bại.
- Tùy chọn `Remember on this device` chỉ lưu khóa bọc bằng DPAPI, không lưu password.

#### Google account rows

Mỗi row có:

- Profile name.
- Email.
- Password.
- TOTP secret.
- Trạng thái có credentials.
- Trạng thái selected cho batch operation.
- Edit/save/cancel/remove theo state.
- Toggle password visibility độc lập với TOTP visibility.

Behavior:

- Password và TOTP bị che mặc định.
- Chỉ hiển thị secret khi vault đang unlock và người dùng chủ động bật toggle.
- Kết thúc edit, clear row hoặc remove row phải reset visibility về hidden.
- Save yêu cầu email và password không rỗng.
- Email được trim; password không tự ý trim vì có thể là secret hợp lệ.
- TOTP rỗng được chuẩn hóa thành placeholder `NONE` nếu domain model yêu cầu.
- Save dùng immutable upsert theo profile identity.
- Save thất bại không được chuyển row sang trạng thái đã lưu.
- Remove cần confirmation ở UI; cancel confirmation không thay đổi vault.
- Remove chỉ xóa record của profile mục tiêu, kể cả khi email trùng ở profile khác.

#### Provider rows

Provider configuration phải là UI thật thay vì message `Feature coming soon`.

Mỗi form cấu hình phải cho phép:

- Chọn auth method.
- Chọn Google account đã lưu hoặc tạo liên kết.
- Nhập direct credential khi provider hỗ trợ.
- Lưu hoặc hủy.
- Test connection sau khi lưu.
- Xóa connection.

Form không được:

- Hiển thị password/TOTP của connection khác trong summary.
- Ghi credential vào log.
- Ghi một phần cấu hình khi validation thất bại.

### 5.4 Login automation

#### Single login

Flow chuẩn:

```text
Chọn profile
  → Chọn provider
  → Đọc connection
  → Xác định preferred auth method
  → Mở vault nếu cần
  → Chạy OAuth hoặc direct automation
  → Cập nhật connection state
  → Hiển thị kết quả
```

Kết quả chuẩn gồm:

- `Success`
- `NoCredentials`
- `InvalidCredentials`
- `ManualInterventionRequired`
- `Cancelled`
- `TimedOut`
- `ProviderUnavailable`
- `Failed`

#### Preferred method và fallback

- Nếu preferred method thành công, không chạy fallback.
- Nếu preferred method thất bại theo loại lỗi cho phép fallback, thử fallback nhiều nhất một lần.
- Không fallback khi người dùng chủ động cancel.
- Không fallback khi lỗi là cấu hình thiếu và fallback cũng không có dữ liệu.
- Kết quả phải ghi method đã thử và method thành công cuối cùng.
- Nếu cả hai thất bại, trả lỗi tổng hợp an toàn và giữ lại nguyên nhân có thể hành động.
- Mỗi profile chỉ có tối đa một workflow login chạy tại một thời điểm.

#### Manual intervention

- App không cố tự động vượt CAPTCHA/MFA.
- UI hiển thị provider, profile và hành động người dùng cần thực hiện.
- Người dùng có thể `Continue` hoặc `Cancel` nếu browser automation hỗ trợ.
- Timeout manual intervention phải kết thúc workflow với `TimedOut`, không để task treo.

#### Batch login

- Chỉ chọn row có credentials hợp lệ.
- Row thiếu credentials được bỏ qua và ghi `Skipped`.
- Batch có progress theo từng profile.
- Có thể cancel; các row chưa chạy chuyển `Skipped` hoặc `Cancelled` theo state contract.
- Không chạy song song trên cùng profile.
- Summary phải phân biệt success, failed, skipped và cancelled.
- Batch không được dùng delay mô phỏng trong behavior production; phải gọi orchestrator thật qua abstraction có thể fake trong test.

### 5.5 Health và quota

- Người dùng có thể refresh status cho profile đang chọn hoặc toàn bộ danh sách.
- `Checking` phải hiển thị trong lúc request đang chạy.
- Thành công cập nhật last checked và quota nếu có.
- Lỗi mạng giữ lại trạng thái cấu hình cũ và hiển thị `Unavailable`, không xóa connection.
- Quota thấp/hết quota hiển thị cảnh báo nhưng không tự xóa credentials.
- Chính sách auto-disable nếu được bật phải ghi marker có lý do và timestamp.
- Reset hoặc kiểm tra lại quota không làm mất provider auth configuration.

Trong v1 không có dashboard URL hoặc màn hình dashboard dành cho người dùng. Nếu Router API production thực sự cần một endpoint, endpoint đó là cấu hình nội bộ của service, không phải user-facing product setting; nó phải được validate và kiểm thử ở service layer. Các test E2E yêu cầu nhập hoặc persist dashboard URL không thuộc contract v1 và phải được loại bỏ hoặc chuyển thành test Router API phù hợp.

### 5.6 Settings

Settings phiên bản đầu:

- Chrome executable path.
- Chrome user-data directory.
- Theme sáng/tối.
- Font scale.
- Tùy chọn remember vault key.
- Timeout cho automation.
- Tùy chọn kiểm tra status tự động.
- Đường dẫn backup/export nếu cần.
- Không có dashboard URL trong user-facing settings; endpoint Router nội bộ, nếu production cần, do service quản lý.

Behavior:

- Validation chạy trước save.
- Save disabled khi có lỗi validation hoặc workflow không cho phép thay đổi.
- Cancel/reset trả về state đã lưu gần nhất.
- Save rồi restart phải giữ các setting được hỗ trợ.
- Test không được yêu cầu field không tồn tại trong UI hiện hành.

### 5.7 Backup, import và recovery

- Export tạo backup vault được mã hóa, không plaintext.
- Import kiểm tra version, envelope, integrity và schema trước khi thay thế dữ liệu.
- Import lỗi không làm hỏng vault hiện tại.
- Thay thế vault dùng file tạm và atomic move khi có thể.
- Trước thao tác destructive, tạo backup nếu chính sách yêu cầu.
- Import/export không ghi password hoặc khóa plaintext vào diagnostics.
- File không hợp lệ trả lỗi phân loại: wrong password, tampered, unsupported version hoặc invalid format.

### 5.8 Self-update

Update flow:

```text
Check release
  → Verify metadata/package
  → Download vào staging
  → Chờ parent process thoát
  → Lock updater mutex
  → Move live → backup
  → Move staging → live
  → Launch health check
  → Success: xóa backup
  → Failure: rollback live từ backup
```

Behavior:

- Path phải absolute và staging/live/backup không được trùng.
- Executable phải nằm dưới target directory.
- Không chạy hai updater đồng thời.
- Parent timeout trả kết quả riêng, không đụng live app.
- Health check thất bại phải rollback.
- Rollback thất bại trả `RollbackFailed`, không báo thành công.
- Cancellation không được bỏ lại live app ở trạng thái không xác định nếu rollback có thể thực hiện.

### 5.9 Diagnostics

- Status message dành cho người dùng phải ngắn, có hướng xử lý.
- Error detail kỹ thuật ghi vào diagnostics ở mức cần thiết.
- Secret patterns gồm password, TOTP, API key, access token, cookie và authorization header phải bị loại khỏi log.
- Diagnostics export không được chứa file vault hoặc browser profile trừ khi người dùng chủ động chọn và có cảnh báo.
- Các lỗi phải phân biệt validation/configuration, authentication, authorization/access denied, network/provider unavailable, timeout/cancellation, storage/corruption và unexpected system failure.

## 6. Kiến trúc kỹ thuật

### 6.1 Layering

```text
RouterPlus.App
  ├── Views / WPF controls
  ├── ViewModels / commands / UI state
  └── Application-facing interfaces

RouterPlus.Infrastructure
  ├── Chrome / CDP / automation adapters
  ├── Router API client
  ├── Vault and settings stores
  ├── Login orchestrators
  └── Update services

RouterPlus.Core
  ├── Immutable domain models
  ├── Provider catalog and policy
  ├── Profile/filter rules
  ├── Login result/state models
  └── Update value objects

RouterPlus.Updater
  └── Isolated update transaction executable
```

Dependency direction:

- Core không phụ thuộc WPF, filesystem, HTTP hoặc Chrome.
- Infrastructure phụ thuộc Core.
- App phụ thuộc Core và Infrastructure qua các abstraction cần thiết.
- Updater phụ thuộc Core update models, không phụ thuộc App UI.
- UI event handlers không tự chứa storage/orchestration business logic.

### 6.2 Testability seams

Các dependency sau phải có interface hoặc factory boundary để có thể fake:

- `IChromeLauncher`
- `IGoogleLoginBrowser`
- `IProviderLoginAutomation` hoặc factory tương đương
- `IRouterApiClient`
- `IGoogleAccountVaultStore`
- Provider connection store abstraction
- `ISecretVault`
- `IUpdateTransactionRuntime`
- External link/process launcher
- Clock/time provider nếu workflow timeout hoặc quota policy cần deterministic time

Không cần tạo abstraction cho mọi class. Chỉ tạo seam tại boundary với OS, filesystem, network, Chrome hoặc thời gian.

### 6.3 Application state

`MainViewModel` quản lý:

- Profile collection và filtered collection.
- Selected profile.
- Provider filter state.
- Recent/pinned profile state.
- Settings state và validation state.
- Batch progress.
- Update state.
- Connection/health summary.

`CredentialsManagerViewModel` quản lý:

- Vault session lifecycle.
- Google account rows.
- Provider connection rows.
- Selection/edit state.
- Unlock/save/remove/refresh commands.
- Status message an toàn.

ViewModel không được khởi tạo background task không thể await hoặc dispose. Nếu constructor phải kick off load để tương thích WPF, phải có observable initialization task hoặc test helper để chờ state ổn định.

## 7. Data model và persistence

### 7.1 Google vault

Logical model:

```text
GoogleAccountVault
  Records: GoogleLoginCredential[]

GoogleLoginCredential
  ProfileId: string
  Email: string
  Password: string
  TotpSecret: string
```

Invariant:

- ProfileId, Email, Password và TOTP theo domain rule không được null/invalid.
- Mỗi ProfileId có tối đa một record.
- Upsert không mutate instance cũ.
- Password/TOTP chỉ tồn tại trong vùng đã mã hóa khi lưu xuống disk.

Storage contract:

- Payload mã hóa AES-GCM.
- Key derivation dùng PBKDF2-HMAC-SHA256 theo tham số versioned hiện hành.
- Remembered key được bọc bằng DPAPI CurrentUser.
- Save dùng temp file + replace/move để tránh file nửa chừng.
- Session dispose giải phóng tài nguyên và ngăn thao tác sau dispose.

### 7.2 Provider connection vault

Logical key:

```text
(profile identity, provider kind) → ProviderAuthConnection
```

Connection chứa preferred auth method, linked Google account reference nếu có, direct credential nếu có và metadata không nhạy cảm như last state/last checked nếu persistence contract yêu cầu.

Invariant:

- Không duplicate profile/provider.
- Save là upsert.
- Remove chỉ xóa key mục tiêu.
- Không để direct credential plaintext trong file.
- Corrupt store không được trả về dữ liệu nửa hợp lệ.

### 7.3 Generic DPAPI secret vault

`ISecretVault` cung cấp save, load, remove và clear/rotate secret theo key.

Contract:

- DPAPI scope phải được xác định rõ.
- Missing key trả null/not found, không tạo secret ngầm.
- Corrupt data trả lỗi phân loại.
- Concurrent write không làm mất bản ghi khác.

### 7.4 Settings

Settings là dữ liệu không nhạy cảm, nhưng path và provider metadata cần được validate. Save phải atomic và load malformed settings phải dùng fallback an toàn kèm diagnostics.

## 8. UI specification

### 8.1 Main window

Bố cục logic:

```text
┌─────────────────────────────────────────────────┐
│ Header: app name, refresh, settings, help       │
├────────────────┬────────────────────────────────┤
│ Profile list    │ Selected profile               │
│ search/filter   │ Open profile                   │
│                 │ Provider connections           │
│ Work            │ Codex      Connected   Test    │
│ Personal        │ Kiro       Not configured      │
│ Research        │ GitHub     Connected   Test    │
│                 │ OpenRouter Unavailable  Fix    │
└────────────────┴────────────────────────────────┘
```

Yêu cầu:

- Selection và provider state phải nhìn thấy mà không cần mở nhiều dialog.
- Mỗi button có AutomationId ổn định.
- Disabled state phải phản ánh command CanExecute.
- Không dùng text localization làm selector duy nhất cho E2E nếu có thể dùng AutomationId.
- UI phải có trạng thái loading, empty, error và disabled rõ ràng.

### 8.2 Credentials Manager

Tabs:

- Google accounts.
- Codex.
- Kiro.
- GitHub.
- OpenRouter.

Toolbar:

- Unlock/lock state.
- Refresh.
- Add.
- Remove.
- Batch login nếu có row đủ điều kiện.
- Close.

Rows cần AutomationId ổn định theo loại control, không theo index duy nhất. Ví dụ:

```text
CredentialsManagerButton
CredentialsManagerCloseButton
CredentialsManagerStatus
UnlockVaultButton
GoogleAccountsList
GoogleEmailEditor
GooglePasswordEditor
GoogleTotpEditor
GooglePasswordVisibilityButton
GoogleTotpVisibilityButton
GoogleLoginRowButton
BatchLoginButton
```

### 8.3 Dialog rules

- Dialog destructive action dùng confirmation.
- Dialog password không giữ text sau close.
- Close button phải dispose vault session một lần an toàn.
- Window close bằng title bar và Close button có cùng cleanup semantics.
- Async event handler phải bắt lỗi và cập nhật status, không để exception thoát UI thread.

## 9. Error and state contracts

| Tình huống | UI state | Hành động tiếp theo |
|---|---|---|
| Vault chưa mở | Locked | Unlock |
| Password rỗng | Validation error | Nhập password |
| Password sai | Locked + safe error | Thử lại hoặc đóng |
| Remembered key hỏng | Locked | Unlock thủ công |
| Vault corrupt | Error | Restore/import backup |
| Provider chưa cấu hình | NotConfigured | Configure |
| Credentials thiếu | NoCredentials | Add/edit credentials |
| Login đang chạy | Running | Chờ hoặc cancel |
| Cần MFA/CAPTCHA | ManualInterventionRequired | Continue/Cancel |
| Login timeout | TimedOut | Retry có chủ đích |
| Network lỗi | Unavailable | Retry/test connection |
| Save lỗi | Edit remains | Sửa hoặc retry |
| Update health fail | Rollback | Retry update hoặc restore |

Không được dùng empty string như cách duy nhất để biểu diễn mọi error state.

## 10. Security requirements

### SR-1 Secret handling

Password, TOTP, API key, token, cookie và authorization header không được ghi vào log, ghi vào test artifact không được bảo vệ, hiển thị trong summary/list hoặc đưa vào exception message nếu không cần thiết.

### SR-2 Vault protection

- Dữ liệu vault mã hóa authenticated encryption.
- Sai password và tamper phải fail closed.
- Remembered unlock chỉ có hiệu lực trên user/device scope đã chọn.
- Dispose session phải ngăn việc sử dụng session đã đóng.

### SR-3 UI disclosure

- Secret hidden mặc định.
- Visibility toggle là hành động chủ động và có phạm vi row.
- Close/edit-end/remove reset visibility.
- Clipboard export nếu bổ sung sau này phải có timeout clear và confirmation.

### SR-4 Automation isolation

- Automation chạy trên đúng profile context.
- Không dùng profile người dùng thật trong deterministic test.
- Không tự động thao tác ngoài provider flow đã được xác định.
- Live test không được chạy nếu thiếu explicit opt-in.

### SR-5 Logs and diagnostics

- Redaction tập trung, test được.
- Error classification không làm mất stack trace nội bộ trong debug artifact an toàn, nhưng user-facing message phải safe.
- Diagnostics export phải được kiểm tra không chứa secret.

### SR-6 Update integrity

- Package và metadata phải được verify trước swap.
- Path traversal và path collision phải bị từ chối.
- Rollback là behavior bắt buộc, không phải best effort không quan sát được.

## 11. Chiến lược kiểm thử

Chiến lược là **Hybrid**: deterministic mặc định, live opt-in.

### 11.1 Unit tests

#### Core

Bao phủ model validation và immutable upsert/remove, provider catalog và method mapping, profile parser/filter/search/matcher, provider status/API key state/quota policy, usage inference, Google TOTP, Google login state machine/result mapping và release/update validation.

Test boundary values, invalid input, duplicate, empty và cancellation. Không assert implementation detail nếu behavior có thể kiểm tra qua outcome. Mỗi test có setup riêng, không shared mutable state.

#### App/ViewModel

Bao phủ MainViewModel initialization/filtering/selection/recent/pinned/settings validation; provider cards và command CanExecute; batch progress/cancel/summary; CredentialsManager locked/unlocked/load/save validation/upsert/remove/visibility/status/disposal; GoogleAutoLoginViewModel; converters và small UI behavior classes.

#### Updater

Bao phủ validation từng loại path, mutex refused, parent wait success/timeout/cancel, swap success, health failure/rollback, swap failure, rollback failure, cancellation và cleanup.

### 11.2 Integration tests

Chạy trên Windows với dữ liệu tạm:

- Google vault create/open/save/reopen.
- Sai password, tamper, malformed envelope, unsupported version.
- Remembered key, invalidation, disposal.
- Atomic save và file replacement.
- ProviderConnectionVaultStore round trip, upsert, remove, duplicate, corruption.
- Generic DPAPI secret vault round trip, missing key, corruption.
- SettingsStore save/load/malformed fallback.
- RouterApiClient với fake `HttpMessageHandler`: success, auth error, server error, timeout, malformed JSON, quota mapping.
- MainViewModel kết nối store + fake HTTP.
- AutoLoginOrchestrator với fake vault, launcher, browser/automation factory: preferred method, fallback, cancel, timeout, disposal.
- Update verifier và update transaction trên thư mục tạm.

Integration tests không truy cập Google, provider thật hoặc Chrome profile thật.

### 11.3 Synthetic E2E

FlaUI/UIA3 chạy với harness root tạm và synthetic profiles:

1. App startup và main window.
2. Profile list, search, filter, selection, open/close.
3. Settings supported: valid save, invalid input, persistence nếu UI có field đó.
4. Credentials Manager mở khi vault locked.
5. Unlock sai password.
6. Unlock thành công.
7. Remember unlock.
8. Google row visibility.
9. Google edit/save/reopen.
10. Google remove/cancel confirmation.
11. Provider tab và configured/unconfigured rows.
12. Provider configure/save/remove khi UI đã triển khai.
13. Single login synthetic success/failure/manual/cancel.
14. Batch login progress/success/failure/cancel.
15. App close và cleanup.
16. Update flow chỉ khi có harness an toàn riêng.

E2E phải dùng AutomationId ổn định, retry/polling với timeout hữu hạn, capture snapshot/log khi fail và tránh `Task.Delay` cố định nếu có thể chờ state. Không chạy đồng thời các test dùng desktop session nếu runner không hỗ trợ isolation.

### 11.4 Live E2E

Live E2E là nhóm riêng, chỉ chạy khi có:

```text
ROUTERPLUS_LIVE_E2E=1
```

và các cấu hình cần thiết. Nhóm này xác nhận Chrome/CDP thật, Google OAuth thật, provider OAuth/direct login thật và Router service thật. Live test không nằm trong đường chạy mặc định của PR CI. Không commit credentials, profile path thật hoặc token.

### 11.5 Coverage quality gate

Mục tiêu đề xuất:

- Core domain: tối thiểu 85% line/branch coverage cho code có logic.
- Infrastructure boundary/orchestration: tối thiểu 80% behavior coverage.
- App ViewModel: tối thiểu 80% command/state/error coverage.
- Updater transaction: 90% nhánh failure/recovery quan trọng.
- E2E: mọi critical user journey có ít nhất một flow.

Coverage percentage không thay thế review behavior. Code generated, UI markup thuần và OS wrapper mỏng có thể được exclude có lý do.

## 12. CI/CD

### Fast PR

```text
dotnet build RouterPlus.sln --configuration Release
dotnet test tests/RouterPlus.Core.Tests --no-build
dotnet test tests/RouterPlus.Infrastructure.Tests --no-build
dotnet test tests/RouterPlus.App.Tests --no-build
dotnet test tests/RouterPlus.Updater.Tests --no-build
```

Không chạy desktop E2E trong lane này nếu runner không có interactive desktop.

### Windows synthetic E2E

- Chạy trên Windows runner có desktop session.
- Build đúng configuration với path mà harness sử dụng.
- Chạy `RouterPlus.App.E2E` riêng.
- Upload failure log/screenshot nếu có.
- Không chạy parallel nếu UIA desktop bị tranh chấp.

### Live validation

- Manual/secured workflow.
- Explicit environment flag.
- Secrets lấy từ CI secret store, không từ source.
- Không dùng dữ liệu người dùng thật nếu không có approval.

Package versions của các test project nên được chuẩn hóa về một version tương thích của xUnit, Test SDK và coverlet để giảm khác biệt giữa test runner.

## 13. Observability và maintenance

- Dùng event/category thống nhất cho startup, UI action, vault, login, update và diagnostics.
- Mọi async workflow có correlation id không chứa secret nếu cần truy vết.
- Test helper dùng polling với timeout hữu hạn.
- Shared fixture chỉ dùng cho immutable setup; không share vault session mutable giữa test.
- Mỗi test artifact phải được cleanup best effort nhưng không nuốt failure chính.
- Stale test phải được xử lý theo contract, không sửa production chỉ để thỏa một selector không còn tồn tại.

## 14. Lộ trình triển khai

### Phase 0 — Contract cleanup

- Kiểm kê capability thực sự có trong UI và service.
- Xác nhận không có dashboard URL trong user-facing v1 settings.
- Chuyển các test dashboard URL stale thành test Router API service layer hoặc loại bỏ.
- Sửa/quarantine test stale theo contract đã chốt.
- Chuẩn hóa test package versions và test categories.
- Tách E2E khỏi fast CI.

**Kết quả:** baseline xanh hoặc có failure được phân loại rõ ràng.

### Phase 1 — Test foundation

- Tạo fake HTTP/browser/process/clock cần thiết.
- Thêm shared test builders và temp-directory fixtures.
- Viết integration tests cho provider vault, DPAPI vault, settings và router client.
- Chuẩn hóa redaction test.

**Kết quả:** boundary storage/network có regression protection.

### Phase 2 — Core/App behavior

- Hoàn thiện unit coverage cho ViewModel, command, profile/filter/provider/quota/update behavior.
- Hoàn thiện CredentialsManagerViewModel behavior.
- Loại bỏ fire-and-forget không quan sát được hoặc cung cấp initialization wait seam.

**Kết quả:** mọi state transition chính được kiểm chứng mà không cần UI.

### Phase 3 — Login orchestration

- Tạo automation/factory interfaces.
- Kết nối preferred/fallback/cancel/timeout.
- Thay batch simulation bằng orchestrator thật.
- Viết integration tests với fake automation.

**Kết quả:** login workflow production không còn là delay mô phỏng.

### Phase 4 — UI functionality

- Triển khai add/edit/remove Google account.
- Triển khai provider configuration UI.
- Chuẩn hóa status, confirmation, secret visibility và AutomationId.
- Viết App-level tests cho dialog/viewmodel.

**Kết quả:** không còn handler chức năng thật hiển thị `Feature coming soon`.

### Phase 5 — E2E và release hardening

- Ổn định synthetic FlaUI journeys.
- Thêm batch success/failure/cancel E2E.
- Thêm update/rollback harness nếu khả thi.
- Thiết lập live E2E opt-in.
- Upload artifact khi fail và enforce CI lanes.

**Kết quả:** critical journeys chạy lặp lại được trên Windows CI.

## 15. Acceptance criteria

Sản phẩm được xem là đạt đặc tả khi:

1. Người dùng có thể tìm, chọn và mở Chrome profile.
2. Provider connection được hiển thị theo profile, không trộn giữa profiles.
3. Google vault và provider vault lưu secret mã hóa, có unlock/remember/dispose đúng contract.
4. Credentials Manager không còn các nút chính giả lập bằng message `Feature coming soon`.
5. Password/TOTP hidden mặc định và không xuất hiện trong log/test artifact.
6. Save/remove/upsert không tạo duplicate hoặc xóa nhầm profile khác.
7. Login single và batch có kết quả success/failure/manual/cancel/timeout rõ ràng.
8. Preferred/fallback được kiểm chứng bằng test.
9. Router/network failure không xóa cấu hình hợp lệ.
10. Settings chỉ test các field thực sự được hỗ trợ; v1 không có dashboard URL hoặc dashboard screen trong UI.
11. Update health failure rollback thành công hoặc báo `RollbackFailed` trung thực.
12. Fast CI không phụ thuộc internet, Chrome thật, Google account hoặc desktop UI.
13. Synthetic E2E chạy riêng trên Windows desktop runner.
14. Live E2E chỉ chạy opt-in.
15. Unit/Integration/Updater/App test suite đạt ngưỡng behavior coverage đã nêu.
16. `dotnet test RouterPlus.sln` không còn chạy một cách mơ hồ các nhóm test cần desktop hoặc live credentials.

## 16. Quyết định cần giữ trong quá trình triển khai

- Không mở rộng sang backend/cloud sync nếu chưa có yêu cầu riêng.
- Không tự thêm dashboard URL hoặc analytics screen.
- Không sửa behavior production chỉ để phục vụ một test stale chưa được xác nhận là contract.
- Không đánh đổi security để làm E2E dễ hơn.
- Không coi phần trăm coverage là đủ nếu chưa có critical journey.
- Mỗi phase phải giữ test hiện có pass hoặc ghi rõ regression/contract change.

## 17. Kết luận

9Router Plus nên được phát triển như một **profile-centric local control center**, không phải một ứng dụng dashboard chung chung. Giá trị cốt lõi là chọn đúng profile, quản lý đúng provider connection, bảo vệ credentials và chạy automation có kiểm soát.

Đặc tả này dùng code hiện có làm nền, nhưng đặt behavior mục tiêu rõ ràng cho các phần còn thiếu. Thứ tự triển khai ưu tiên test foundation và security boundary trước, sau đó mới hoàn thiện UI configuration và login automation. Cách tiếp cận này giảm rủi ro sửa sai contract, giữ CI deterministic và tạo nền tảng để bổ sung live validation mà không làm ảnh hưởng người dùng hoặc pipeline mặc định.
