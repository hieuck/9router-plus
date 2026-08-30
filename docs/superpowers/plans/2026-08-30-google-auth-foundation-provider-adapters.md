# Google Authentication Foundation + Provider Auth Adapters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish one reusable Google authentication foundation and route all provider Google OAuth flows through provider-specific adapters without changing supported authentication capabilities.

**Architecture:** Wrap the existing `GoogleLoginStateMachine` behind an application-facing `IGoogleAuthenticationService`, then add a `ProviderOAuthAdapterRegistry` keyed by `ProviderKind`. Main Window, Credentials Manager, `AutoLoginOrchestrator`, and OpenRouter key flow will consume these boundaries; direct provider login remains a separate branch. Chrome/CDP lifecycle consolidation is deliberately deferred until the service and routing boundaries are stable.

**Tech Stack:** C# / .NET 8 / WPF MVVM, existing CDP client and managed Chrome session, xUnit, Moq, existing vault and provider models.

**Spec:** `docs/superpowers/specs/2026-08-30-google-auth-foundation-provider-adapters-design.md`

## Global Constraints

- Google auto-login is the foundation. Every provider flow that supports “Sign in with Google” must go through this module.
- Preserve current behavior and capabilities while changing ownership and composition incrementally.
- Do not remove OpenRouter OAuth, direct login, or auto-get-key capability.
- Provider adapters must not implement Google password, TOTP, account-picker, or credential-vault logic.
- Direct provider login must remain independent of the Google foundation.
- Do not replace Chrome/CDP with WebView, UI Automation, clipboard automation, extensions, or remote CDP.
- Never log passwords, TOTP values, API keys, cookies, tokens, DOM contents, or sensitive URLs.
- Use synthetic credentials and fake browser/CDP boundaries in automated tests; never use real Google credentials or live Google UI.
- Keep `https://openrouter.ai/settings/keys` as the OpenRouter business URL; internal session markers must not become the final user-visible URL.
- Each phase must pass its targeted tests before a separate commit is created.

## Repository Map and Ownership

### Existing files to preserve or adapt

- `src/RouterPlus.Infrastructure/Chrome/GoogleLoginStateMachine.cs` — existing Google credential state machine; preserve behavior and move calls behind the service boundary.
- `src/RouterPlus.Infrastructure/Chrome/IGoogleLoginBrowser.cs` — fakeable browser boundary for Google state-machine tests; keep it independent of provider OAuth.
- `src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs` — CDP implementation of Google browser operations; keep Google selectors here rather than in provider adapters.
- `src/RouterPlus.Infrastructure/Chrome/GoogleOAuthFlowAutomation.cs` — existing OAuth polling/base behavior; use as a migration source, not as a new caller-facing boundary.
- `src/RouterPlus.Infrastructure/Chrome/CodexOAuthAutomation.cs` — provider-specific OAuth behavior to adapt without moving its provider selectors into Google code.
- `src/RouterPlus.Infrastructure/Chrome/GitHubOAuthAutomation.cs` — provider-specific OAuth behavior.
- `src/RouterPlus.Infrastructure/Chrome/OpenRouterOAuthAutomation.cs` — OpenRouter-specific OAuth behavior.
- `src/RouterPlus.Infrastructure/Chrome/AwsBuilderIdOAuthAutomation.cs` — Kiro/AWS Builder ID-specific OAuth behavior.
- `src/RouterPlus.Infrastructure/Chrome/OAuthAutoLoginOrchestrator.cs` — interactive OAuth orchestrator currently hardcodes Codex; change its dependency and routing.
- `src/RouterPlus.Infrastructure/Services/AutoLoginOrchestrator.cs` — batch/provider orchestrator currently owns both provider switches; migrate it to shared registries while preserving fallback policy.
- `src/RouterPlus.Infrastructure/Chrome/DirectLoginAutomation.cs` and provider subclasses — retain as the independent direct-login branch.
- `src/RouterPlus.Infrastructure/Chrome/OpenRouterKeyFlowOrchestrator.cs` — compose Google authentication with OpenRouter onboarding.
- `src/RouterPlus.Infrastructure/Chrome/OpenRouterOnboardingAutomation.cs` and browser interfaces/adapters — retain only OpenRouter wizard/key creation responsibilities.
- `src/RouterPlus.Infrastructure/Chrome/ChromeManagedSession.cs` — defer target-attachment extraction to the final lifecycle phase.
- `src/RouterPlus.App/ViewModels/MainViewModel.cs` — replace direct Google automation composition incrementally; keep UI policy in the ViewModel.
- `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs` — replace direct Google automation delegate with the shared service seam while preserving its Google-only policy.

### New files introduced by this plan

- `src/RouterPlus.Infrastructure/Services/IGoogleAuthenticationService.cs` — caller-facing Google authentication contract.
- `src/RouterPlus.Infrastructure/Services/GoogleAuthenticationService.cs` — thin composition service over existing vault/session/state-machine behavior.
- `src/RouterPlus.Infrastructure/Services/GoogleAuthenticationRequest.cs` — request data required by the service; contains no UI state.
- `src/RouterPlus.Infrastructure/Chrome/IProviderOAuthAdapter.cs` — provider OAuth adapter contract.
- `src/RouterPlus.Infrastructure/Chrome/ProviderOAuthAdapterRegistry.cs` — deterministic `ProviderKind` routing.
- `src/RouterPlus.Infrastructure/Chrome/ProviderOAuthRequest.cs` — provider OAuth input contract.
- `src/RouterPlus.Infrastructure/Chrome/ProviderOAuthResult.cs` — provider OAuth output contract if existing `OAuthConsentResult` cannot express the needed boundary.
- `src/RouterPlus.Infrastructure/Chrome/ManagedChromeTargetConnector.cs` — only in the lifecycle consolidation phase, after call sites are migrated.

### Tests to add or modify

- `tests/RouterPlus.Infrastructure.Tests/GoogleAuthenticationServiceTests.cs` — service delegation, validation, cancellation, and result preservation.
- `tests/RouterPlus.Infrastructure.Tests/ProviderOAuthAdapterRegistryTests.cs` — exact provider-to-adapter mapping and unsupported behavior.
- `tests/RouterPlus.Infrastructure.Tests/OAuthAutoLoginOrchestratorTests.cs` — regression coverage for provider-aware routing and error mapping.
- `tests/RouterPlus.Infrastructure.Tests/OpenRouterKeyFlowOrchestratorTests.cs` — composition ordering and Google-service dependency.
- `tests/RouterPlus.Infrastructure.Tests/AutoLoginOrchestratorTests.cs` — fallback and provider routing behavior.
- `tests/RouterPlus.App.Tests/ViewModels/CredentialsManagerViewModelTests.cs` — shared Google-service caller behavior if the existing test seam supports it.
- Existing Google state-machine and CDP tests — extend only where a new service seam requires contract assertions.

---

### Task 1: Establish baseline and characterization tests

**Files:**
- Modify: `tests/RouterPlus.Infrastructure.Tests/AutoLoginOrchestratorTests.cs`
- Create: `tests/RouterPlus.Infrastructure.Tests/OAuthAutoLoginOrchestratorTests.cs`
- Create: `tests/RouterPlus.Infrastructure.Tests/GoogleAuthenticationServiceTests.cs` (initial contract tests only)
- Inspect: `src/RouterPlus.Infrastructure/Chrome/OAuthAutoLoginOrchestrator.cs`
- Inspect: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginStateMachine.cs`
- Inspect: `src/RouterPlus.Infrastructure/Chrome/OpenRouterKeyFlowOrchestrator.cs`

**Interfaces:**
- Consumes: existing `AutoLoginOrchestrator`, `OAuthAutoLoginOrchestrator`, `GoogleLoginStateMachine`, and OpenRouter flow behavior.
- Produces: executable regression coverage for the hardcoded Codex defect, Google result contracts, and OpenRouter ordering assumptions.

- [ ] **Step 1: Run the relevant existing tests before changes**

Run:

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --no-restore
```

Expected: record the current pass/fail result; do not alter unrelated failing tests.

- [ ] **Step 2: Write the failing provider-routing regression test**

Add a test seam that supplies a provider-specific factory/registry to `OAuthAutoLoginOrchestrator`, then assert that a GitHub request invokes the GitHub adapter rather than Codex. The test must use fake CDP/session objects and synthetic profile data; it must not launch Chrome.

Representative assertion shape:

```csharp
[Fact]
public async Task RunAsync_UsesAdapterForRequestedProvider()
{
    var adapter = new RecordingProviderOAuthAdapter(ProviderKind.GitHub);
    var registry = new StubProviderOAuthAdapterRegistry(adapter);
    var orchestrator = CreateOrchestrator(registry);

    var result = await orchestrator.RunAsync(
        ProviderKind.GitHub,
        new Uri("https://github.com/login/oauth/authorize"),
        new Uri("https://github.com"),
        "synthetic@example.invalid",
        TimeSpan.FromSeconds(1),
        CancellationToken.None);

    Assert.Equal(ProviderKind.GitHub, adapter.LastProvider);
    Assert.Equal(OAuthAutoLoginOutcome.Success, result.Outcome);
}
```

Adapt the setup to the existing constructor/session types; the assertion must fail against the current unconditional `new CodexOAuthAutomation(...)` construction.

- [ ] **Step 3: Add OpenRouter ordering characterization**

Extend `OpenRouterKeyFlowOrchestratorTests` with a fake Google authentication boundary and a fake onboarding browser/automation boundary. Assert that onboarding is not called when Google authentication fails and is called only after it succeeds.

- [ ] **Step 4: Add Google contract characterization**

Add tests around the future service seam for successful result pass-through, cancellation propagation, and no secret diagnostics. Keep the tests red until Task 2 introduces the service.

- [ ] **Step 5: Run the new tests and verify expected failures**

Run:

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ProviderRouting|FullyQualifiedName~OpenRouterKeyFlow|FullyQualifiedName~GoogleAuthentication"
```

Expected: the new tests fail because the new seams do not yet exist or the current hardcoded routing violates the test.

- [ ] **Step 6: Commit the baseline tests**

```powershell
git add tests/RouterPlus.Infrastructure.Tests
git commit -m "test(auth): capture current authentication flow contracts" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 2: Introduce the Google authentication foundation boundary

**Files:**
- Create: `src/RouterPlus.Infrastructure/Services/IGoogleAuthenticationService.cs`
- Create: `src/RouterPlus.Infrastructure/Services/GoogleAuthenticationRequest.cs`
- Create: `src/RouterPlus.Infrastructure/Services/GoogleAuthenticationService.cs`
- Modify: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginStateMachine.cs` only if a small visibility/constructor seam is required
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs` at `CreateDefaultGoogleLoginAutomation`
- Create/Modify: `tests/RouterPlus.Infrastructure.Tests/GoogleAuthenticationServiceTests.cs`
- Modify: `tests/RouterPlus.Core.Tests/GoogleAutoLoginViewModelTests.cs` or the existing MainViewModel Google tests

**Interfaces:**
- Consumes: `IGoogleLoginBrowser`, `GoogleLoginStateMachine.RunAsync`, `GoogleLoginCredential`, existing managed Chrome launcher/session abstractions.
- Produces:

```csharp
public interface IGoogleAuthenticationService
{
    Task<GoogleLoginResult> AuthenticateAsync(
        GoogleAuthenticationRequest request,
        CancellationToken cancellationToken);
}

public sealed record GoogleAuthenticationRequest(
    GoogleLoginCredential Credential,
    IGoogleLoginBrowser Browser);
```

Use an equivalent request shape if the existing browser/session ownership requires a factory delegate; the contract must keep callers from directly invoking the state machine.

- [ ] **Step 1: Define the request and service interface**

Keep the request free of UI concerns. It may contain the already-connected `IGoogleLoginBrowser` because `ChromeManagedSession` owns connection lifetime and the service should not silently launch a second browser. Do not put provider identity or provider selectors into this request.

- [ ] **Step 2: Implement the minimal delegation service**

Implement `GoogleAuthenticationService.AuthenticateAsync` by validating the request and delegating to:

```csharp
return await GoogleLoginStateMachine.RunAsync(
    request.Browser,
    request.Credential,
    cancellationToken);
```

Do not change existing state transitions, selectors, TOTP generation, origin guards, or result messages in this task.

- [ ] **Step 3: Run service tests and make them pass**

Run:

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GoogleAuthenticationService"
```

Expected: PASS, including cancellation and result pass-through.

- [ ] **Step 4: Route MainViewModel’s Google flow through the service**

Change only the composition point in `CreateDefaultGoogleLoginAutomation`: keep managed Chrome launch and adapter ownership as currently required, but call `IGoogleAuthenticationService.AuthenticateAsync` instead of calling `GoogleLoginStateMachine.RunAsync` directly. Preserve the existing behavior that keeps the session open for success/manual intervention and disposes it for terminal failures.

- [ ] **Step 5: Update MainViewModel tests**

Assert that the existing Google auto-login delegate still maps success, manual intervention, cancellation, and failure to the same UI behavior. Use the service fake rather than a live browser.

- [ ] **Step 6: Run the targeted test set**

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --no-restore
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleAutoLogin|FullyQualifiedName~MainViewModel"
```

Expected: PASS for all affected tests.

- [ ] **Step 7: Commit the Google foundation phase**

```powershell
git add src/RouterPlus.Infrastructure/Services/IGoogleAuthenticationService.cs src/RouterPlus.Infrastructure/Services/GoogleAuthenticationRequest.cs src/RouterPlus.Infrastructure/Services/GoogleAuthenticationService.cs src/RouterPlus.App/ViewModels/MainViewModel.cs tests/RouterPlus.Infrastructure.Tests/GoogleAuthenticationServiceTests.cs tests/RouterPlus.Core.Tests
git commit -m "refactor(auth): introduce Google authentication foundation" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 3: Add provider OAuth adapters and eliminate hardcoded Codex routing

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/IProviderOAuthAdapter.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/ProviderOAuthRequest.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/ProviderOAuthAdapterRegistry.cs`
- Create or Modify: `src/RouterPlus.Infrastructure/Chrome/ProviderOAuthResult.cs`
- Modify: `src/RouterPlus.Infrastructure/Chrome/OAuthAutoLoginOrchestrator.cs`
- Modify: `src/RouterPlus.Infrastructure/Services/AutoLoginOrchestrator.cs`
- Add adapter wrappers beside existing implementations if needed: `CodexOAuthAdapter.cs`, `GitHubOAuthAdapter.cs`, `OpenRouterOAuthAdapter.cs`, `AwsBuilderIdOAuthAdapter.cs`
- Modify: `tests/RouterPlus.Infrastructure.Tests/OAuthAutoLoginOrchestratorTests.cs`
- Modify: `tests/RouterPlus.Infrastructure.Tests/AutoLoginOrchestratorTests.cs`
- Create: `tests/RouterPlus.Infrastructure.Tests/ProviderOAuthAdapterRegistryTests.cs`

**Interfaces:**
- Consumes: `IGoogleAuthenticationService`, existing provider automation classes, `ProviderKind`, `CdpSession`, existing OAuth result types.
- Produces:

```csharp
public interface IProviderOAuthAdapter
{
    ProviderKind Provider { get; }

    Task<ProviderOAuthResult> RunAsync(
        ProviderOAuthRequest request,
        IGoogleAuthenticationService googleAuthentication,
        CancellationToken cancellationToken);
}

public interface IProviderOAuthAdapterRegistry
{
    IProviderOAuthAdapter Get(ProviderKind provider);
}
```

`ProviderOAuthRequest` must contain provider/start/target/profile data and the connected CDP context needed by the adapter, but no raw Google password/TOTP fields.

- [ ] **Step 1: Write exact registry mapping tests**

Assert all current supported OAuth mappings:

```csharp
Assert.IsType<CodexOAuthAdapter>(registry.Get(ProviderKind.Codex));
Assert.IsType<GitHubOAuthAdapter>(registry.Get(ProviderKind.GitHub));
Assert.IsType<OpenRouterOAuthAdapter>(registry.Get(ProviderKind.OpenRouter));
Assert.IsType<AwsBuilderIdOAuthAdapter>(registry.Get(ProviderKind.Kiro));
```

Assert an unsupported `ProviderKind` throws the project’s chosen explicit unsupported exception or returns an explicit unsupported adapter result. It must not return Codex.

- [ ] **Step 2: Implement the registry**

Register each existing provider automation exactly once. Keep construction dependencies explicit. Do not add a default-to-Codex branch.

- [ ] **Step 3: Implement thin provider adapter wrappers**

Each wrapper delegates provider-specific work to the corresponding existing automation. If the existing base automation currently owns generic Google credential steps, split only the minimum needed to call the new Google service; do not duplicate the selectors in the wrapper.

The provider adapter must:

1. start or inspect the provider flow;
2. click the provider’s Google CTA where applicable;
3. invoke `IGoogleAuthenticationService` for Google credential authentication;
4. continue provider-specific consent/callback detection;
5. return a provider-neutral result.

- [ ] **Step 4: Change `OAuthAutoLoginOrchestrator` to receive provider identity/registry**

Change its constructor to accept `ProviderKind` or an `IProviderOAuthAdapterRegistry` in addition to its existing session dependencies. Replace:

```csharp
var automation = new CodexOAuthAutomation(...);
```

with registry lookup and adapter invocation. Preserve navigation, timeout, cancellation, and disposal behavior. Map provider adapter failure to existing `OAuthAutoLoginResult` outcomes without converting failure to success.

- [ ] **Step 5: Reuse the registry from `AutoLoginOrchestrator`**

Remove the provider OAuth switch from `AutoLoginOrchestrator` and inject/use the same registry. Leave the direct-login switch unchanged in this task, except for extracting a separate registry only if required to compile cleanly; do not mix direct-login behavior changes into OAuth routing.

- [ ] **Step 6: Run routing tests**

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --filter "FullyQualifiedName~ProviderOAuthAdapterRegistry|FullyQualifiedName~OAuthAutoLoginOrchestrator|FullyQualifiedName~AutoLoginOrchestrator"
```

Expected: PASS; specifically, GitHub/OpenRouter/Kiro requests must not instantiate Codex automation.

- [ ] **Step 7: Commit the provider-routing phase**

```powershell
git add src/RouterPlus.Infrastructure/Chrome src/RouterPlus.Infrastructure/Services/AutoLoginOrchestrator.cs tests/RouterPlus.Infrastructure.Tests
git commit -m "fix(auth): route OAuth through provider adapters" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 4: Unify Main Window and Credentials Manager Google callers

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs` only where the shared service is composed/injected
- Modify: application DI/composition root file identified by the existing `GoogleLoginAutomation` registration
- Modify: `tests/RouterPlus.App.Tests/ViewModels/CredentialsManagerViewModelTests.cs`
- Modify: `tests/RouterPlus.Core.Tests/GoogleAutoLoginViewModelTests.cs` if required by the current test project boundaries

**Interfaces:**
- Consumes: `IGoogleAuthenticationService` from Task 2 and existing `GoogleLoginCredential`/profile/session factories.
- Produces: both ViewModels use the same service implementation; Credentials Manager may retain a Google-only workflow policy.

- [ ] **Step 1: Write the Credentials Manager service-consumption test**

Inject a fake `IGoogleAuthenticationService` into the ViewModel’s constructor or its composition delegate. Assert `LoginRowAsync` passes the selected credential once and returns the fake result. Assert `BatchLoginAsync` uses the same service for each selected Google record and does not call a separate Google state-machine path.

- [ ] **Step 2: Adapt the constructor seam**

Replace the direct `Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>` implementation with a service-backed delegate or direct `IGoogleAuthenticationService` dependency. Preserve the existing public behavior and test-friendly injection; avoid changing unrelated vault UI logic.

- [ ] **Step 3: Route single and batch login through the service**

Change only the invocation boundary. Keep row selection, vault loading, progress, and UI messages unchanged. The service must own Google credential state-machine execution; the ViewModel must not call `GoogleLoginStateMachine` directly.

- [ ] **Step 4: Verify direct login isolation**

Add a test to `AutoLoginOrchestratorTests` using a fake Google service/registry and direct credentials. Assert the direct branch succeeds/fails based on the direct automation result without invoking the Google service.

- [ ] **Step 5: Run caller tests**

```powershell
dotnet test tests/RouterPlus.App.Tests/RouterPlus.App.Tests.csproj --filter "FullyQualifiedName~CredentialsManager"
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~GoogleAutoLogin|FullyQualifiedName~MainViewModel"
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --filter "FullyQualifiedName~AutoLoginOrchestrator"
```

Expected: PASS, including batch cancellation and loading-state cleanup.

- [ ] **Step 6: Commit the unified-caller phase**

```powershell
git add src/RouterPlus.App/ViewModels src/RouterPlus.App tests/RouterPlus.App.Tests tests/RouterPlus.Core.Tests tests/RouterPlus.Infrastructure.Tests
git commit -m "refactor(auth): unify Google login callers" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 5: Compose OpenRouter key flow with the Google foundation

**Files:**
- Modify: `src/RouterPlus.Infrastructure/Chrome/OpenRouterKeyFlowOrchestrator.cs`
- Modify: `src/RouterPlus.Infrastructure/Chrome/OpenRouterOnboardingAutomation.cs` only if its API currently exposes Google behavior
- Modify: `src/RouterPlus.Infrastructure/Chrome/OpenRouterOnboardingBrowser.cs` only if a Google-specific member must be removed from its boundary
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs` at `CreateDefaultOpenRouterKeyFlow`
- Modify: `tests/RouterPlus.Infrastructure.Tests/OpenRouterKeyFlowOrchestratorTests.cs`
- Modify: `tests/RouterPlus.Core.Tests/MainViewModelAutoGetKeyTests.cs`

**Interfaces:**
- Consumes: `IGoogleAuthenticationService`, `OpenRouterOnboardingAutomation`, `IOpenRouterOnboardingBrowser`, `GoogleLoginCredential`.
- Produces: OpenRouter key flow explicitly calls Google foundation before onboarding and never calls `GoogleLoginStateMachine` directly.

- [ ] **Step 1: Write the composition-order tests**

Use recording fakes with an event list:

```csharp
var events = new List<string>();
// fake Google service adds "google"; fake onboarding adds "onboarding"
Assert.Equal(new[] { "google", "onboarding" }, events);
```

Add failure tests asserting onboarding is not called when Google authentication returns manual intervention, invalid credential, cancellation, or browser failure.

- [ ] **Step 2: Change `OpenRouterKeyFlowOrchestrator` dependency**

Replace its direct `GoogleLoginStateMachine.RunAsync` dependency with `IGoogleAuthenticationService.AuthenticateAsync`. Keep the existing connected `IGoogleLoginBrowser` request and credential flow. Do not move OpenRouter selectors into the Google service.

- [ ] **Step 3: Keep onboarding provider-specific**

Review `OpenRouterOnboardingAutomation` and ensure it owns only keys page, welcome wizard, New Key, key name, Create, and API-key capture. If a method combines Google and onboarding behavior, split the call at the orchestrator boundary without changing selectors.

- [ ] **Step 4: Preserve URL and early-success behavior**

Keep `https://openrouter.ai/settings/keys` as the navigation target and retain the existing “API key already present” short-circuit. Ensure session-marker handling remains internal and does not alter the final business URL.

- [ ] **Step 5: Run OpenRouter and auto-get-key tests**

```powershell
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --filter "FullyQualifiedName~OpenRouterKeyFlow|FullyQualifiedName~OpenRouterOnboarding"
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~AutoGetKey"
```

Expected: PASS, including Google failure preventing onboarding and API-key capture after successful authentication.

- [ ] **Step 6: Commit the OpenRouter composition phase**

```powershell
git add src/RouterPlus.Infrastructure/Chrome/OpenRouterKeyFlowOrchestrator.cs src/RouterPlus.Infrastructure/Chrome/OpenRouterOnboardingAutomation.cs src/RouterPlus.Infrastructure/Chrome/OpenRouterOnboardingBrowser.cs src/RouterPlus.App/ViewModels/MainViewModel.cs tests/RouterPlus.Infrastructure.Tests/OpenRouterKeyFlowOrchestratorTests.cs tests/RouterPlus.Core.Tests/MainViewModelAutoGetKeyTests.cs
git commit -m "refactor(openrouter): compose key flow with Google foundation" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 6: Consolidate managed Chrome/CDP lifecycle

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/ManagedChromeTargetConnector.cs`
- Modify: `src/RouterPlus.Infrastructure/Chrome/ChromeManagedSession.cs`
- Modify: `src/RouterPlus.Infrastructure/Chrome/GoogleLoginCdpBrowser.cs` only if constructor/session ownership needs a narrow adjustment
- Modify: `src/RouterPlus.Infrastructure/Chrome/OpenRouterOnboardingCdpBrowser.cs` only if shared session ownership needs a narrow adjustment
- Modify: `src/RouterPlus.Infrastructure/Chrome/ChromeLauncherAdapter.cs` if the existing profile setting is demonstrably bypassed by auth flows
- Modify: `tests/RouterPlus.Core.Tests/ChromeManagedSessionTests.cs`
- Add: `tests/RouterPlus.Infrastructure.Tests/ManagedChromeTargetConnectorTests.cs`

**Interfaces:**
- Consumes: existing `ChromeCdpClient`, `CdpSession`, loopback/session-marker policies, and browser adapters.
- Produces: one internal target-attachment implementation with explicit client/session ownership and unchanged public flow behavior.

- [ ] **Step 1: Write connector tests for target selection**

Cover one page target, no page target until timeout, multiple marked Google targets, one allowed Google fallback target, wrong-origin targets, and cancellation. Use fake HTTP/CDP responses; do not launch Chrome.

- [ ] **Step 2: Extract shared connection logic**

Move only the repeated operations from `ConnectAnyTargetAsync` and `ConnectGoogleLoginAsync` into `ManagedChromeTargetConnector`: CDP connect, bounded target polling, target selection, attach, bring-to-front, and dispose-on-error. Preserve the current exact timeout behavior unless a test exposes a bug.

- [ ] **Step 3: Rebuild specialized session methods on the connector**

Make `ConnectAnyTargetAsync`, `ConnectGoogleLoginAsync`, and `ConnectOpenRouterFlowAsync` use the shared connector while preserving their target policies. Browser adapters must not dispose the shared client; `CdpSession` remains the owner of the CDP client.

- [ ] **Step 4: Verify ChromeLauncherAdapter profile behavior**

If the adapter still hardcodes `useOriginalProfile: true` while auth settings provide `UseOriginalProfileForAutoLogin`, add a focused test and pass the configured value through. Do not broaden this change to unrelated launch settings.

- [ ] **Step 5: Run lifecycle and full infrastructure tests**

```powershell
dotnet test tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj --filter "FullyQualifiedName~ChromeManagedSession"
dotnet test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj
```

Expected: PASS, with no leaked CDP client/session on connection failures and no regression in loopback checks or temp-profile cleanup.

- [ ] **Step 6: Commit the lifecycle phase**

```powershell
git add src/RouterPlus.Infrastructure/Chrome/ManagedChromeTargetConnector.cs src/RouterPlus.Infrastructure/Chrome/ChromeManagedSession.cs src/RouterPlus.Infrastructure/Chrome/ChromeLauncherAdapter.cs tests/RouterPlus.Core.Tests/ChromeManagedSessionTests.cs tests/RouterPlus.Infrastructure.Tests/ManagedChromeTargetConnectorTests.cs
git commit -m "refactor(chrome): consolidate managed CDP lifecycle" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

### Task 7: Remove obsolete overlap and add extension guardrails

**Files:**
- Modify/Delete: obsolete compatibility wrappers only after repository-wide caller search confirms zero references
- Modify: `src/RouterPlus.Infrastructure/Chrome/GoogleOAuthFlowAutomation.cs` and provider OAuth classes to remove duplicate Google selectors only when covered by the new service
- Modify: `src/RouterPlus.Infrastructure/Chrome/DirectLoginAutomation.cs` or add `DirectLoginRegistry.cs` only if the direct branch still has a duplicated provider factory
- Modify: DI/composition root and XML documentation files touched by the migration
- Modify: `docs/superpowers/specs/2026-08-30-google-auth-foundation-provider-adapters-design.md` only if implementation decisions materially changed the approved design
- Add/Modify: routing and composition tests for the final invariants

**Interfaces:**
- Consumes: all completed boundaries from Tasks 2–6.
- Produces: no obsolete caller-facing path that bypasses the Google service, no provider-specific Google credential selectors, and a documented extension rule for new providers.

- [ ] **Step 1: Search for forbidden bypasses**

Run repository searches for:

```powershell
rg "GoogleLoginStateMachine\.RunAsync|new CodexOAuthAutomation|accounts\.google\.com|input\[type=.?tel|TotpSecret" src tests
```

Classify each match. Keep Google selectors in the Google browser/state-machine module; remove only provider-owned duplicates and direct calls from callers.

- [ ] **Step 2: Remove compatibility code only after reference verification**

Use `rg`/IDE references to prove each wrapper has zero callers. Delete only wrappers introduced by this refactor; do not remove pre-existing automation classes that still provide capability.

- [ ] **Step 3: Add provider-extension guardrail tests**

Assert that every registered Google OAuth provider has an adapter and that no registry lookup silently returns Codex for another provider. Assert direct-login providers remain separate from Google adapters.

- [ ] **Step 4: Update documentation and XML comments**

Document the rule that a new provider supporting Google OAuth must depend on `IGoogleAuthenticationService`. Keep security constraints and OpenRouter URL behavior explicit. Do not add speculative abstractions or unrelated documentation.

- [ ] **Step 5: Run the complete validation suite**

```powershell
dotnet build 9RouterPlus.sln --no-restore
dotnet test 9RouterPlus.sln --no-restore
```

Expected: build succeeds and all tests pass. If a pre-existing test fails, record the exact failure and do not claim the phase is complete.

- [ ] **Step 6: Commit final cleanup**

```powershell
git add src tests docs
git commit -m "chore(auth): remove obsolete authentication overlap" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Self-Review Checklist

- [x] Spec coverage: Google foundation, provider routing, direct-login separation, OpenRouter composition, CDP lifecycle, tests, security, and per-phase commits are all represented.
- [x] No implementation step relies on a placeholder such as TBD/TODO or “handle appropriately”; each step names files, behavior, and verification.
- [x] Type consistency: `IGoogleAuthenticationService`, `GoogleAuthenticationRequest`, `IProviderOAuthAdapter`, `ProviderOAuthRequest`, and `IProviderOAuthAdapterRegistry` are defined before later tasks consume them.
- [x] Scope control: no OpenRouter capability removal, no live-Google tests, no UI redesign, and no broad unrelated refactor.
- [x] Commit boundaries: each phase ends with a targeted test run and a distinct commit message.

## Execution Handoff

The plan is ready for execution. Use either:

1. **Subagent-driven execution (recommended):** dispatch a fresh implementation agent per task and review after each task.
2. **Inline execution:** execute this plan in the current session with checkpoints after each phase.
