# Refactor Test Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Làm cho `dotnet build` + `dotnet test` xanh trên cả 5 test project, đồng thời tái cấu trúc thư mục test theo domain và gom dữ liệu test về một nguồn dùng chung.

**Architecture:** Chỉ chạm `tests/`. Không sửa `src/`. Mỗi task kết thúc bằng một trạng thái build/test xanh hơn hoặc bằng trước đó, và một commit riêng.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, xUnit 2.9.2 (Core.Tests) / 2.5.3 (App.Tests), Moq 4.20.72.

**Spec:** `docs/superpowers/specs/2026-09-11-refactor-test-suite-design.md`

## Global Constraints

- Không sửa bất kỳ file nào trong `src/`.
- Không đổi tên test project, không đổi TargetFramework, không thêm package test mới.
- Namespace phải khớp tên thư mục: `RouterPlus.Core.Tests.<Folder>`.
- `RouterPlus.App.Tests/ViewModels/MainViewModelPublicTests.cs` hiện đang gọi API không tồn tại — đây là chặn build, phải xử lý trước mọi việc khác.
- Mỗi task kết thúc bằng `dotnet build` và `dotnet test` cho project liên quan phải xanh.

---

### Task 1: Sửa `MainViewModelPublicTests.cs` — gỡ chặn build

**Files:**
- Modify: `tests/RouterPlus.App.Tests/ViewModels/MainViewModelPublicTests.cs`

**Bối cảnh:** File hiện tại tham chiếu 3 API không tồn tại:
- `IProviderConnectionVaultStore` — không có interface này; `ProviderConnectionVaultStore` là class `sealed` (`src/RouterPlus.Infrastructure/Security/ProviderConnectionVaultStore.cs:27`).
- `IGoogleAccountVaultPaths` — không có; là class `sealed` `GoogleAccountVaultPaths` (`src/RouterPlus.Infrastructure/Security/GoogleAccountVaultPaths.cs:6`).
- `ISettingsStore` / `Settings` — không có; là class `SettingsStore` (`src/RouterPlus.Infrastructure/Storage/SettingsStore.cs:9`) và record `RouterSettings` (`src/RouterPlus.Infrastructure/Storage/RouterSettings.cs:5`).

Constructor thật của `MainViewModel` (`src/RouterPlus.App/ViewModels/MainViewModel.cs:131-147`) có 17 tham số optional, **không** có `providerConnectionVaultStore`. Nó tự dựng vault từ `googleLoginVaultPaths.VaultPath`:

```csharp
var providerConnectionPath = Path.Combine(
    Path.GetDirectoryName(_googleLoginVaultPaths.VaultPath) ?? string.Empty,
    "provider-connections.vault");
_providerConnectionVaultStore = new ProviderConnectionVaultStore(providerConnectionPath);
```

Vì vậy test phải trỏ `googleLoginVaultPaths: new GoogleAccountVaultPaths(tempDir)` và ghi credential thật vào `ProviderConnectionVaultStore(Path.Combine(tempDir, "provider-connections.vault"))`.

- [ ] **Step 1: Viết lại file**

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.App.ViewModels;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class MainViewModelPublicTests
{
    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static ChromeProfile CreateProfile(string name, string directoryName, string userDataDirectory) =>
        new(ChromeProfile.CreateId(userDataDirectory, directoryName), name, directoryName, userDataDirectory, false);

    private static async Task<MainViewModel> CreateViewModelAsync(string directory, params ChromeProfile[] profiles)
    {
        var settingsStore = new SettingsStore(Path.Combine(directory, "settings.json"));
        await settingsStore.SaveAsync(new RouterSettings(DashboardBaseUrl: "http://localhost:20128"));

        var viewModel = new MainViewModel(
            settingsStore,
            googleLoginVaultPaths: new GoogleAccountVaultPaths(directory),
            harnessProfiles: profiles);

        await viewModel.InitializeAsync();
        return viewModel;
    }

    private static ProviderConnectionVaultStore CreateProviderVault(string directory) =>
        new(Path.Combine(directory, "provider-connections.vault"));

    [Fact]
    public async Task HasVaultCredentialsAsync_returns_true_when_a_provider_has_credentials()
    {
        var directory = CreateTempDirectory();
        try
        {
            var profile = CreateProfile("Work", "Default", directory);
            var vault = CreateProviderVault(directory);
            await vault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = profile.Name,
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "user@example.com"
            });

            var viewModel = await CreateViewModelAsync(directory, profile);

            Assert.True(await viewModel.HasVaultCredentialsAsync(profile, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task HasVaultCredentialsAsync_returns_false_when_no_provider_has_credentials()
    {
        var directory = CreateTempDirectory();
        try
        {
            var profile = CreateProfile("Work", "Default", directory);
            var viewModel = await CreateViewModelAsync(directory, profile);

            Assert.False(await viewModel.HasVaultCredentialsAsync(profile, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SelectProfilesWithVaultCredentialsAsync_selects_only_profiles_with_credentials()
    {
        var directory = CreateTempDirectory();
        try
        {
            var withCreds = CreateProfile("WithCreds", "Default", directory);
            var withoutCreds = CreateProfile("WithoutCreds", "Profile 1", directory);
            var vault = CreateProviderVault(directory);
            await vault.SaveConnectionAsync(new ProviderAuthConnection
            {
                ProfileName = withCreds.Name,
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = "user@example.com"
            });

            var viewModel = await CreateViewModelAsync(directory, withCreds, withoutCreds);

            await viewModel.SelectProfilesWithVaultCredentialsAsync(CancellationToken.None);

            Assert.True(viewModel.IsMultiSelectMode);
            Assert.Equal(1, viewModel.ProfileRows.Count(row => row.IsSelected));
            Assert.Equal(
                "Đã chọn 1 profile có vault credentials",
                viewModel.StatusText);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
```

Nếu `AuthMethod` không nằm trong `RouterPlus.Core.Models`, sửa `using` theo namespace thật (`grep -n "enum AuthMethod" src/ -r`).

- [ ] **Step 2: Build**

Run: `dotnet build tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj -v q --nologo`
Expected: `Build succeeded` — 0 error.

- [ ] **Step 3: Chạy test của file này**

Run: `dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj --no-build --filter "FullyQualifiedName~MainViewModelPublicTests"`
Expected: 3/3 Passed.

- [ ] **Step 4: Commit**

```bash
git add tests/RouterPlus.App.Tests/ViewModels/MainViewModelPublicTests.cs
git commit -m "test: align MainViewModelPublicTests with real MainViewModel API"
```

---

### Task 2: Tái cấu trúc thư mục `RouterPlus.Core.Tests`

**Files:**
- Move: 69 file trong `tests/RouterPlus.Core.Tests/Unit/` vào các thư mục domain
- Move: `tests/RouterPlus.Core.Tests/Unit/Usings.cs` → `tests/RouterPlus.Core.Tests/Usings.cs`

**Lưu ý:** `Unit/QuotaAutoDisablePolicyTests.cs` đã được sửa và đang xanh (23/23) — **không viết lại**, chỉ di chuyển namespace.

Bảng ánh xạ (file → thư mục đích, namespace tương ứng):

| File | Thư mục | Namespace |
|---|---|---|
| `ChromeManagedSessionTests.cs`, `ChromeProfileCatalogTests.cs`, `ChromeProfileDeleterTests.cs`, `ChromeProfileFilterTests.cs`, `ChromeProfileParserTests.cs`, `ChromeProfileProvisionerTests.cs` | `Chrome/` | `RouterPlus.Core.Tests.Chrome` |
| `CodexCredentialAndResultTests.cs`, `CodexLoginCredentialTests.cs`, `CodexLoginModelsTests.cs`, `CodexLoginResultTests.cs`, `GoogleAccountVaultStoreTests.cs`, `GoogleAccountVaultTests.cs`, `GoogleLoginCdpBrowserTests.cs`, `GoogleLoginCredentialTests.cs`, `GoogleLoginResultTests.cs`, `GoogleLoginStateMachineTests.cs`, `GoogleTotpGeneratorAdditionalTests.cs`, `GoogleTotpGeneratorTests.cs`, `ProfileSecretKeyTests.cs` | `Security/` | `RouterPlus.Core.Tests.Security` |
| `GitHubReleaseClientHttpErrorTests.cs`, `GitHubReleaseClientTests.cs`, `OAuthCallbackListenerTests.cs`, `RouterApiClientHttpBehaviorTests.cs`, `RouterApiClientLifecycleTests.cs`, `RouterApiConnectionTests.cs`, `RouterApiDeleteConnectionTests.cs`, `RouterApiOAuthTests.cs`, `NodeRouterSetupServiceTests.cs` | `Api/` | `RouterPlus.Core.Tests.Api` |
| `MainViewModelApiKeyTests.cs`, `MainViewModelAsyncBranchTests.cs`, `MainViewModelAutoGetKeyTests.cs`, `MainViewModelCoreBehaviorTests.cs`, `MainViewModelDashboardCommandTests.cs`, `MainViewModelDeviceCodeWorkflowTests.cs`, `MainViewModelErrorLoggingTests.cs`, `MainViewModelOpenRouterPkceTests.cs`, `MainViewModelProfileContextMenuTests.cs`, `MainViewModelProfileSearchTests.cs`, `MainViewModelRecentProfilesTests.cs`, `MainViewModelSettingsTests.cs`, `MainViewModelUpdateAsyncBranchTests.cs`, `MainViewModelUpdateTests.cs`, `GoogleAutoLoginViewModelAsyncBranchTests.cs`, `GoogleAutoLoginViewModelTests.cs` | `ViewModels/` | `RouterPlus.Core.Tests.ViewModels` |
| `ProviderApiKeyStateTests.cs`, `ProviderCardQuotaTests.cs`, `ProviderCatalogTests.cs`, `ProviderConnectionCalculationsTests.cs`, `ProviderConnectionStatusTests.cs`, `ProviderDefinitionTests.cs`, `ProviderDisplayStatusTests.cs`, `ProviderHealthStateTests.cs`, `ProviderModelBranchTests.cs`, `ProviderQuotaAdditionalTests.cs`, `ProfileConnectionMatcherTests.cs`, `QuotaAutoDisablePolicyTests.cs`, `QuotaAutoDisablePolicyAdditionalTests.cs`, `QuotaPollingServiceTests.cs`, `UsageInferenceIntegrationTests.cs`, `UsageInferenceServiceTests.cs` | `Providers/` | `RouterPlus.Core.Tests.Providers` |
| `PriorityCalculatorTests.cs`, `PriorityCalculatorAdditionalTests.cs` | `Providers/` | `RouterPlus.Core.Tests.Providers` |
| `SelfUpdateServiceTests.cs`, `UpdatePackageValidationTests.cs`, `ReleaseVersionTests.cs` | `Updates/` | `RouterPlus.Core.Tests.Updates` |
| `SettingsStoreTests.cs`, `ThemeTemplateTests.cs`, `ToastNotificationLayoutTests.cs` | `Settings/` | `RouterPlus.Core.Tests.Settings` |

- [ ] **Step 1: Di chuyển file**

Dùng `git mv` để giữ lịch sử. Ví dụ cho nhóm Chrome:

```bash
cd tests/RouterPlus.Core.Tests
git mv Unit/ChromeManagedSessionTests.cs Unit/ChromeProfileCatalogTests.cs \
       Unit/ChromeProfileDeleterTests.cs Unit/ChromeProfileFilterTests.cs \
       Unit/ChromeProfileParserTests.cs Unit/ChromeProfileProvisionerTests.cs Chrome/
git mv Unit/Usings.cs Usings.cs
```

Lặp lại cho từng nhóm trong bảng. Các file còn lại trong `Unit/` (Observability, Models, Integration đã có thư mục sẵn) giữ nguyên.

- [ ] **Step 2: Sửa namespace**

Với mỗi file vừa di chuyển, đổi `namespace RouterPlus.Core.Tests;` → `namespace RouterPlus.Core.Tests.<Folder>;`

Kiểm tra còn sót:
```bash
grep -rn "^namespace RouterPlus.Core.Tests;" tests/RouterPlus.Core.Tests/Chrome \
  tests/RouterPlus.Core.Tests/Security tests/RouterPlus.Core.Tests/Api \
  tests/RouterPlus.Core.Tests/ViewModels tests/RouterPlus.Core.Tests/Providers \
  tests/RouterPlus.Core.Tests/Updates tests/RouterPlus.Core.Tests/Settings
```
Expected: không có kết quả.

- [ ] **Step 3: Build**

Run: `dotnet build tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj -v q --nologo`
Expected: `Build succeeded` — 0 error.

- [ ] **Step 4: Test**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --no-build --framework net8.0-windows`
Expected: `Passed! - Failed: 0, Passed: 985` (số lượng giữ nguyên, không đổi).

- [ ] **Step 5: Commit**

```bash
git add -A tests/RouterPlus.Core.Tests
git commit -m "test: reorganize Core.Tests into domain folders"
```

---

### Task 3: Tạo TestHelpers dùng chung cho `RouterPlus.Core.Tests`

**Files:**
- Modify: `tests/RouterPlus.Core.Tests/TestHelpers/TestData.cs`
- Create: `tests/RouterPlus.Core.Tests/TestHelpers/Mocks.cs`
- Modify: `tests/RouterPlus.Core.Tests/Providers/QuotaAutoDisablePolicyTests.cs`

**Interfaces:**
- Produces: `TestData.CreateConnection(ProviderKind provider, IReadOnlyList<ProviderQuota>? quotas = null, long? usageCount = null, long? limitCount = null) → ProviderConnection`
- Produces: `TestData.CreateProfile(string name, string directoryName, string userDataDirectory) → ChromeProfile`
- Produces: `TestData.CreateTempDirectory() → string`

- [ ] **Step 1: Mở rộng `TestData.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.TestHelpers;

public static class TestData
{
    public static ProviderConnection CreateConnection(
        ProviderKind provider,
        IReadOnlyList<ProviderQuota>? quotas = null,
        long? usageCount = null,
        long? limitCount = null) =>
        new(
            "synthetic-connection",
            provider,
            "Synthetic",
            1,
            true,
            UsageCount: usageCount,
            LimitCount: limitCount,
            Quotas: quotas);

    public static ChromeProfile CreateProfile(
        string name = "Synthetic",
        string directoryName = "Default",
        string? userDataDirectory = null)
    {
        var root = userDataDirectory ?? CreateTempDirectory();
        return new ChromeProfile(
            ChromeProfile.CreateId(root, directoryName),
            name,
            directoryName,
            root,
            false);
    }

    public static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rp-core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
```

- [ ] **Step 2: Tạo `Mocks.cs`**

`ProviderConnectionVaultStore` là class `sealed` không có interface, nên test phải dùng instance thật trỏ vào thư mục tạm. `Mocks` cung cấp factory thay vì `Mock<T>`:

```csharp
using System.IO;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.TestHelpers;

public static class Mocks
{
    /// <summary>Provider connection vault backed by a temp directory (no interface exists to mock).</summary>
    public static ProviderConnectionVaultStore CreateProviderVault(string directory) =>
        new(Path.Combine(directory, "provider-connections.vault"));

    /// <summary>Google account vault backed by a temp directory.</summary>
    public static GoogleAccountVaultStore CreateGoogleVault(string directory) =>
        new(new GoogleAccountVaultPaths(directory));

    /// <summary>Settings store backed by a temp directory.</summary>
    public static SettingsStore CreateSettingsStore(string directory) =>
        new(Path.Combine(directory, "settings.json"));
}
```

- [ ] **Step 3: Dùng `TestData` trong `QuotaAutoDisablePolicyTests.cs`**

Xoá helper `CreateConnection` private ở cuối file (dòng 265-278) và thêm `using RouterPlus.Core.Tests.TestHelpers;`. Thay mọi lời gọi `CreateConnection(` bằng `TestData.CreateConnection(`.

- [ ] **Step 4: Build + test**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~QuotaAutoDisablePolicyTests"`
Expected: `Passed! - Failed: 0, Passed: 23, Total: 23`.

- [ ] **Step 5: Commit**

```bash
git add tests/RouterPlus.Core.Tests/TestHelpers tests/RouterPlus.Core.Tests/Providers/QuotaAutoDisablePolicyTests.cs
git commit -m "test: add shared TestData and Mocks helpers for Core.Tests"
```

---

### Task 4: Sửa 12 test đỏ trong `RouterPlus.App.Tests`

**Files:**
- Modify: `tests/RouterPlus.App.Tests/ViewModels/CredentialsManagerViewModelTests.cs`
- Modify: `tests/RouterPlus.App.Tests/ViewModels/AsyncRelayCommandTests.cs`
- Modify: `tests/RouterPlus.App.Tests/ViewModels/AsyncRelayCommandDeterministicTests.cs`

**Nguyên nhân đã xác định:**

(a) **10 lỗi `Assert.Contains` trong `CredentialsManagerViewModelTests`** — test kỳ vọng thông báo cũ, nhưng code hiện tại sinh thông báo khác. Bằng chứng thực tế từ lần chạy test:

| Dòng | Kỳ vọng (cũ) | Thực tế sinh ra |
|---|---|---|
| 2249 | `"No Codex credentials"` | `"Google vault locked. Unlock to..."` |
| 1951 | `"No credentials to check"` | `"Google vault locked. Unlock to..."` |
| 2028 | `"No credentials to check"` | `"Google vault locked. Unlock to..."` |
| 2042 | `"No configured accounts"` | `"Google vault locked. Unlock to..."` |
| 2617 | `"No Kiro credentials"` | `"Google vault locked. Unlock to..."` |
| 3477 | `"No credentials to login with"` | `"Google vault locked. Unlock to..."` |
| 2960 | `"synthetic network error"` | `"Batch login completed: 0 succe..."` |
| 3007 | `"not found"` | `"Batch login completed: 0 succe..."` |
| 3028 | `"Batch login completed"` | `"Vault not unlocked. Please unl..."` |
| 3073 | `"Batch login completed"` | `"Vault not unlocked. Please unl..."` |
| 3095 | `"Batch login cancelled"` | `"Vault not unlocked. Please unl..."` |
| 3536 | `"synthetic manual step"` | `"⚠ Test Profile: Manual interve..."` |
| 2075 | `"1 healthy"` | `"Health check completed: 0 heal..."` |

Nhóm `"Google vault locked"` xuất hiện vì test chưa mở khoá vault Google trước khi gọi lệnh — đây là **test thiếu setup**, không phải code sai. Nhóm `"Vault not unlocked"` tương tự. Nhóm `"0 succeeded"` là do vault chưa có credential nào được seed.

(b) **2 lỗi `Assert.False` trong test `CanExecute`:**
- `AsyncRelayCommandDeterministicTests.cs:115` — `GenericExecute_SkipsExecutionForWrongParameterType`
- `AsyncRelayCommandTests.cs:220` — `CanExecute_ReturnsFalseForWrongParameterType`

Cần đọc `AsyncRelayCommand<T>.CanExecute` thật để xác định ngữ nghĩa đúng trước khi sửa assertion.

- [ ] **Step 1: Đọc `AsyncRelayCommand<T>.CanExecute`**

Run: `grep -n "CanExecute" src/RouterPlus.App/ViewModels/AsyncRelayCommand.cs`

Xác định: `CanExecute(object?)` trả gì khi tham số sai kiểu. Nếu implementation thật sự trả `true` cho kiểu sai, thì **implementation là bug** → dừng lại, báo người dùng, không sửa test cho khớp. Nếu trả `false` thì test đang gọi sai overload → sửa test.

- [ ] **Step 2: Xử lý nhóm `AsyncRelayCommand`**

Sửa assertion cho khớp ngữ nghĩa đã xác minh ở Step 1, hoặc báo bug nếu implementation sai.

Run: `dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj --filter "FullyQualifiedName~AsyncRelayCommand"`
Expected: `Passed! - Failed: 0`.

- [ ] **Step 3: Xử lý nhóm `CredentialsManagerViewModelTests` — thiếu setup vault**

Với mỗi test trong bảng có thực tế là `"Google vault locked. Unlock to..."` hoặc `"Vault not unlocked. Please unl..."`, thêm bước mở khoá vault trong phần Arrange trước khi Act. Đọc một test đang xanh trong cùng file (ví dụ `RemoveProviderCommands_remove_the_selected_connection_for_each_provider` ở dòng 333) để lấy đúng cách mở khoá vault mà file này đang dùng, rồi áp dụng cùng cách.

- [ ] **Step 4: Xử lý nhóm assert vào chuỗi thông báo**

Với các test còn lại, so chuỗi kỳ vọng với chuỗi thực tế trong output test. Nếu chuỗi thực tế **diễn tả đúng cùng một hành vi** thì cập nhật kỳ vọng; nếu hành vi đã khác thì dừng lại và báo người dùng.

- [ ] **Step 5: Chạy toàn bộ App.Tests**

Run: `dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj`
Expected: `Passed! - Failed: 0`.

- [ ] **Step 6: Commit**

```bash
git add tests/RouterPlus.App.Tests
git commit -m "test: fix outdated CredentialsManager and AsyncRelayCommand assertions"
```

---

### Task 5: Xác minh toàn bộ và commit

- [ ] **Step 1: Build toàn solution**

Run: `dotnet build RouterPlus.sln -v q --nologo`
Expected: `Build succeeded` — 0 Warning, 0 Error.

Nếu tên solution khác, dùng `ls *.sln`.

- [ ] **Step 2: Chạy toàn bộ test**

Run: `dotnet test --no-build`
Expected:
- `RouterPlus.Core.Tests` — Failed: 0
- `RouterPlus.Infrastructure.Tests` — Failed: 0, Passed: 532
- `RouterPlus.Updater.Tests` — Failed: 0, Passed: 57
- `RouterPlus.App.Tests` — Failed: 0
- `RouterPlus.App.E2E` — Failed: 0, Passed: 22

- [ ] **Step 3: Cập nhật spec**

Ghi lại số liệu cuối cùng vào `docs/superpowers/specs/2026-09-11-refactor-test-suite-design.md` (mục Mục tiêu), rồi commit:

```bash
git add docs/superpowers/specs/2026-09-11-refactor-test-suite-design.md
git commit -m "docs: record final test suite state after refactor"
```

---

## Completion Criteria

- [ ] `dotnet build` toàn solution: 0 error.
- [ ] `dotnet test` toàn solution: 0 failed trên cả 5 project.
- [ ] Không còn file test nào nằm ở `tests/RouterPlus.Core.Tests/Unit/` (thư mục này không còn tồn tại hoặc rỗng).
- [ ] Không file test nào còn namespace `RouterPlus.Core.Tests;` nếu nó nằm trong thư mục con.
- [ ] `tests/RouterPlus.Core.Tests/TestHelpers/` chứa `TestData.cs` và `Mocks.cs`.
- [ ] `git diff --stat src/` rỗng — không có thay đổi nào trong `src/`.
