# Google Authentication Foundation + Provider Auth Adapters

**Date:** 2026-08-30  
**Status:** Approved for phased implementation

## 1. Goal

Reduce overlap between Google auto-login, provider OAuth, direct provider login, OpenRouter key onboarding, and the Chrome/CDP lifecycle by establishing one reusable Google authentication foundation.

The governing rule is:

> Google auto-login is the foundation. Every provider flow that supports “Sign in with Google” must go through this module.

The refactor must preserve current behavior and capabilities while changing ownership and composition incrementally. OpenRouter OAuth, direct login, and auto-get-key remain supported unless a later decision explicitly changes their scope.

## 2. Scope

### In scope

- A single application-facing Google authentication service.
- Reuse of that service by interactive login, Main Window batch login, Credentials Manager, provider OAuth flows, and OpenRouter key flow.
- Provider-specific OAuth adapters selected by `ProviderKind`.
- Separation of provider direct login from Google authentication.
- OpenRouter onboarding as provider-specific work after Google authentication.
- Consistent timeout, cancellation, result mapping, and Chrome/CDP ownership.
- Regression tests for routing, composition, security, and failure behavior.

### Out of scope

- Rewriting Google selectors or changing supported Google UI states without a failing test or compatibility requirement.
- Removing OpenRouter OAuth or direct-login capability.
- Changing provider authentication policy or fallback semantics unless required to remove duplicate orchestration.
- Replacing Chrome/CDP with WebView, UI Automation, clipboard automation, extensions, or remote CDP.
- Unrelated UI, vault, localization, or provider-catalog changes.

## 3. Current problems

The current implementation has several overlapping boundaries:

1. `GoogleLoginStateMachine` owns Google credential state transitions, while `GoogleOAuthFlowAutomation` and provider subclasses also contain Google account-picker, consent, visibility, and state detection logic.
2. `OAuthAutoLoginOrchestrator` creates `CodexOAuthAutomation` unconditionally, even when the caller is handling another provider.
3. `AutoLoginOrchestrator` contains its own provider OAuth and direct-login switches.
4. Main Window batch login and Credentials Manager use different Google automation entry points.
5. `OpenRouterKeyFlowOrchestrator` contains a provider-specific flow that must call Google authentication before onboarding, but its dependency boundary should be explicit.
6. `ChromeManagedSession` repeats CDP connection, target polling, target attachment, and bring-to-front plumbing.

These problems create functional risk as well as duplication: the wrong provider automation can be selected, and changes to Google login behavior can diverge across callers.

## 4. Target architecture

```text
App / ViewModels
        |
        v
Authentication application services
        |
        +-- IGoogleAuthenticationService
        |
        +-- ProviderAuthenticationOrchestrator
        |       |
        |       +-- ProviderOAuthAdapterRegistry
        |       |       +-- CodexOAuthAdapter
        |       |       +-- GitHubOAuthAdapter
        |       |       +-- OpenRouterOAuthAdapter
        |       |       +-- AwsBuilderIdOAuthAdapter
        |       |
        |       +-- DirectLoginRegistry
        |               +-- CodexDirectLoginAutomation
        |               +-- GitHubDirectLoginAutomation
        |               +-- OpenRouterDirectLoginAutomation
        |               +-- KiroDirectLoginAutomation
        |
        +-- OpenRouterKeyFlowOrchestrator
                +-- IGoogleAuthenticationService
                +-- OpenRouterOnboardingAutomation

Chrome/CDP infrastructure
        +-- managed Chrome lifecycle
        +-- GoogleLoginCdpBrowser
        +-- provider OAuth browser adapters
        +-- OpenRouterOnboardingCdpBrowser
```

### 4.1. Google foundation

The Google foundation is the only module allowed to automate Google credential authentication. It owns:

- Google origin and target validation.
- Google page-state reading and supported state transitions.
- Email and password entry.
- Account picker selection.
- Authenticator/2FA method selection.
- TOTP generation and entry.
- Manual challenge, CAPTCHA, passkey, and unsupported-state detection.
- Completion detection.
- Timeout, cancellation, browser-disconnect, and result mapping.
- Secret-safe diagnostics.

The first implementation should preserve `GoogleLoginStateMachine` and `IGoogleLoginBrowser` behavior behind the new boundary rather than rewrite the state machine.

The foundation must not own provider-specific buttons, provider consent, provider callbacks, onboarding, API-key creation, or direct provider login.

### 4.2. Provider OAuth adapters

Each provider OAuth adapter owns only its provider-specific flow:

- provider start URL and initial CTA;
- provider-specific “Sign in with Google” interaction;
- transition into Google authentication;
- provider-specific consent;
- redirect/callback/completion detection.

An adapter may depend on `IGoogleAuthenticationService`, but must not directly call `GoogleLoginStateMachine`, read Google credentials, or implement Google password/TOTP/account-picker selectors.

The adapter is selected by a registry keyed by `ProviderKind`:

```text
ProviderKind.Codex      -> Codex OAuth adapter
ProviderKind.GitHub     -> GitHub OAuth adapter
ProviderKind.OpenRouter -> OpenRouter OAuth adapter
ProviderKind.Kiro       -> AWS Builder ID OAuth adapter
```

Missing support must produce an explicit unsupported result; it must never silently use another provider's adapter.

### 4.3. Direct login

Direct login remains a separate branch. It may automate provider email/password/TOTP fields, but it must not depend on or invoke the Google foundation. Existing `DirectLoginAutomation` subclasses remain provider-specific and are routed through a dedicated registry or equivalent factory.

### 4.4. OpenRouter key flow

OpenRouter key automation is composed from two responsibilities:

1. OpenRouter authentication, which invokes the Google foundation when the user chooses Google OAuth.
2. OpenRouter onboarding, which handles the welcome wizard, New Key dialog, key name, creation, and API-key capture.

`OpenRouterOnboardingAutomation` and its browser adapter must not contain Google credential-login logic. The flow remains:

```text
OpenRouter sign-in
    -> provider Google CTA
    -> IGoogleAuthenticationService
    -> return to OpenRouter keys page
    -> OpenRouter onboarding
    -> API key result
```

The initial business URL remains `https://openrouter.ai/settings/keys`. Internal session markers must not become the final user-visible URL or be logged as sensitive URL data.

### 4.5. Chrome/CDP lifecycle

Managed Chrome owns the process and the CDP client/session lifetime. Browser adapters do not dispose a shared client. A later cleanup phase may extract a shared target connector from `ChromeManagedSession` for:

- CDP connection;
- page-target polling;
- target selection;
- attachment;
- bring-to-front;
- error cleanup.

This extraction must preserve loopback-only CDP, session-marker association, selected profile behavior, and temp-profile cleanup.

## 5. Interfaces and contracts

Names may be adjusted to match existing conventions, but the boundaries must remain equivalent:

```csharp
public interface IGoogleAuthenticationService
{
    Task<GoogleLoginResult> AuthenticateAsync(
        GoogleLoginRequest request,
        CancellationToken cancellationToken);
}

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

The implementation may keep a compatibility wrapper while callers migrate, but the wrapper must not retain duplicate provider policy or Google automation logic.

## 6. Phased implementation

### Phase 0: Baseline and characterization

- Run the existing test suite and build.
- Add or strengthen tests for the current Google, OpenRouter, routing, cancellation, and diagnostics contracts.
- Add a regression test that exposes the hardcoded Codex selection in `OAuthAutoLoginOrchestrator`.

**Commit:** `test(auth): capture current authentication flow contracts`

### Phase 1: Google authentication boundary

- Add the application-facing Google authentication interface/request boundary.
- Wrap the existing `GoogleLoginStateMachine` and `IGoogleLoginBrowser` without changing selectors or state behavior.
- Route the Main Window Google auto-login delegate through the service.
- Preserve result and secret-redaction behavior.

**Commit:** `refactor(auth): introduce Google authentication foundation`

### Phase 2: Provider OAuth registry and routing

- Introduce provider OAuth request/result and registry/factory abstractions.
- Adapt existing Codex, GitHub, OpenRouter, and AWS Builder ID OAuth implementations.
- Pass the provider identity or adapter into `OAuthAutoLoginOrchestrator`.
- Remove unconditional `CodexOAuthAutomation` construction.
- Reuse the same registry from `AutoLoginOrchestrator`.

**Commit:** `fix(auth): route OAuth through provider adapters`

### Phase 3: Unified callers and batch login

- Make Main Window and Credentials Manager consume the same Google authentication service.
- Preserve Credentials Manager's Google-only policy if that remains its intended UI behavior.
- Unify browser lifecycle, timeout, cancellation, and result mapping at the service boundary.
- Keep direct login on its separate path and add tests proving it never invokes Google authentication.

**Commit:** `refactor(auth): unify Google login callers`

### Phase 4: OpenRouter composition

- Inject/use `IGoogleAuthenticationService` from `OpenRouterKeyFlowOrchestrator`.
- Keep onboarding limited to keys-page wizard and key creation.
- Preserve early success when a key already exists, the exact keys URL, bounded waits, and secret-safe diagnostics.
- Add composition tests proving Google authentication completes before onboarding begins.

**Commit:** `refactor(openrouter): compose key flow with Google foundation`

### Phase 5: Chrome/CDP lifecycle consolidation

- Extract shared target attachment/polling plumbing from `ChromeManagedSession` only after the service boundaries are stable.
- Normalize ownership and cleanup for `ChromeCdpClient`, `CdpSession`, adapters, Chrome process, and temp user-data directories.
- Verify target association, loopback restrictions, and cancellation behavior.

**Commit:** `refactor(chrome): consolidate managed CDP lifecycle`

### Phase 6: Cleanup and guardrails

- Remove compatibility wrappers and duplicate Google selectors after all callers migrate.
- Update XML documentation and dependency registration.
- Add a provider-extension convention requiring Google OAuth adapters to depend on `IGoogleAuthenticationService`.
- Run the full test suite and build.

**Commit:** `chore(auth): remove obsolete authentication overlap`

## 7. Testing strategy

### Google foundation

Cover successful email/password/TOTP flow, already-authenticated state, account picker, invalid credentials, rejected TOTP, manual challenge, passkey/CAPTCHA, wrong origin, cancellation, timeout, browser disconnect, and secret-safe diagnostics.

### Provider routing

For every supported `ProviderKind`, assert that the registry returns the matching adapter. Assert that unsupported providers return an explicit unsupported result and never fall back to Codex or another provider.

### Composition

- Google OAuth provider flows call the Google service.
- Provider-specific continuation starts only after Google authentication succeeds.
- Direct login never calls the Google service.
- OpenRouter onboarding starts only after the OpenRouter page returns from Google authentication.
- Existing API keys avoid unnecessary authentication/onboarding.

### Caller behavior

Cover Main Window interactive login, Main Window batch login, Credentials Manager single login, Credentials Manager batch login, fallback policy, loading-state cleanup, cancellation, and user-visible result mapping.

### Chrome and security

Use fakes for CDP/browser boundaries. Do not use real credentials or Google's live UI in automated tests. Verify loopback-only CDP, intended-target association, no cross-tab secret submission, process/profile cleanup, and absence of passwords, TOTP values, API keys, cookies, tokens, DOM contents, or sensitive URLs in diagnostics.

## 8. Acceptance criteria

1. All Google credential automation callers use one `IGoogleAuthenticationService` implementation.
2. Provider OAuth adapters contain no Google password, TOTP, or account-picker automation.
3. `OAuthAutoLoginOrchestrator` routes by provider and never hardcodes Codex.
4. Main Window and Credentials Manager share the Google authentication implementation.
5. OpenRouter auto-get-key authenticates through the Google foundation before onboarding.
6. Direct provider login remains independent of the Google foundation.
7. Chrome/CDP ownership and cleanup are explicit and tested.
8. Existing OpenRouter OAuth, direct login, and key onboarding capabilities remain intact.
9. No secret appears in logs or other unencrypted diagnostics.
10. Each phase has a separate commit, and the build plus relevant tests pass before that phase is committed.
