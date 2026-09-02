# Provider Login Implementation TODO

## Status: Skeleton Complete ✓

All provider tabs (Codex, Kiro, GitHub, OpenRouter) now have login buttons (🚀).
Codex login skeleton is implemented with TODOs for full automation.

---

## What Works Now

### UI Layer ✓
- Login buttons (🚀) visible in all provider tabs
- Buttons enabled when `HasCredentials == true`
- Click triggers appropriate login command

### ViewModel Layer ✓
- `LoginCodexRowCommand` - implemented with skeleton
- `LoginKiroRowCommand` - stub (shows "not implemented yet")
- `LoginGitHubRowCommand` - stub (shows "not implemented yet")
- `LoginOpenRouterRowCommand` - stub (shows "not implemented yet")

### Models ✓
- `CodexLoginCredential` - credential model with Google OAuth / Direct methods
- `CodexLoginResult` - result model with Success/Failed/Timeout/etc categories
- `AuthMethod` enum - already existed (GoogleOAuth, Direct)

### Infrastructure ✓
- `MainViewModel.CreateDefaultCodexLoginAutomation()` - skeleton method with TODOs
- `MainViewModel.CodexLoginAutomation` - exposed and injected
- `CredentialsManagerViewModel` - receives and uses Codex login runner

---

## What Needs Implementation

### 1. Codex Login Automation (Priority 1)

**Location:** `src/RouterPlus.App/ViewModels/MainViewModel.cs:3411`
**Method:** `CreateDefaultCodexLoginAutomation()`

#### Google OAuth Flow:
1. **Reuse Google session:**
   - Google login already completed by `LoginCodexRowAsync`
   - Need to reuse the Chrome session (stored in `_googleLoginSessions`)
   
2. **Navigate to Codex OAuth:**
   - URL: `https://chatgpt.com/codex` or OAuth authorize URL
   - Or get OAuth URL from router API (requires investigation)

3. **Auto-consent:**
   - Detect "Continue with Google" button
   - Click it automatically
   - Wait for redirect to chatgpt.com

4. **Success detection:**
   - Check final URL contains `chatgpt.com`
   - Or check for Codex-specific page elements

#### Direct Login Flow:
1. **Navigate to OpenAI login:**
   - URL: `https://auth.openai.com/authorize` or login page
   
2. **Auto-fill credentials:**
   - Find and fill email input
   - Find and fill password input
   - Submit form

3. **Handle TOTP (if provided):**
   - Detect TOTP input field
   - Generate TOTP code from secret
   - Fill and submit

4. **Success detection:**
   - Same as Google OAuth flow

#### Reference Pattern:
- See `CreateDefaultGoogleLoginAutomation()` at line 3290
- Similar Chrome session management
- Similar CDP automation pattern
- Similar error handling

---

## Files Modified in This Session

### New Files:
- `src/RouterPlus.Core/Providers/CodexLoginResult.cs`
- `src/RouterPlus.Core/Providers/CodexLoginCredential.cs`
- `PROVIDER_LOGIN_TODO.md` (this file)

### Modified Files:
- `src/RouterPlus.App/ViewModels/MainViewModel.cs`
- `src/RouterPlus.App/ViewModels/CredentialsManagerViewModel.cs`
- `src/RouterPlus.App/MainWindow.xaml.cs`
- `src/RouterPlus.App/Views/CredentialsManagerDialog.xaml`

---

## Build Status

✓ All code compiles with 0 warnings, 0 errors
✓ UI verified - login buttons present in all tabs
✓ Skeleton tested - shows "not implemented yet" messages

---

Generated: 2026-09-02T13:49:42Z
