# Developer Harness FlaUI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans (acceptable) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an external FlaUI-based Windows UI harness that can build and launch RouterPlus in a deterministic local test environment, exercise the profile context menu with a real right-click, and collect actionable artifacts without user-supplied credentials or logs.

**Architecture:** Keep the harness outside production code in a dedicated `tests/RouterPlus.App.E2E` project plus a PowerShell runner under `tools/dev-harness`. The first slice uses a temporary fake Chrome User Data directory containing deterministic `Local State` profile metadata and a temporary settings file; it launches the existing app with a test-only injected settings/profile source rather than touching the user's real settings or Chrome. FlaUI UIA3 drives the native WPF window and verifies profile selection/context-menu items and timing. Failure artifacts (logs, screenshots, process output) are written to a run-specific directory and processes are always cleaned up.

**Tech Stack:** .NET 8 Windows, WPF, FlaUI 5.x (`FlaUI.Core`, `FlaUI.UIA3`), xUnit, PowerShell 5.1 runner, Windows UI Automation.

**Spec:** User-approved requirement: external Developer Harness for the development process, not an end-user app feature; selected FlaUI; deterministic/offline test data; no real credentials/services.

## Global Constraints

- Harness code stays outside `src/RouterPlus.App` production UI except for the minimum Debug/Test injection seam needed to select isolated settings/profile data.
- Never read or use real Chrome profiles, real vaults, real API keys, passwords, TOTP, OAuth tokens, cookies, or external provider services.
- Every run uses a unique temporary root and cleans up the launched RouterPlus process in `finally`.
- UI tests must identify controls by stable automation properties or visible labels, not fragile screen coordinates except for the right-click target's bounds.
- Right-click success is measured from the mouse action until the expected context-menu item is discoverable; include a configurable threshold and a failure screenshot.
- Existing unit/integration tests must remain green; do not change user-facing behavior.
- Use FlaUI 5.x because current documentation indicates .NET 8 support.

## File map

- Modify: `src/RouterPlus.App/RouterPlus.App.csproj` only if a Debug/Test compile constant or test-only injection seam is required; keep production dependencies unchanged.
- Modify: `src/RouterPlus.App/App.xaml.cs` to select a test-only composition root when the harness sets an explicit environment flag; normal startup remains unchanged.
- Modify: `src/RouterPlus.Infrastructure/Storage/SettingsStore.cs` only if a process-scoped explicit path is required; preserve the existing default path for normal users.
- Modify: `src/RouterPlus.App/MainWindow.xaml` only if stable `AutomationProperties.AutomationId` values are required.
- Create: `tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj` — Windows-only test project referencing the app and FlaUI packages.
- Create: `tests/RouterPlus.App.E2E/TestEnvironment.cs` — temporary root, deterministic fake profile data, fake app configuration, artifact directory, and cleanup.
- Create: `tests/RouterPlus.App.E2E/RouterPlusProcess.cs` — launch/wait/terminate RouterPlus Debug executable.
- Create: `tests/RouterPlus.App.E2E/MainWindowDriver.cs` — UIA3 lifecycle, profile list lookup, right-click, context-menu discovery, screenshot capture.
- Create: `tests/RouterPlus.App.E2E/ProfileContextMenuTests.cs` — right-click assertions and timing output.
- Create: `tools/dev-harness/run-debug-loop.ps1` — build, run E2E, preserve artifacts, return exit code.
- Modify: `RouterPlus.sln` — add E2E project under the tests solution folder/configurations.
- Create: `docs/developer-harness.md` — command and offline-data guarantees.

### Task 1: Add a test-only composition seam

**Files:**
- Modify: `src/RouterPlus.App/App.xaml.cs`
- Modify: `src/RouterPlus.App/RouterPlus.App.csproj` only if needed
- Modify: `src/RouterPlus.Infrastructure/Storage/SettingsStore.cs` only if needed
- Test: `tests/RouterPlus.Core.Tests/SettingsStoreTests.cs` or a new focused test

**Interfaces:**
- Preserve normal `new SettingsStore()` behavior and the current LocalApplicationData settings path.
- Add a Debug/Test-only environment flag such as `ROUTERPLUS_HARNESS=1` and path variable `ROUTERPLUS_HARNESS_ROOT` read by the app composition root.
- The harness composition root must construct `SettingsStore(<root>\settings.json)` and a deterministic profile source/installation without invoking real Chrome discovery, network API, or vault services.
- If the existing `MainViewModel` constructor cannot receive the profile source, introduce the smallest internal factory/delegate seam; do not put test logic into release behavior.

- [ ] **Step 1: Write a failing composition test or seam-level test**

```csharp
[Fact]
public async Task SettingsStore_uses_explicit_test_path_without_touching_default_path()
{
    var path = Path.Combine(Path.GetTempPath(), $"routerplus-settings-{Guid.NewGuid():N}.json");
    try
    {
        var store = new SettingsStore(path);
        await store.SaveAsync(new RouterSettings(DashboardBaseUrl: "http://127.0.0.1:20128", UseLightTheme: true));
        var loaded = await store.LoadAsync();
        Assert.Equal("http://127.0.0.1:20128", loaded.DashboardBaseUrl);
    }
    finally
    {
        File.Delete(path);
    }
}
```

- [ ] **Step 2: Verify the existing explicit-path constructor**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj -c Debug --filter FullyQualifiedName~SettingsStore`
Expected: existing settings tests pass; `SettingsStore(string? filePath = null)` already supports isolated paths.

- [ ] **Step 3: Implement the narrow harness composition path**

When `ROUTERPLUS_HARNESS=1`, `App.OnStartup` must use only synthetic settings and deterministic fake profile data. Keep normal startup exactly as-is when the variable is absent. The harness path must not call `ChromeLocator.Find`, `ChromeProfileReader.Read` on real directories, `SettingsStore.Load()` on the user path, real `RouterApiClient`, or real vault services.

- [ ] **Step 4: Re-run seam tests and normal startup tests**

Run: `dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj -c Debug --filter FullyQualifiedName~SettingsStore`
Expected: PASS; normal default path behavior unchanged.

### Task 2: Scaffold the FlaUI E2E project

**Files:**
- Create: `tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj`
- Modify: `RouterPlus.sln`

**Interfaces:**
- Test project targets `net8.0-windows`, is non-packable, and references `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `FlaUI.Core`, and `FlaUI.UIA3`.
- Project references the app project so the harness can locate the Debug executable, but launches the executable as a separate process.

- [ ] **Step 1: Add the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FlaUI.Core" Version="5.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="5.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\RouterPlus.App\RouterPlus.App.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the project to the solution**

Run: `dotnet sln RouterPlus.sln add tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj`
Expected: project appears under the tests solution folder/configurations.

- [ ] **Step 3: Restore/build the empty harness project**

Run: `dotnet build tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Debug`
Expected: PASS before UI test code is added.

### Task 3: Build deterministic test environment and process lifecycle

**Files:**
- Create: `tests/RouterPlus.App.E2E/TestEnvironment.cs`
- Create: `tests/RouterPlus.App.E2E/RouterPlusProcess.cs`

**Interfaces:**
- `TestEnvironment.CreateAsync()` returns an `IAsyncDisposable` environment with `RootPath`, `SettingsPath`, `ChromeUserDataPath`, and `ArtifactPath`.
- Seed synthetic data only: profile names `Harness Alpha` and `Harness Beta`, and a harness composition flag. Do not pretend a fake `chrome.exe` is usable; the app must use the harness profile source rather than real Chrome discovery.
- `RouterPlusProcess.StartAsync(TestEnvironment environment)` launches `src/RouterPlus.App/bin/Debug/net8.0-windows/RouterPlus.exe`, waits for a nonzero main window handle, and exposes process/artifact paths.
- Cleanup terminates only the process started by the test and deletes only the unique temporary root.

- [ ] **Step 1: Write an environment test**

```csharp
[Fact]
public async Task Create_seeds_two_profiles_and_isolated_root()
{
    await using var environment = await TestEnvironment.CreateAsync();
    Assert.True(Directory.Exists(environment.RootPath));
    Assert.True(File.Exists(environment.HarnessManifestPath));
    Assert.Contains("Harness Alpha", await File.ReadAllTextAsync(environment.HarnessManifestPath));
}
```

- [ ] **Step 2: Implement deterministic seeding**

Create a unique root, write a small manifest containing the two synthetic profile records and no secrets, and create `artifacts` beneath it. Do not touch the user's LocalApplicationData or Chrome directories.

- [ ] **Step 3: Implement process launch and cleanup**

Set `ROUTERPLUS_HARNESS=1` and `ROUTERPLUS_HARNESS_ROOT=<root>` only in the child process environment. Use FlaUI's documented pattern:

```csharp
var app = FlaUI.Core.Application.Launch(executablePath);
app.WaitWhileMainHandleIsMissing();
using var automation = new UIA3Automation();
var window = app.GetMainWindow(automation);
```

Wait for the title `9Router Profile Tool`; on failure save process metadata and the child's debug output under `ArtifactPath`.

- [ ] **Step 4: Run a startup smoke test**

Run: `dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Debug --filter FullyQualifiedName~StartupSmoke`
Expected: app window is found and the child process is cleaned up.

### Task 4: Implement the native WPF main-window driver

**Files:**
- Create: `tests/RouterPlus.App.E2E/MainWindowDriver.cs`
- Modify: `src/RouterPlus.App/MainWindow.xaml` only if stable `AutomationProperties.AutomationId` values are required

**Interfaces:**
- `FindProfileList()` returns the WPF list control using AutomationId/name or a stable descendant query.
- `FindProfileItem(string name)` returns the list item containing the seeded profile name.
- `RightClickProfile(string name)` moves the mouse to the item's bounds and calls `Mouse.Click(MouseButton.Right)`.
- `WaitForContextMenu(TimeSpan timeout)` polls the UIA tree for `Mở thư mục profile` and returns appearance duration.
- `CaptureFailure(string label)` saves a FlaUI screenshot and UIA tree dump under the run artifact path.

- [ ] **Step 1: Add the failing right-click test**

```csharp
[Fact]
public async Task Right_click_profile_opens_expected_context_menu()
{
    await using var environment = await TestEnvironment.CreateAsync();
    await using var app = await RouterPlusProcess.StartAsync(environment);
    using var driver = new MainWindowDriver(app);
    driver.RightClickProfile("Harness Alpha");
    var elapsed = driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
    Assert.InRange(elapsed, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
}
```

- [ ] **Step 2: Implement robust element lookup**

Prefer `AutomationId`, then accessible name/text, then a bounded descendant search. If existing XAML has no stable identifiers, add only invisible automation IDs to `ProfileList` and the context-menu host/item template.

- [ ] **Step 3: Implement real right-click and menu polling**

Use FlaUI's `Mouse.Click(MouseButton.Right)` at the profile item's center, not a direct command invocation. Poll until the menu item is visible/enabled; capture timestamps and avoid fixed sleeps except for a short polling interval.

- [ ] **Step 4: Add failure artifact capture**

On assertion failure, write `right-click-failure.png`, a UI tree text dump, and the artifact path to test output. Always dismiss the context menu and dispose the process in `finally`.

### Task 5: Add the right-click suite and runner

**Files:**
- Create: `tests/RouterPlus.App.E2E/ProfileContextMenuTests.cs`
- Create: `tools/dev-harness/run-debug-loop.ps1`
- Modify: `.gitignore` only if artifact output needs an ignored directory

**Interfaces:**
- Tests cover menu opening, expected items, target selection, repeated right-clicks, and timing threshold.
- Runner accepts `-Filter`, `-Configuration` (default `Debug`), and `-KeepArtifacts`; builds the app and E2E project, runs tests, and returns the test exit code.

- [ ] **Step 1: Add menu assertions**

```csharp
var expected = new[]
{
    "Đăng nhập Google bằng Chrome",
    "Tự động đăng nhập Google",
    "Mở thư mục profile",
    "Sao chép tên profile",
    "Xóa profile…"
};
Assert.All(expected, header => Assert.True(driver.ContextMenuContains(header), header));
```

- [ ] **Step 2: Add repeatability coverage**

Right-click `Harness Alpha`, dismiss; right-click `Harness Beta`, assert the menu appears again and selected-profile UI reflects `Harness Beta`. Repeat three times to catch stale ContextMenu/selection issues.

- [ ] **Step 3: Add the runner**

```powershell
param(
  [string]$Configuration = "Debug",
  [string]$Filter = "FullyQualifiedName~ProfileContextMenuTests",
  [switch]$KeepArtifacts
)
$ErrorActionPreference = "Stop"
dotnet build .\src\RouterPlus.App\RouterPlus.App.csproj -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test .\tests\RouterPlus.App.E2E\RouterPlus.App.E2E.csproj -c $Configuration --no-restore --filter $Filter
$exitCode = $LASTEXITCODE
if (-not $KeepArtifacts) { Remove-Item .\artifacts\dev-harness -Recurse -Force -ErrorAction SilentlyContinue }
exit $exitCode
```

Keep actual artifacts under the unique `TestEnvironment` root so failures remain available when `-KeepArtifacts` is used.

- [ ] **Step 4: Run the complete harness**

Run: `powershell -ExecutionPolicy Bypass -File .\tools\dev-harness\run-debug-loop.ps1 -KeepArtifacts`
Expected: build succeeds, native WPF right-click tests pass, and no RouterPlus process remains.

### Task 6: Verify integration and document operation

**Files:**
- Create: `docs/developer-harness.md`
- Test: existing solution tests plus E2E runner

- [ ] **Step 1: Run existing tests**

Run: `dotnet test RouterPlus.sln -c Debug --no-restore`
Expected: all existing tests pass.

- [ ] **Step 2: Run the harness and inspect artifacts**

Run: `powershell -ExecutionPolicy Bypass -File .\tools\dev-harness\run-debug-loop.ps1 -KeepArtifacts`
Check: fake data is isolated; no real user profile path appears in output; screenshots/UI tree are retained when requested or on failure.

- [ ] **Step 3: Run Release build**

Run: `dotnet build RouterPlus.sln -c Release --no-restore`
Expected: PASS; harness remains tooling and is not shipped in the app.

- [ ] **Step 4: Document the developer command**

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\dev-harness\run-debug-loop.ps1 -KeepArtifacts
```

Explain that it runs offline with seeded fake profiles, tests native right-click behavior, and never uses real credentials.

- [ ] **Step 5: Commit the harness as a focused change**

```bash
git add RouterPlus.sln tests/RouterPlus.App.E2E tools/dev-harness docs/developer-harness.md
git commit -m "test: add FlaUI developer harness for profile context menu"
```
