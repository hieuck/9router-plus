# Google Auto-Login: Direct Execution from Context Menu

**Date:** 2026-09-05  
**Purpose:** Convert Google auto-login to direct execution without dialog, using Credentials vault as single source of truth

## Changes Made

### 1. MainViewModel - New Direct Auto-Login Method

**File:** `src/RouterPlus.App/ViewModels/MainViewModel.cs`

**Added method:** `RunGoogleAutoLoginDirectAsync()`

**Behavior:**
1. Checks if profile is selected
2. Attempts to open vault with remembered key
3. If vault locked → Shows error message directing user to Credentials Manager
4. If no credentials found → Shows error message directing user to Credentials Manager
5. If credentials found → Runs auto-login automation directly
6. Updates status based on result (success/manual intervention/failure)

**Key Features:**
- ✅ No dialog shown - runs directly
- ✅ Single source of truth: Credentials vault only
- ✅ Vietnamese status messages for user feedback
- ✅ Full observability instrumentation (TraceScope, events, counters)
- ✅ Automatic vault session cleanup (await using)

**Error Messages:**
- Vault locked: "❌ Vault chưa được mở khóa. Vui lòng mở Credentials Manager để thiết lập."
- No credentials: "❌ Không tìm thấy thông tin đăng nhập cho {profile}. Vui lòng thêm trong Credentials Manager."
- Login success: "✓ Đăng nhập Google thành công cho {profile}"
- Manual intervention: "⚠ {profile}: Cần can thiệp thủ công - {message}"
- Login failed: "❌ {profile}: Đăng nhập thất bại - {message}"

### 2. MainWindow - Context Menu Handler Update

**File:** `src/RouterPlus.App/MainWindow.xaml.cs`

**Modified method:** `ProfileGoogleAutoLogin_Click`

**Changes:**
- Method signature: `void` → `async void` (required for async call)
- Removed: Dialog creation and ShowDialog() call
- Removed: UIEventLogger.LogDialogOpen/Close calls
- Added: Direct call to `ViewModel.RunGoogleAutoLoginDirectAsync()`

## User Flow

### Before (With Dialog)
1. User right-clicks profile → "Tự động đăng nhập Google"
2. **Dialog opens** with vault unlock fields
3. User enters vault password
4. User clicks "Auto Login" button
5. Dialog shows progress and result

### After (Direct Execution)
1. User right-clicks profile → "Tự động đăng nhập Google"
2. **No dialog** - automation starts immediately
3. Status bar shows progress
4. Status bar shows final result

## Credentials Source - Single Source of Truth

### ✅ Vault as Only Source
All credential operations now use `IGoogleAccountVaultStore`:
- **Load:** `session.Vault.Find(profileId)` - finds credential by stable profile ID
- **Save:** Done in Credentials Manager only
- **Update:** Done in Credentials Manager only
- **Delete:** Done in Credentials Manager only

### Flow Diagram
```
User Right-Click
    ↓
ProfileGoogleAutoLogin_Click
    ↓
RunGoogleAutoLoginDirectAsync()
    ↓
Open Vault (TryOpenRememberedAsync)
    ↓
Find Credential (vault.Find(profileId))
    ↓
    ├─ Not found → Error: "Add in Credentials Manager"
    ├─ Vault locked → Error: "Unlock in Credentials Manager"
    └─ Found → Run automation with credential
                    ↓
                    ├─ Success → "✓ Login successful"
                    ├─ Manual intervention → "⚠ Manual intervention required"
                    └─ Failed → "❌ Login failed"
```

## Observability

### Events Logged
- `AutoLoginVaultLocked` - Vault not unlocked
- `AutoLoginNoCredentials` - No credentials for profile
- `AutoLoginStarted` - Starting direct auto-login
- `AutoLoginDirectFailed` - Exception during auto-login

### Metrics
- Counter: `autologin.direct` (tags: result)
- Histogram: `GoogleAutoLoginDirect` operation timing

### TraceScope
- Operation: `GoogleAutoLoginDirect`
- Checkpoint: `CredentialsLoaded` (email, has_totp)

## Testing Scenarios

### Happy Path
1. User opens Credentials Manager
2. User unlocks vault and saves Google credentials
3. User right-clicks profile → "Tự động đăng nhập Google"
4. Automation runs automatically
5. Status shows "✓ Đăng nhập Google thành công"

### Error: Vault Locked
1. User right-clicks profile → "Tự động đăng nhập Google"
2. Vault is locked (no remembered key)
3. Status shows: "❌ Vault chưa được mở khóa. Vui lòng mở Credentials Manager để thiết lập."
4. User opens Credentials Manager, unlocks vault with "Remember on device"
5. User tries again → Works

### Error: No Credentials
1. Vault is unlocked but profile has no saved credentials
2. User right-clicks profile → "Tự động đăng nhập Google"
3. Status shows: "❌ Không tìm thấy thông tin đăng nhập cho {profile}. Vui lòng thêm trong Credentials Manager."
4. User opens Credentials Manager, adds credentials
5. User tries again → Works

## Build Status

✅ Compilation successful
- RouterPlus.Core: No changes
- RouterPlus.Infrastructure: No changes  
- RouterPlus.App: Compiles successfully

## Summary

The Google auto-login feature now:
1. ✅ Works directly from context menu without showing dialog
2. ✅ Uses Credentials vault as single source of truth
3. ✅ Provides clear Vietnamese error messages
4. ✅ Maintains full observability
5. ✅ Preserves security and privacy
6. ✅ Guides users to Credentials Manager when needed
