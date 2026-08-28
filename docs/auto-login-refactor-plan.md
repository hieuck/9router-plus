# Auto-Login Refactor Plan

**Created:** 2026-08-28  
**Status:** Planning  
**Context:** User request "lên kế hoạch chi tiết cho vấn đề AutoLogin này" after commit 6839e95

**Importers/Callers:**
- `MainViewModel.cs` calls `AwsBuilderIdOAuthAutomation` (Kiro flow)
- `MainViewModel.cs` calls `CodexOAuthAutomation` (Codex flow - existing)
- `GoogleLoginCdpBrowser` used for full Google login

**Affected API:**
- NEW: `GoogleOAuthHelpers.cs` (static helpers for account picker, consent buttons, TOTP)
- NEW records: `AccountPickerOptions`, `AccountPickerResult`, `ConsentButtonOptions`
- MODIFIED: `AwsBuilderIdOAuthAutomation.cs` (call helpers)
- MODIFIED: `CodexOAuthAutomation.cs` (call helpers)

---

## 1. Current Situation

Có 3 file automation với chức năng chồng chéo:

### 1.1. GoogleLoginCdpBrowser.cs (1384 lines)
- **Mục đích:** Full Google login automation (email → password → TOTP → completion)
- **Use case:** Đăng nhập vào Google account hoàn chỉnh
- **Key features:**
  - Fill email, password, TOTP fields
  - Handle 2FA method picker
  - Skip passkey enrollment speedbump
  - Skip home address collection speedbump
  - Bypass account chooser (navigate to identifier page)

### 1.2. CodexOAuthAutomation.cs (394 lines)
- **Mục đích:** OAuth consent automation cho Codex (OpenAI)
- **Use case:** Click consent buttons khi đã đăng nhập Google
- **Key features:**
  - Detect Google OAuth pages
  - Detect OpenAI OAuth pages
  - Click account picker (match email)
  - Click Google consent button (Continue/Allow)
  - No TOTP (assumes already logged in)

### 1.3. AwsBuilderIdOAuthAutomation.cs (642 lines)
- **Mục đích:** OAuth consent automation cho AWS Builder ID (Kiro)
- **Use case:** Full flow từ AWS SSO page → Google login → AWS consent
- **Key features:**
  - Click "Continue with Google" on AWS page
  - Click account picker (match email, skip AWS SSO identity, fallback scroll)
  - Fill TOTP if needed
  - Click Google consent button
  - Click AWS Builder ID consent button

---

## 2. Code Overlap Analysis

### 2.1. Duplicated Logic (High Priority)

#### A. **Account Picker Selection**
**Location:**
- `AwsBuilderIdOAuthAutomation.TryClickAccountAsync` (lines 356-471) - 116 lines
- `CodexOAuthAutomation.TryClickAccountAsync` (lines 225-296) - 72 lines

**Overlap:**
- Find buttons matching `targetEmail`
- Filter visible elements
- Click via JavaScript or CDP mouse events
- Return boolean success

**Differences:**
- AWS version: broader selector (`li[data-email], div[data-email]`), skips AWS SSO identity, scroll fallback
- Codex version: simpler, only `button, [role="button"]`

**Refactor target:** ~80% overlap, extract to shared helper

---

#### B. **Google Consent Button Click**
**Location:**
- `AwsBuilderIdOAuthAutomation.TryClickGoogleConsentButtonAsync` (lines 536-581) - 46 lines
- `CodexOAuthAutomation.TryClickConsentButtonAsync` (lines 299-376) - 78 lines

**Overlap:**
- Keywords: "continue", "tiếp tục", "allow", "cho phép", "accept", "chấp nhận"
- Find button via JavaScript
- Click via CDP mouse events or element.click()

**Differences:**
- AWS: uses element.click() in JavaScript
- Codex: uses CDP mouse events

**Refactor target:** ~90% overlap, extract to shared helper

---

#### C. **TOTP Fill Logic**
**Location:**
- `AwsBuilderIdOAuthAutomation.TryFillTotpAsync` (lines 473-534) - 62 lines
- `GoogleLoginCdpBrowser.FillAsync` (TOTP case, lines 830-977) - 147 lines

**Overlap:**
- Selector: `input[name="totpPin"]` or `input[type="tel"]`
- Fill value
- Dispatch input/change events
- Submit

**Differences:**
- AWS: simpler, inline script
- GoogleLoginCdpBrowser: extensive focus/clear/verify logic, separate Submit method

**Refactor target:** ~50% overlap, consider extracting core logic only

---

#### D. **isVisible Helper**
**Location:** Inline in JavaScript in all 3 files

**Code:**
```javascript
const isVisible = el => {
    if (!el) return false;
    const rect = el.getBoundingClientRect();
    return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
};
```

**Refactor target:** 100% duplicate, extract to constant/helper

---

### 2.2. Similar Patterns (Medium Priority)

#### E. **Page State Detection**
- All 3 files have `ReadOAuthStateAsync` or `ReadStateOnceAsync`
- Different detection logic (AWS pages vs Google OAuth vs Google login forms)
- Could extract common structure (URL detection, button detection pattern)

#### F. **Main Loop Pattern**
- `WaitAndConsentAsync` in OAuth automations
- Similar structure: deadline, polling loop, screen deduplication (`clickedScreenUrls`)
- Could extract template method pattern

---

### 2.3. Unique Logic (Keep Separate)

#### AWS Builder ID Specific:
- Detect AWS Builder ID pages (`view.awsapps.com`, `us-east-1.signin.aws`)
- Click "Continue with Google" button on AWS page
- Click AWS consent buttons ("Confirmer et continuer", "Autoriser l'accès")
- Skip AWS SSO identity in account picker

#### Codex Specific:
- Detect OpenAI OAuth pages (`auth.openai.com`)
- Detect target service completion (`chatgpt.com`, `openai.com`)

#### GoogleLoginCdpBrowser Specific:
- Full login flow (email → password)
- 2FA method picker selection
- Skip passkey enrollment
- Skip home address collection
- Extensive diagnostics capture

---

## 3. Refactor Goals

### 3.1. Primary Goals
1. **Eliminate duplication** in account picker and consent button logic
2. **Maintain current behavior** - no functional changes
3. **Improve maintainability** - fix bugs once, apply to all
4. **Preserve debugging** - keep DebugConsole logging

### 3.2. Non-Goals
1. **Do NOT merge all 3 files** - they serve different use cases
2. **Do NOT over-abstract** - avoid speculative generalization
3. **Do NOT break existing code** - all tests must pass

---

## 4. Proposed Architecture

### 4.1. New Structure

```
src/RouterPlus.Infrastructure/Chrome/
├── GoogleOAuthHelpers.cs              (NEW - shared OAuth helpers)
├── AwsBuilderIdOAuthAutomation.cs     (REFACTOR - use helpers)
├── CodexOAuthAutomation.cs            (REFACTOR - use helpers)
├── GoogleLoginCdpBrowser.cs           (KEEP AS-IS for Phase 1)
└── ChromeCdpClient.cs                 (existing)
```

### 4.2. GoogleOAuthHelpers.cs (NEW FILE)

```csharp
namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Shared helpers for Google OAuth consent automation.
/// Used by CodexOAuthAutomation and AwsBuilderIdOAuthAutomation.
/// </summary>
internal static class GoogleOAuthHelpers
{
    // JavaScript helpers
    public static string IsVisibleScript { get; }
    
    // Account picker
    public static Task<AccountPickerResult> TryClickAccountAsync(
        ChromeCdpClient client,
        string sessionId,
        string targetEmail,
        AccountPickerOptions options,
        CancellationToken cancellationToken);
    
    // Consent button
    public static Task<bool> TryClickConsentButtonAsync(
        ChromeCdpClient client,
        string sessionId,
        ConsentButtonOptions options,
        CancellationToken cancellationToken);
    
    // TOTP fill (simple version for OAuth)
    public static Task<bool> TryFillTotpAsync(
        ChromeCdpClient client,
        string sessionId,
        string totpCode,
        CancellationToken cancellationToken);
}

public sealed record AccountPickerOptions(
    bool SkipAwsSsoIdentity = false,
    bool EnableScrollFallback = false,
    string[]? AdditionalSelectors = null);

public sealed record AccountPickerResult(
    bool Clicked,
    bool Found,
    int TotalButtons,
    string? ClickedText);

public sealed record ConsentButtonOptions(
    string[]? AdditionalKeywords = null);
```

---

## 5. Implementation Plan

### Phase 1: Extract Google OAuth Helpers (Priority 1)

**Effort:** 3-4 hours  
**Risk:** Low (pure extraction, no behavior change)

#### Step 1.1: Create GoogleOAuthHelpers.cs
- Extract `IsVisibleScript` constant
- Extract `TryClickAccountAsync` with options
- Extract `TryClickConsentButtonAsync`
- Extract `TryFillTotpAsync` (simple version)

#### Step 1.2: Refactor AwsBuilderIdOAuthAutomation
- Replace `TryClickAccountAsync` with helper call (pass `SkipAwsSsoIdentity: true, EnableScrollFallback: true`)
- Replace `TryClickGoogleConsentButtonAsync` with helper call
- Replace `TryFillTotpAsync` with helper call
- Keep `TryClickContinueWithGoogleAsync` (AWS-specific)
- Keep `TryClickAwsConsentButtonAsync` (AWS-specific)
- Keep `ReadOAuthStateAsync` (AWS-specific detection)

#### Step 1.3: Refactor CodexOAuthAutomation
- Replace `TryClickAccountAsync` with helper call (default options)
- Replace `TryClickConsentButtonAsync` with helper call
- Keep `ReadOAuthStateAsync` (OpenAI-specific detection)

#### Step 1.4: Verify
- Build successful
- Run manual tests:
  - Add Kiro connection (AWS Builder ID flow)
  - Add Codex connection (OpenAI OAuth flow)
- Check logs for expected behavior
- Verify connections created successfully

---

### Phase 2: Google Login Consolidation (Priority 2)

**Effort:** 4-6 hours  
**Risk:** Medium (GoogleLoginCdpBrowser is complex)

**Decision:** Defer to Phase 2

GoogleLoginCdpBrowser serves a **different use case** (full login) vs OAuth consent automation. Evaluate whether to:

**Option A:** Extract TOTP fill logic into shared helper (GoogleLoginCdpBrowser uses it)
**Option B:** Keep separate for now (different complexity levels)

**Recommendation:** Start with Phase 1, re-evaluate after seeing helper usage patterns.

---

### Phase 3: Template Method Pattern (Priority 3)

**Effort:** 2-3 hours  
**Risk:** Medium (changes main loop structure)

**Decision:** Defer to Phase 3

Extract common `WaitAndConsentAsync` loop pattern:
- Deadline polling loop
- Screen deduplication (clickedScreenUrls)
- State detection dispatch

**Recommendation:** Only if we see more OAuth providers added (e.g., GitHub, GitLab).

---

## 6. Success Criteria

### Phase 1 Complete When:
1. ✅ `GoogleOAuthHelpers.cs` created with 3 helper methods
2. ✅ `AwsBuilderIdOAuthAutomation.cs` reduced by ~150 lines
3. ✅ `CodexOAuthAutomation.cs` reduced by ~100 lines
4. ✅ All builds pass
5. ✅ Manual test: Add Kiro connection succeeds (log shows helper calls)
6. ✅ Manual test: Add Codex connection succeeds
7. ✅ No behavior changes (same log output patterns)

### Known Risks:
1. **CDP timing** - helper extraction might change timing, add delays if needed
2. **Error handling** - ensure exceptions propagate correctly
3. **Logging** - maintain DebugConsole output for troubleshooting

---

## 7. Decision Log

### Decision 1: Keep GoogleLoginCdpBrowser Separate (Phase 1)
**Rationale:** Different use case (login vs OAuth consent), high complexity, low overlap with OAuth automation.

### Decision 2: Use static helpers vs base class
**Rationale:** OAuth automation classes don't share state, static helpers avoid inheritance complexity.

### Decision 3: Pass ChromeCdpClient explicitly vs wrap
**Rationale:** Avoid creating new abstraction, helpers are thin wrappers over CDP calls.

---

## 8. Next Steps

1. **Immediate:** Get user approval on Phase 1 plan
2. **Then:** Create `GoogleOAuthHelpers.cs` with 3 methods
3. **Then:** Refactor `AwsBuilderIdOAuthAutomation.cs`
4. **Then:** Refactor `CodexOAuthAutomation.cs`
5. **Then:** Build + manual test both flows
6. **Then:** Commit with detailed message
7. **Then:** Plan Phase 2 (if needed)

---

## 9. Open Questions

1. **Q:** Should TOTP fill be in helpers? It's only used by AWS Builder ID (OAuth) vs GoogleLoginCdpBrowser (login).
   **A:** Extract simple version for OAuth. GoogleLoginCdpBrowser can optionally use it later.

2. **Q:** Should we extract page state detection pattern?
   **A:** Not in Phase 1. Detection logic is provider-specific (AWS domains vs OpenAI domains).

3. **Q:** Should we add unit tests for helpers?
   **A:** Not in Phase 1. These are integration-level (CDP automation), manual E2E tests are sufficient.

---

## 10. Estimated Effort

| Phase | Task | Effort | Risk |
|-------|------|--------|------|
| Phase 1.1 | Create GoogleOAuthHelpers.cs | 1-2h | Low |
| Phase 1.2 | Refactor AwsBuilderIdOAuth | 1h | Low |
| Phase 1.3 | Refactor CodexOAuth | 30m | Low |
| Phase 1.4 | Verify + test | 1h | Low |
| **Total Phase 1** | | **3-4h** | **Low** |

---

## Appendix: Code Metrics

### Before Refactor:
- AwsBuilderIdOAuthAutomation: 642 lines
- CodexOAuthAutomation: 394 lines
- GoogleLoginCdpBrowser: 1384 lines (not touched Phase 1)
- **Total:** 2420 lines

### After Phase 1 (Estimated):
- GoogleOAuthHelpers: ~200 lines (new)
- AwsBuilderIdOAuthAutomation: ~490 lines (-150)
- CodexOAuthAutomation: ~290 lines (-100)
- GoogleLoginCdpBrowser: 1384 lines (unchanged)
- **Total:** 2364 lines (-56 lines, but better maintainability)

**Net reduction:** ~56 lines, but main benefit is **single source of truth** for account picker and consent logic.
