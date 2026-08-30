# 9Router Plus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hoàn thiện 9Router Plus thành local, profile-centric control center với vault an toàn, login orchestration có thể kiểm thử, Credentials Manager đầy đủ và CI/E2E deterministic.

**Architecture:** Giữ layering hiện tại: `RouterPlus.Core` chứa domain/policy; `RouterPlus.Infrastructure` chứa filesystem, DPAPI, HTTP, Chrome/CDP và orchestration; `RouterPlus.App` chứa WPF Views/ViewModels; `RouterPlus.Updater` giữ update transaction. Triển khai theo contract cleanup → storage/orchestration → UI → E2E/CI, mỗi phase có test cycle độc lập và không ghi đè các thay đổi chưa commit.

**Tech Stack:** C#/.NET 8, WPF/WinForms, xUnit, Microsoft.NET.Test.Sdk, coverlet, Moq, FlaUI.Core 5.0.0, FlaUI.UIA3 5.0.0, Windows DPAPI, AES-GCM/PBKDF2, GitHub Actions Windows runners.

**Spec:** `docs/superpowers/specs/2026-08-30-9router-plus-technical-spec.md`

## Global Constraints

- **Profile-first:** mọi provider operation phải có profile context.
- **Stable identity:** dùng `ChromeProfile.Id` cho Google vault và các association mới; display name chỉ là presentation.
- **Secure by default:** secret bị che, log bị làm sạch, session được dispose.
- **Deterministic by default:** test không phụ thuộc internet, tài khoản thật hoặc Chrome thật nếu không cần.
- **Không tự động vượt CAPTCHA, MFA hoặc cơ chế bảo vệ của provider.**
- **Không lưu credentials plaintext.**
- **Không retry login vô hạn.**
- **v1 không có dashboard URL hoặc dashboard screen dành cho người dùng.** Nếu Router API cần endpoint, đó là internal service configuration.
- **Không force-close browser người dùng.** Chỉ process do app launch và own mới được app cleanup.
- **Không ghi password, TOTP, API key, token, cookie hoặc authorization header vào log/artifact.**
- **Mọi thay đổi lưu trữ quan trọng phải atomic hoặc có rollback.**
- **Live E2E chỉ chạy khi có `ROUTERPLUS_LIVE_E2E=1`.**

## Current-state review

Đặc tả phù hợp với hướng profile-first/local-only nhưng mô tả target state, chưa phải trạng thái hiện tại. Các gap đã đối chiếu:

1. `SettingsPersistenceJourneyTests` vẫn yêu cầu dashboard URL dù v1 đã chốt không có user-facing dashboard URL.
2. `CredentialsManagerDialog.xaml.cs` còn `Feature coming soon` cho add/edit và bốn provider configuration actions.
3. `CredentialsManagerViewModel.BatchLoginAsync` và main batch path còn fixed delays; Credentials Manager chưa gọi real orchestrator.
4. `ProviderConnectionVaultStore` biến lỗi load thành empty dictionary và ghi trực tiếp vào live file; chưa fail-closed/atomic đầy đủ.
5. `AutoLoginOrchestrator` tạo concrete automation trong method, result chỉ có `Success`, `AuthMethod?`, `ErrorMessage`, và fallback chưa phân biệt cancellation/timeout/manual.
6. `ChromeLauncher` có WM_CLOSE/process kill khi dùng original profile; điều này trái contract không force-close.
7. `GoogleLoginCdpBrowser` diagnostics có thể ghi URL/page-derived content/field values và còn coordinate-based clicks.
8. Credentials Manager dùng profile name/email ở một số lookup/removal path thay vì stable profile identity.
9. CI hiện chạy `dotnet test RouterPlus.sln`, trộn desktop E2E vào fast lane; E2E launcher còn hardcode Debug app path.
10. Working tree đã có thay đổi chưa commit ở Credentials Manager/E2E. Executor phải review diff và không ghi đè chúng.

---

## Phase 0 — Baseline and contract cleanup

### Task 0.1: Freeze a fresh baseline

**Files:**
- Read: `RouterPlus.sln`, `.github/workflows/ci.yml`, all `tests/*/*.csproj`.
- Optional report: `docs/TEST-COVERAGE-REPORT.md` only if the repository already uses this report.

**Interfaces:** Produces a dated baseline of build/test outcomes; no production behavior changes.

- [ ] Run the non-E2E projects separately:

```powershell
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --configuration Debug
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --configuration Debug
dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj --configuration Debug
dotnet test tests/RouterPlus.Updater.Tests/RouterPlus.Updater.Tests.csproj --configuration Debug
```

- [ ] Run the desktop E2E project separately and preserve TRX output.
- [ ] Record every failure as contract mismatch, product bug, harness bug or timing issue; do not hide baseline failures.
- [ ] Inspect `git diff -- src/RouterPlus.App tests/RouterPlus.App.E2E` before any edit and preserve the existing user changes.

### Task 0.2: Reconcile stale dashboard URL tests

**Files:**
- Modify: `tests/RouterPlus.App.E2E/SettingsPersistenceJourneyTests.cs`.
- Modify: `tests/RouterPlus.App.E2E/AppDriver.cs` only to remove now-unused dashboard helpers.
- Test: `tests/RouterPlus.Core.Tests/SettingsStoreTests.cs`, `tests/RouterPlus.App.Tests/*Settings*`.

**Interfaces:** Keeps dashboard endpoint internal to the service layer; no user-facing dashboard URL property or AutomationId.

- [ ] Delete `Invalid_dashboard_url_is_visible_and_save_is_disabled` and `Dashboard_url_survives_save_and_application_restart`; do not replace them with dashboard UI tests.
- [ ] Add/retain supported settings journeys for actual controls such as Chrome path, theme, font scale and window state.
- [ ] Add a migration test for an old settings payload containing synthetic `DashboardBaseUrl`; verify it is ignored/safely migrated and never rendered.
- [ ] If `RouterApiClient` still needs an endpoint, inject a validated internal endpoint through composition; do not expose it in `RouterSettings` UI.
- [ ] Verify with:

```powershell
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~Settings"
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj --filter "FullyQualifiedName~Settings"
```

### Task 0.3: Diagnose `CredentialsManagerButton` discovery failure

**Files:**
- Modify only as diagnosis requires: `tests/RouterPlus.App.E2E/VaultVisibilityTests.cs`, `TestEnvironment.cs`, `AppProcess.cs`, `E2EInstrumentation.cs`.
- Read: `src/RouterPlus.App/MainWindow.xaml`, `MainWindow.xaml.cs`.

**Interfaces:** Produces a stable locked-vault journey using the actual main-window composition contract.

- [ ] Capture the startup window title and AutomationId tree before changing selectors.
- [ ] Verify harness startup reaches the main window and does not show a wizard/other modal first.
- [ ] If the button is intentionally absent, update the test to the supported command/AutomationId rather than adding a duplicate UI path.
- [ ] Replace fixed state waits with a five-second deadline polling helper and include a control-tree diagnostic on timeout.
- [ ] Run the two visibility tests in isolation, then the full E2E project.

**Commit boundary:** `test(contract): classify baseline and remove stale dashboard journeys` after Tasks 0.1–0.3 are green or explicitly documented as baseline failures.

---

## Phase 1 — Storage and security reliability

### Task 1.1: Make provider vault fail-closed and atomic

**Files:**
- Modify: `src/RouterPlus.Infrastructure/Security/ProviderConnectionVaultStore.cs`.
- Test: `tests/RouterPlus.Infrastructure.Tests/ProviderConnectionVaultStoreTests.cs`.

**Interfaces:** Preserve `GetConnectionAsync`, `SaveConnectionAsync`, `RemoveConnectionAsync`, `HasCredentialsAsync`, `GetProfileConnectionsAsync`, and `Dispose`. Missing file may mean empty; malformed, unsupported or cryptographically invalid file must be observable failure, never silent empty state.

- [ ] Add failing tests for malformed JSON, invalid Base64/DPAPI data, unsupported provider data, partial file and interrupted write.
- [ ] Add a test saving two connections, injecting a temp/replace failure, and verifying the previous live file remains readable.
- [ ] Implement same-directory temp write, flush/close, atomic replace/move, and temp cleanup in `finally`.
- [ ] Hold one operation gate across the complete load-modify-save operation, not only while acquiring it.
- [ ] Keep load failure state distinct from “no connections”; do not overwrite a valid in-memory snapshot with empty data.
- [ ] Log only counts/provider kinds; never log secret fields.
- [ ] Run:

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ProviderConnectionVaultStore"
```

### Task 1.2: Complete Google vault integrity and recovery coverage

**Files:**
- Modify only if tests reveal a defect: `src/RouterPlus.Infrastructure/Security/GoogleAccountVaultStore.cs`.
- Test: existing vault tests under `tests/RouterPlus.Core.Tests` and `tests/RouterPlus.App.Tests`.

**Interfaces:** Preserve version-1 AES-GCM/PBKDF2 envelope and the existing public APIs: `CreateAsync`, `OpenAsync`, `TryOpenRememberedAsync`, `SaveAsync`, `ExportAsync`, `ImportAsync`.

- [ ] Add tests for wrong password, tampered ciphertext/nonce/tag, malformed envelope, unsupported version/KDF, invalid payload schema, atomic-save failure and remembered-key mismatch.
- [ ] Assert on-disk bytes do not contain synthetic email/password/TOTP markers.
- [ ] Validate/decrypt import completely before changing current state; test that failed import leaves current vault and remembered material unchanged.
- [ ] Test backup creation, replacement and rollback if replacement/cleanup fails.
- [ ] Test session disposal/use-after-dispose and stale remembered `VaultId` rejection.
- [ ] Map user-visible failures to safe generic categories without exposing cryptographic details.

### Task 1.3: Introduce redaction and diagnostics tests

**Files:**
- Create if no reusable helper exists: `src/RouterPlus.Infrastructure/Diagnostics/DiagnosticRedactor.cs`.
- Modify: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs`, and any shared CDP diagnostic helper.
- Create: `tests/RouterPlus.Infrastructure.Tests/DiagnosticRedactorTests.cs`.

**Interfaces:** `DiagnosticRedactor.Redact(string? input)` returns a safe diagnostic string; browser diagnostics expose booleans/counts and fixed categories, not page content.

- [ ] Add tests proving synthetic password, TOTP, API key, bearer token, cookie, query value, hash value and email value are absent from logs/artifacts.
- [ ] Replace URL query/hash, body text, visible labels, iframe source, input values, button samples, coordinates and DOM dumps with allowlisted state flags/counts.
- [ ] Keep diagnostics opt-in and ensure normal production flow does not depend on them.
- [ ] Run focused tests and inspect captured output for synthetic secret markers.

**Commit boundaries:**
- `fix(security): make provider vault fail closed and atomic` after Task 1.1.
- `test(security): cover vault integrity and diagnostics redaction` after Tasks 1.2–1.3.

---

## Phase 2 — Domain identity and login orchestration

### Task 2.1: Unify stable profile identity at application boundaries

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`.
- Modify if required: `src/RouterPlus.Core/Security/GoogleLoginCredential.cs`, `GoogleAccountVault.cs`.
- Test: `tests/RouterPlus.Core.Tests/GoogleAccountVaultTests.cs`, `tests/RouterPlus.App.Tests/ViewModels/CredentialsManagerVaultIntegrationTests.cs`.

**Interfaces:** Google records are keyed by `ChromeProfile.Id`; provider name-keyed compatibility is preserved until a separately approved provider migration. Display name remains UI-only.

- [ ] Add tests for same display name with distinct profile IDs, renamed display name with unchanged ID, lookup/upsert by ID and removal by ID.
- [ ] Update Credentials Manager lookup/save/remove to pass a stable profile key, not email or display name.
- [ ] Define compatibility mapping for existing name-keyed records before changing serialized data; do not silently merge ambiguous names.
- [ ] Remove-by-email tests must prove two profiles sharing an email are not deleted together.
- [ ] Run Core and App vault integration tests.

### Task 2.2: Make optional TOTP explicit

**Files:**
- Modify: `src/RouterPlus.Core/Security/GoogleLoginCredential.cs`, related serialization/vault model files.
- Modify callers: `GoogleAutoLoginViewModel`, Credentials Manager VM, state machine and test builders.
- Test: `GoogleLoginCredentialTests.cs`, vault round-trip tests and state-machine tests.

**Interfaces:** `TotpSecret` is nullable/optional; blank input normalizes to `null`; non-empty invalid values are rejected; no sentinel is persisted or submitted.

- [ ] Add failing tests for null/blank TOTP accepted, malformed non-empty TOTP rejected, round-trip omission and no-secret state-machine behavior.
- [ ] Update all constructors/callers and JSON contracts while retaining compatibility with valid existing version-1 data.
- [ ] If an old sentinel exists, read it only through a narrowly tested compatibility path and never write/use it operationally.
- [ ] Run all Core security tests and compile with warnings as errors.

### Task 2.3: Inject provider automation and typed outcomes

**Files:**
- Modify: `src/RouterPlus.Infrastructure/Services/AutoLoginOrchestrator.cs`.
- Create focused factory interfaces under `src/RouterPlus.Infrastructure/Services` or `Chrome`.
- Modify: `AutoLoginResult` model and callers.
- Test: `tests/RouterPlus.Infrastructure.Tests/AutoLoginOrchestratorTests.cs`.

**Interfaces:** Preserve `LoginAsync(string profileName, ProviderKind provider, Uri startUri, TimeSpan timeout, CancellationToken cancellationToken)`. Add an injected automation factory and a typed result containing terminal category, attempted methods, selected method and safe error/action text. Categories include `Success`, `NoCredentials`, `InvalidCredentials`, `ManualInterventionRequired`, `Cancelled`, `TimedOut`, `ProviderUnavailable`, `Failed`.

- [ ] Add failing tests for preferred OAuth/direct success, preferred failure with allowed fallback, no credentials, absent fallback, cancellation without fallback, timeout, provider unavailable, browser launch failure and CDP disposal.
- [ ] Inject vault/factory seams so tests do not construct Chrome/CDP or concrete automations.
- [ ] Use linked timeout/cancellation tokens; cancellation is terminal and is never retried as fallback.
- [ ] Allow fallback only for classified retryable failures and only when alternative credentials exist.
- [ ] Dispose vault/browser sessions in `finally`; keep secrets out of errors.
- [ ] Run focused orchestrator tests.

**Commit boundaries:**
- `fix(identity): use stable profile identity for Google records` after Task 2.1.
- `fix(credentials): make TOTP optional without sentinel values` after Task 2.2.
- `refactor(login): inject automation boundaries and explicit outcomes` after Task 2.3.

---

## Phase 3 — Safe Chrome/CDP automation

### Task 3.1: Remove force-close behavior from original-profile launch

**Files:**
- Modify: `src/RouterPlus.Infrastructure/Chrome/ChromeLauncher.cs`.
- Modify: `ChromeLauncherAdapter.cs`, `ChromeManagedSession.cs` only as required.
- Test: new `tests/RouterPlus.Infrastructure.Tests/ChromeLauncherTests.cs` and existing session tests.

**Interfaces:** `LaunchManagedAsync` keeps its current signature. Profile-in-use is a typed/actionable failure. Only a process launched and owned by the current managed session may be terminated on dispose.

- [ ] Add a failing fake process/inspection test proving original-profile launch never calls WM_CLOSE or `Process.Kill`.
- [ ] Add locked/in-use test returning “close the selected profile manually or choose the supported isolated mode” guidance without process details.
- [ ] Remove/retire `CloseProcessesUsingProfile` and `CloseVisibleBrowserWindows` after all references are migrated.
- [ ] Keep isolated mode as explicit policy only; do not silently switch the approved Google flow to another profile.
- [ ] Verify managed process cleanup and temporary directory cleanup remain best effort and do not hide launch failure.
- [ ] Run focused launcher/session tests and provider regressions.

### Task 3.2: Harden CDP endpoint and target identity

**Files:**
- Modify: `src/RouterPlus.Infrastructure/Chrome/ChromeCdpClient.cs`, `ChromeManagedSession.cs`.
- Test: `tests/RouterPlus.Infrastructure.Tests/ChromeManagedSessionTests.cs` or the existing Core test location if that is the repository convention.

**Interfaces:** CDP accepts loopback-only endpoint; one session-marked target is selected and pinned; target loss/replacement/disconnect is terminal.

- [ ] Add tests rejecting public/private-network/wildcard/non-loopback endpoints and invalid WebSocket scheme/host.
- [ ] Add tests for zero/multiple session-marked targets, pre-existing unmarked target exclusion and target closure.
- [ ] Verify every sensitive operation rechecks target ID and allowed Google origin.
- [ ] Keep random local port and session marker launch arguments; no remote CDP binding.
- [ ] Run focused transport/session tests.

### Task 3.3: Replace coordinate controls with semantic controls/manual handoff

**Files:**
- Modify: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs`.
- Test: `tests/RouterPlus.Infrastructure.Tests/GoogleLoginCdpBrowserTests.cs` and state-machine tests.

**Interfaces:** Browser adapter uses semantic selectors/roles; selector ambiguity or unsupported challenge returns `ManualInterventionRequired`; no coordinate mouse event or clipboard fallback.

- [ ] Add a fake-CDP test that fails if `Input.dispatchMouseEvent` is called during the approved Google flow.
- [ ] Replace coordinate clicks with semantic DOM operations through the existing Runtime/CDP path.
- [ ] Return manual intervention when a control cannot be identified safely; do not click uncertain controls.
- [ ] Assert fake request records contain method names and safe flags only, never field values.
- [ ] Run focused browser and state-machine tests.

**Commit boundaries:**
- `fix(chrome): stop force-closing original profiles` after Task 3.1.
- `fix(cdp): pin loopback target and use semantic controls` after Tasks 3.2–3.3.

---

## Phase 4 — Credentials Manager and main workspace behavior

### Task 4.1: Stabilize Credentials Manager initialization, lifecycle and row state

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`.
- Modify: `src/RouterPlus.App/Views/CredentialsManagerDialog.xaml` only for bindings/AutomationIds.
- Test: `tests/RouterPlus.App.Tests/ViewModels/GoogleAccountRowViewModelTests.cs`, `CredentialsManagerViewModelTests.cs`, `CredentialsManagerVaultIntegrationTests.cs`.

**Interfaces:** Existing row bindings remain compatible. Add an awaitable initialization task or explicit `LoadDataAsync` seam. `DisposeAsync` is idempotent.

- [ ] Add tests for locked/unlocked rows, edit/save/cancel transitions, visibility reset, validation, trimmed email, preserved password semantics, optional TOTP and stable-ID upsert.
- [ ] Ensure failed save leaves edit mode and row values intact; session/in-memory vault changes occur only after persistence succeeds.
- [ ] Ensure CanExecute/property notifications update after row selection/edit/unlock changes.
- [ ] Clear row secret fields and reset visibility on lock, close, remove and failed operation.
- [ ] Make title-bar close and button close share one awaited/idempotent cleanup path.
- [ ] Run focused App tests.

### Task 4.2: Implement real Google add/edit/remove dialog flow

**Files:**
- Modify: `src/RouterPlus.App/Views/CredentialsManagerDialog.xaml`, `.xaml.cs`.
- Modify: `CredentialsManagerViewModel.cs`.
- Test: App VM tests and `tests/RouterPlus.App.E2E/CredentialsManagerDialogTests.cs`.

**Interfaces:** Code-behind owns WPF window/PasswordBox/confirmation mechanics; ViewModel owns validation, state and persistence.

- [ ] Replace all Google `Feature coming soon` handlers with add/edit state and commands.
- [ ] New email draft starts from the profile display name only as a draft; save validation still rejects non-email text before persistence/browser work.
- [ ] Add explicit Save/Cancel controls and stable AutomationIds.
- [ ] Keep password/TOTP hidden by default; clear controls in `finally` and on close/lock/failure.
- [ ] Add remove confirmation and prove cancellation leaves vault unchanged.
- [ ] Add tests for add/edit valid/invalid, cancel, remove confirmed/cancelled, persistence after reopen and two profiles sharing email.
- [ ] Run the focused synthetic journey.

### Task 4.3: Implement provider configuration dialog

**Files:**
- Create: `src/RouterPlus.App/Views/ProviderConnectionConfigDialog.xaml`, `.xaml.cs`.
- Create: `src/RouterPlus.App/ViewModels/ProviderConnectionConfigViewModel.cs`.
- Modify: Credentials Manager XAML/code-behind/ViewModel.
- Test: `tests/RouterPlus.App.Tests/ViewModels/ProviderConnectionConfigViewModelTests.cs` and provider E2E journey.

**Interfaces:** VM accepts profile identity, `ProviderKind`, current `ProviderAuthConnection?` and Google account references; save calls `SaveConnectionAsync`; test connection does not mutate on failure.

- [ ] Add tests for auth method selection, Google account link, direct credential validation, save/cancel, test failure, remove and upsert.
- [ ] Use one shared dialog with provider catalog capabilities for Codex/Kiro/GitHub/OpenRouter; do not duplicate four event handlers.
- [ ] Use secure fields and safe statuses; never include direct credentials in accessibility names/logs.
- [ ] Add AutomationIds: `ProviderConnectionConfigDialog`, `ProviderAuthMethod`, `ProviderGoogleAccount`, `ProviderDirectEmail`, `ProviderDirectPassword`, `ProviderDirectTotp`, `ProviderSaveButton`, `ProviderCancelButton`, `ProviderTestButton`, `ProviderRemoveButton`.
- [ ] Replace all provider placeholders and run provider synthetic E2E.

### Task 4.4: Replace simulated batch login with orchestrator-backed workflow

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`, `MainViewModel.cs`.
- Modify/create result/state types under `src/RouterPlus.Core` if required.
- Test: `tests/RouterPlus.App.Tests/ViewModels/CredentialsManagerViewModelTests.cs` and batch-focused tests.

**Interfaces:** Inject a cancellation-aware single-login delegate/orchestrator. Batch rows expose `Success`, `Failed`, `Skipped`, `Cancelled`, `ManualInterventionRequired`; summary exposes counts.

- [ ] Add tests for empty selection, missing credentials/profile, sequential success, one failure, manual handoff, cancel before next row and cancel during current row.
- [ ] Remove all fixed production `Task.Delay` calls from batch behavior; state changes are driven by operation events/polling, not simulation.
- [ ] Own/dispose a batch `CancellationTokenSource`; reset `IsBatchLoginRunning` in every terminal path.
- [ ] Do not classify cancellation as generic failure; do not start fallback after cancellation.
- [ ] Ensure no concurrent login runs for one profile and no duplicate status overwrite.
- [ ] Run all App tests and grep production batch code for delay calls.

### Task 4.5: Align profile selection, health and supported settings

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs`, `ProfileRowViewModel.cs`.
- Modify: `src/RouterPlus.Core/Providers/ProviderHealthState.cs` and catalog only if needed.
- Modify: `src/RouterPlus.Infrastructure/Storage/RouterSettings.cs` and settings UI.
- Test: Main/settings tests and supported settings E2E.

**Interfaces:** Refresh preserves last known configured state; health states include `Checking` and `Unavailable`; selection clears when the selected stable ID disappears; no dashboard URL UI.

- [ ] Add tests for search/filter/status cycle, selection preservation/clearing, duplicate-free refresh, health success/error/timeout/quota and provider configuration preservation on network failure.
- [ ] Decide explicitly whether Ollama/Kimchi are v1 out-of-scope; hide consistently or define their complete behavior before enabling them.
- [ ] Add versioned settings fields only for approved v1 settings such as vault remember option, automation timeout and status-check policy; validate and persist them through `SettingsStore`.
- [ ] Remove dashboard URL from user-facing settings and leave any internal Router endpoint in service composition.
- [ ] Run MainViewModel/App settings tests and supported E2E.

**Commit boundaries:**
- `feat(credentials): implement Google add-edit-remove flow` after Tasks 4.1–4.2.
- `feat(credentials): add provider connection configuration dialog` after Task 4.3.
- `feat(login): route credentials batch through orchestrator` after Task 4.4.
- `fix(settings): remove dashboard URL from v1 UI contract` after Task 4.5.

---

## Phase 5 — Test infrastructure, E2E and CI

### Task 5.1: Add deterministic test doubles and artifact redaction

**Files:**
- Create/modify test-only helpers under `tests/RouterPlus.Core.Tests`, `tests/RouterPlus.Infrastructure.Tests`, `tests/RouterPlus.App.Tests`, `tests/RouterPlus.App.E2E`.

**Interfaces:** Scripted HTTP handler, fake login automation, fake Chrome launcher/session, isolated temp-vault fixture, deadline polling helper and redaction assertion helper.

- [ ] Builders create synthetic `ChromeProfile`, provider connection, credential and typed login result.
- [ ] Fakes support cancellation through `TaskCompletionSource` and record calls without secret values.
- [ ] Polling helper always has a deadline and includes safe control/state diagnostics on timeout.
- [ ] Redaction assertion fails if synthetic markers such as `synthetic-password`, `synthetic-totp` or `Bearer synthetic-token` appear in output.
- [ ] Verify helpers never use real user paths, real accounts or external network.

### Task 5.2: Stabilize critical synthetic E2E journeys

**Files:**
- Modify: `tests/RouterPlus.App.E2E/TestEnvironment.cs`, `AppProcess.cs`, `AppDriver.cs`, `E2EInstrumentation.cs`.
- Modify existing Credentials Manager, profile, settings and lifecycle journeys.
- Create provider configuration and batch journey files.

**Interfaces:** Harness supports no-vault, locked-vault, wrong-password, invalid-remembered-key, corrupt-vault and unlocked-vault fixtures. App path is configurable by build configuration, not hardcoded Debug.

- [ ] Cover startup, profile search/filter/open, supported settings, locked/unlocked vault, wrong password, remembered unlock, Google CRUD, provider configure/save/remove, single result states and batch states.
- [ ] Use AutomationIds and bounded polling; remove fixed waits except documented UI stabilization waits.
- [ ] Failure artifacts contain window/control IDs only and are checked for secret markers.
- [ ] Run each journey in isolation and the full synthetic suite three consecutive times.

### Task 5.3: Separate fast CI, Windows synthetic E2E and live validation

**Files:**
- Modify: `.github/workflows/ci.yml`.
- Create if useful: `.github/workflows/e2e-windows.yml`, `.github/workflows/live-e2e.yml`.
- Modify test project traits only if filters need them.

**Interfaces:** Fast lane runs build + Core/Infrastructure/App/Updater tests without desktop UI, Chrome or network. Synthetic lane runs on Windows desktop-capable runner. Live lane requires `ROUTERPLUS_LIVE_E2E=1` and secured secrets.

- [ ] Replace solution-wide test invocation in fast lane with explicit non-E2E projects.
- [ ] Build/publish the app before synthetic E2E and pass the resulting path/configuration to `AppProcess`.
- [ ] Upload TRX and failure snapshots/logs per lane; do not upload vault files or secret-bearing process dumps.
- [ ] Ensure desktop E2E is non-parallel on shared interactive runners.
- [ ] Make live tests fail closed when opt-in/configuration is absent.
- [ ] Validate YAML and run the four explicit non-E2E commands locally.

### Task 5.4: Complete updater/release safety coverage

**Files:**
- Test: `tests/RouterPlus.Updater.Tests/UpdateTransactionTests.cs`.
- Modify production only if a missing terminal path is demonstrated: `src/RouterPlus.Updater/UpdateTransaction.cs`.
- Use test-only filesystem/process/mutex seams.

**Interfaces:** Existing `IUpdateTransactionRuntime`, `IUpdateMutex` and `UpdateTransactionResult` remain stable.

- [ ] Add tests for invalid paths, mutex refusal, parent timeout, swap failure, health-check failure with successful rollback, rollback failure and cleanup.
- [ ] Assert filesystem contents after each terminal result, not just enum value.
- [ ] Run updater tests in Release configuration.

**Commit boundaries:**
- `test(e2e): stabilize synthetic critical journeys` after Tasks 5.1–5.2.
- `ci: separate fast, synthetic e2e and live lanes` after Task 5.3.
- `test(updater): cover rollback and recovery terminal states` after Task 5.4.

---

## Verification gates

After each production phase, run:

```powershell
dotnet build RouterPlus.sln --configuration Release
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --configuration Release --no-build
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --configuration Release --no-build
dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj --configuration Release --no-build
dotnet test tests/RouterPlus.Updater.Tests/RouterPlus.Updater.Tests.csproj --configuration Release --no-build
```

Before completion, run:

```powershell
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj --configuration Release
dotnet format RouterPlus.sln --verify-no-changes
scripts/release-preflight.ps1
git diff --check
git status --short
```

Final acceptance checks:

- No user-facing dashboard URL field or dashboard screen exists in v1.
- No required Credentials Manager action displays `Feature coming soon`.
- Production batch code has no simulation delay.
- Provider-vault corruption fails closed and successful writes are atomic.
- Original-profile launch never closes/kills unrelated Chrome processes.
- Orchestrator tests cover preferred/fallback/cancel/timeout/disposal with fakes.
- CDP flow uses loopback/pinned target/semantic controls and diagnostics contain no page secrets.
- Password/TOTP are hidden by default, cleared on lifecycle boundaries and absent from logs/artifacts.
- Fast CI does not run desktop/live E2E.
- Synthetic E2E passes on a Windows desktop runner.
- Live E2E is opt-in and fails closed without configuration.
- Existing unrelated working-tree changes are preserved and reviewed before commit.

## Spec coverage map

- Product/profile workspace: Tasks 2.1, 4.5, 5.2.
- Provider connections and health/quota: Tasks 4.3, 4.5.
- Credentials Manager: Tasks 4.1–4.4.
- Google login state machine and browser safety: Tasks 2.2–2.3, 3.1–3.3.
- Vault encryption, remembered unlock, import/export/recovery: Tasks 1.1–1.3, 4.1.
- Settings and dashboard decision: Tasks 0.2, 4.5.
- Backup/import/recovery: Task 1.2 and 4.1.
- Self-update: Task 5.4.
- Diagnostics/security: Task 1.3 and 3.2–3.3.
- Testability and CI lanes: Tasks 5.1–5.3.
- Acceptance and live validation: Tasks 5.2–5.3.

## Commit discipline

Use the commit boundaries above. Before each commit, inspect the staged diff and ensure no pre-existing user changes were included accidentally. Keep each commit buildable. Do not amend existing commits and do not commit secrets, vault files, E2E screenshots containing credentials, or live-test artifacts.
