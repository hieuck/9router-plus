# Codebase Audit — Optimization & Refactor (2026-10-25)

Repo: `E:\GitHub\9router-plus` — branch `main`, commit `a770299`.
Scope: read-only audit, no code changes. 4 free subagents + coordinator verification.

## Method / limits

- Subagents (free models):
  - architecture: `architect` + `nemotron-3-ultra:free`
  - performance: `general` + `nemotron-3-ultra:free`
  - code quality: `code-reviewer` + `nous/beam-1.0:free`
  - build/test/debt: `explore` + `nemotron-3-ultra:free`
- Static review + existing live logs (2026-10-25). No full test suite, no app run during audit.
- Reviewed by 2 more free subagents: fact-check (`code-reviewer` + `nous/beam-1.0:free`) and roadmap critique (`architect` + `nemotron-3-ultra:free`). Both returned **approve-with-corrections**; corrections are applied and listed in the Review section.
- High/quick-win claims marked **verified** were re-read by the coordinator. Free-model line numbers/test sizes can drift; current metrics below are authoritative.
- Current metrics: 426 src `.cs`, 235 test `.cs`; `MainViewModel` 5,440 lines; `CredentialsManagerViewModel` 2,550; `GoogleLoginCdpBrowser` 1,805; `CredentialsManagerViewModelTests` 3,964.

## Executive summary — fix in this order

1. **Security:** 9Router dashboard password is persisted **plaintext** in `settings.json`.
2. **Performance:** every connection list also fetches quotas for every connection: 274 connections → 274 `GET /api/usage/{id}` per list; list takes ~14–20s; waits poll it every 2s; status lags after the connection already exists.
3. **Provider extensibility:** adding a provider touches 8+ switch sites; centralize metadata.
4. **MainViewModel god class (5,440):** extract profile, connection, workflow, credentials services.
5. **UX quick wins:** show success before the full refresh, skip quotas while waiting, pause quota polling during a workflow, remove Chrome fixed sleeps. Profiles are already virtualized; only the nested connections `ItemsControl` is not (`MainWindow.xaml:445-458,938`).
6. **Tests/build:** 1 pre-existing Core failure, Infra flake, E2E fragility, huge test files, test package drift, no analyzers.
7. **Feature debt:** CredentialsManager direct login for Kiro/GitHub/OpenRouter is a stub.
8. **Supply-chain/hygiene:** local bootstrap downloads `dotnet-install.ps1` without checksum; `WORKTREE_*.md`/`PROVIDER_LOGIN_TODO.md` are tracked; stale provider docs. (`*.log`, `*.stackdump`, `*.csx` are already ignored.)

## Verified performance root cause (highest ROI)

Code path:

- `RouterApiClient.ListConnectionsAsync` → `FetchAllQuotasAsync` (`RouterApiClient.cs:37`).
- `FetchAllQuotasAsync` builds one task per connection (`RouterApiClient.cs:579`), runs `Task.WhenAll` (`:591`), each task calls `GET api/usage/{connectionId}` (`:604`).
- With 274–275 connections this is **274–275 HTTP calls per list**.
- `WaitForNewConnectionAsync` polls the list every 2s; `RefreshConnectionStatusesAsync`/quota polling also list; the recent workflow performed multiple such loads.
- Measured: full list ~14–20s; a 274-connection workflow 73.5s; manual-TOTP run 152s; the 9Router connection was created ~1.7s after Nous confirmation but the UI status stayed “Đang chờ 9Router tạo connection…” while full lists/refresh ran.

Client-only fixes (no server change):

1. Add `ListConnectionsAsync(..., includeQuotas: false)`; use it in `WaitForNewConnectionAsync`, `RenameNewConnectionAsync`, and workflow waits. They only need IDs/names. Fetch quotas once afterwards/background.
2. Pause quota polling during provider workflows (`MainViewModel.PauseQuotaPolling`/`ResumeQuotaPollingAsync` already exist) and gate duplicate `ConnectionRefreshRequested`.
3. Set success/rename status **before** the full refresh; move the quota-bearing refresh to background.
4. Cache/diff the last list; only fetch quotas for new/changed connections.

Server/API fixes (coordinate 9Router):

5. `GET /api/providers?provider=nous&page=&pageSize=` and/or a lightweight connections-only response.
6. `GET /api/usage/batch?ids=...` (274 calls → 1) or include quotas in the providers response.
7. Event-driven/long-poll connection wait instead of 2s polling.

### Review corrections for the performance section

- **Unbounded fan-out:** `FetchAllQuotasAsync` starts one task per connection before `Task.WhenAll` (`RouterApiClient.cs:583-591`), so 274 connections means 274 concurrent sockets. With the 20s `HttpClient.Timeout` (`MainViewModel.cs:167,5209`), one slow batch can by itself make a list take ~20s. Throttle with a `SemaphoreSlim` (e.g. 8) even if quotas are kept.
- **The refresh gate serializes but does not dedupe:** `_connectionRefreshGate` (`MainViewModel.cs:119,2687-2695`) queues duplicate requests; each queued call still runs a full 274-quota list. Only `QuotaPollingService` skips an overlapping tick (`QuotaPollingService.cs:122-135`). Coalesce `ConnectionRefreshRequested` during a workflow.
- **`includeQuotas:false` is only safe for wait/rename/fallback paths.** `WaitForNewConnectionAsync` compares ID/`LastRefreshAt`/`Email` only (`RouterApiClient.cs:374-377`). The main status refresh needs quota rows for auto-disable (`MainViewModel.cs:2792-2798`) and reset suggestions (`:2830`); when exhausted connections exist it lists everything a second time (`:2826`). Do not flip it globally.
- **Quota failures are silent:** `FetchQuotaAsync` catches every non-cancellation exception and returns null (`RouterApiClient.cs:638-641`). Missing quota rows silently disable Kiro auto-disable, which requires `QuotaRows.Count > 0` (`QuotaAutoDisablePolicy.cs:22-29`). Log/count failures at debug level at minimum.
- **`Task.WhenAny` edge:** if polling wins, `automationTask` is not awaited (`MainViewModel.cs:3565,3581`); a later fault is unobserved. Low impact once the flow succeeds, but observe it explicitly.

## Quick wins (≤1 day, client-only)

| # | Fix | Evidence | Impact |
|---|-----|----------|--------|
| 1 | `includeQuotas:false` for wait/rename + skip duplicate refresh | `RouterApiClient.cs:25-47,37,579-604`; `MainViewModel.cs` wait/rename | Removes 274 usage calls per 2s poll; cuts the 14–20s wait/status lag |
| 2 | Success status before background refresh | `RenameNewConnectionAsync`; live logs | User sees success when connection exists (<2s), not after 14–20s refresh |
| 3 | Pause quota polling during workflow | `QuotaPollingService`; `PauseQuotaPolling`/`Resume` | Stops concurrent full-list/usage storms |
| 4 | Chrome: remove `Thread.Sleep(1500/2000)` + poll | `ChromeLauncher.cs:523,608`; `ChromeManagedSession.cs:213` | ~3.5s blocked time → async waits |
| 5 | Encrypt dashboard password (DPAPI) + migration | `RouterSettings.cs:7`; `SettingsStore` JSON; `settings.json` | Removes plaintext local secret |
| 6 | Virtualize only the nested connections `ItemsControl` (profiles already virtualized) | `MainWindow.xaml:445-458`; nested list at `:938` | Render cost for cards with many connections; no startup gain |
| 7 | Bootstrap installer checksum | `scripts/bootstrap-dotnet.ps1:6,11` | Supply-chain integrity |
| 8 | Analyzers; central test package versions; untrack `WORKTREE_*.md` / `PROVIDER_LOGIN_TODO.md` | `Directory.Build.props`; tracked docs | Quality gates; version drift; repo hygiene (logs/stackdump/csx already ignored) |

## Architecture / refactor priorities

1. **Provider metadata single source:** `ProviderKind`, `ProviderCatalog`, `ProviderDefinition.ShortDisplayName/Glyph`, `ProviderCardViewModel.ShortCode`, `RouterApiClient` alias parse/map, `MainViewModel.GetProviderLoginUri`, `AutoLoginOrchestrator.GetProviderLoginUrl`, `DirectLoginAutomationFactory`, `ProviderOAuthAdapterRegistry`, quota/usage switches, XAML triggers, tests. Add alias/short/glyph/login URL/factories to descriptor/registry; keep `ProviderKind` for persistence. Impact high, risk medium, effort L.
2. **Split `MainViewModel`:** `ProfileManager` (discovery/CRUD/health), `ProviderConnectionService` (list/test/rename/quota/auto-disable), `ProviderWorkflowService` (add/device-code/browser/poll), credentials/settings; then DI/`IAppContext`. Impact high, risk medium, effort L.
3. **Split `CredentialsManagerViewModel` (2,550) and its 3,964-line test file** by feature.
4. **OAuth automation test seams:** fake page-state/browser/CDP; do not rewrite DOM. OAuth/CDP coverage is still near zero.
5. **Vault/credentials:** common `IVaultStore<T>`; dashboard password DPAPI; avoid concrete vault exposure from the VM.
6. **Cancellation ownership:** one CTS per user action; pass tokens; remove ad-hoc `Func<>` seams where services can be injected.

Order: provider metadata (independent) → ProfileManager/ConnectionService → Workflow → Credentials/DI → OAuth seams.

## Do not refactor yet

- `GoogleLoginCdpBrowser` DOM selectors (1,805 lines) — brittle, works only with instrumentation.
- `ChromeManagedSession`/CDP transport — high risk, low churn.
- `RouterApiClient` JSON schema parsing — tied to 9Router API.
- WPF style dictionaries — already modularized.
- `ProviderKind` → string vault keys/DPAPI migration — not worth it until provider count grows.

## Code quality / security

| Severity | Finding | Evidence | Fix | Effort |
|----------|---------|----------|-----|--------|
| High | Dashboard password plaintext | `RouterSettings.cs:7`; `SettingsStore` JSON; live `settings.json` | DPAPI + migration | M |
| High | Provider metadata duplicated 8+ sites | see list above | descriptor/registry | L |
| High | God classes | 5,440 / 2,550 / 1,805 | split services | L |
| Medium | Direct login stub for Kiro/GitHub/OpenRouter | `CredentialsManagerViewModel.cs:1741-1775` | wire factory or remove action | M |
| Medium | Quota/usage provider gaps (`Nous`, `Kimchi`, `GitHub`) | `QuotaAutoDisablePolicy`; `UsageInferenceService` | explicit cases or documented defaults | S |
| Medium | Huge single test file | `CredentialsManagerViewModelTests` 3,964 | split by feature | M |
| Low | GitHub enum not in catalog | `ProviderCatalog` vs `ProviderKind.GitHub` | catalog-ize or document | S |
| Low | Dead diagnostic comments | `GoogleLoginCdpBrowser` | delete dead comments | S |

## Tests / build / CI

- Known pre-existing: `MainViewModelDashboardCommandTests.Confirmed_reenable…` (quota dashboard). Infra had one flaky run (clean on retry); E2E FlaUI is environment-dependent.
- Test package drift (xUnit/Moq/coverlet versions across projects) — centralize.
- No analyzers beyond SDK defaults — add `Microsoft.CodeAnalysis.NetAnalyzers`.
- Test helper duplication (`CreateTempDirectory` in 3 projects) — shared test-helpers project.
- E2E: keep synthetic/headless in CI; live Chrome E2E behind manual/self-hosted.
- Bootstrap: `dotnet-install.ps1` downloaded without checksum.
- Repo hygiene: `*.log`, `*.stackdump`, `*.csx` are already ignored (`.gitignore:91,132,133`). The actual tracked clutter is `WORKTREE_AUDIT.md`, `WORKTREE_CLEANUP_SUMMARY.md`, `PROVIDER_LOGIN_TODO.md`; `PROVIDER_LOGIN_TODO.md`/CHANGELOG mention a GitHub provider not in the UI.

## Old audit status (2026-09-06)

Resolved: VaultStoreBase extraction, vault tests, observability unification, empty-catch comments, TODO removal, thread-safety docs, modular XAML styles.
Still open: MainViewModel god class, provider extensibility blast radius, OAuth/CDP test coverage, overall coverage, plus new plaintext-password finding.

## Recommended roadmap

**Phase 1 (1–2 days):** quick wins #1–#4 + dashboard password DPAPI + Chrome sleeps + nested-list virtualization + analyzers/untracking.
Expected: workflow visible completion <5s after Nous; fewer 14–20s stalls; no plaintext secret.

**Phase 2 (1–2 weeks):** provider descriptor/registry; split MainViewModel services + tests; split credentials VM/tests; test helpers/package versions; direct-login stub decision.

**Phase 3 (coordinate 9Router):** provider-filtered/paged providers, batch usage, event-driven wait; startup defer/lazy load.
Expected: 274-connection list <500ms; 274 usage calls → 1; full flow nearing manual <30s.

## Review (2026-10-25)

Two independent free-subagent reviews: fact-check (`code-reviewer` + `nous/beam-1.0:free`) and roadmap critique (`architect` + `nemotron-3-ultra:free`). **Verdict: both approve-with-corrections.** Their factual corrections are applied above.

### Revised top-5 order (both reviewers converged)

1. **Quick wins #1–#4** — client-only; directly fixes the 14–20s status lag and the 274-call storm during waits.
2. **Dashboard password DPAPI + migration** — independent, high severity, no refactor prerequisite.
3. **Provider descriptor/registry** — do this before splitting the VM so the new services consume descriptors, not switches.
4. **Split `MainViewModel`** into `ProfileManager` / `ProviderConnectionService` / `ProviderWorkflowService` + DI.
5. **Split `CredentialsManagerViewModel` and its 3,964-line test file** by feature.

OAuth/CDP seams stay in Phase 3; do not rewrite `GoogleLoginCdpBrowser` DOM. The single smallest high-impact batch is **items 1 + 2**; the registry and VM split are not prerequisites for it.

### Corrections applied

- Metrics were non-blank line counts; authoritative totals are 5,440 / 2,550 / 1,805 / 3,964.
- `ProfileList` is already virtualized (`MainWindow.xaml:445-458`); only the nested `Connections` `ItemsControl` (`:938`) is not. Quick win #6 narrowed.
- `.gitignore` already ignores `*.log`, `*.stackdump`, `*.csx` (`.gitignore:91,132,133`); real hygiene item is tracked `WORKTREE_*.md` / `PROVIDER_LOGIN_TODO.md`.
- `MainViewModel` constructor has **16** parameters (`MainViewModel.cs:131-147`), not 18 (and not 17).
- The refresh gate serializes but does not dedupe; `includeQuotas:false` is safe only on wait/rename paths; quota fan-out is unbounded.

### Risks the first draft missed

- **DPAPI migration:** read legacy plaintext, re-encrypt, rewrite atomically; keep backward-compatible deserialization. If DPAPI fails (roaming/profile corruption), prompt for the password rather than locking the user out.
- **Quota fan-out throttle:** adding `includeQuotas:false` but leaving the 274-task `Task.WhenAll` only moves the storm to the status refresh. Add a `SemaphoreSlim` cap.
- **Silent quota failures:** `FetchQuotaAsync` swallowing errors can silently turn off quota auto-disable; at least log the failure count.
- **Server dependency:** provider-filtered/paged list and batch usage are the only path to <500ms with 274 connections; client cache/diff is a stopgap, not a substitute.
- **Unobserved automation task:** when polling wins, `automationTask` is not awaited (`MainViewModel.cs:3565,3581`); observe it explicitly.

## Verification / confidence

- Verified by coordinator: plaintext password, 274 `api/usage` calls via `FetchAllQuotasAsync`, Chrome sleeps, direct-login stub, bootstrap no checksum, metrics, test counts, constructor has **16** parameters (`MainViewModel.cs:131-147`), `ProfileList` virtualization, `.gitignore` coverage.
- Reported by subagents, not re-verified: test package version drift; WPF virtualization defaults elsewhere; E2E CI details; some architecture line numbers.
- Do not act on unverified line numbers without re-reading current source.
