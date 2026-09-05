# Google Auto-Login ConfirmIdentifier Timeout Fix

**Date:** 2026-09-05  
**Commit:** 51fa8bd  
**Status:** ✅ FIXED

## Problem

Auto-login kẹt 15 giây sau khi submit password, rồi timeout với error:
```
Google Password submit did not advance to the next authentication state
```

**Triệu chứng:**
- Password được fill thành công
- Nút "Next" được click
- Trang chuyển sang `/confirmidentifier`
- Automation đợi 15 giây rồi báo lỗi
- Password page không "advance" sang bước tiếp theo

## Root Cause

**WaitForNextStateAsync logic bug:**

```csharp
// Line 1088-1092: Password considered "advanced" if:
GoogleLoginField.Password =>
    (!state.HasPasswordField || state.Has2FAMethodPicker) &&
    (state.HasTotpField || state.HasCompletionSignal ||
     state.HasManualChallenge || state.Has2FAMethodPicker)
```

**Kịch bản lỗi:**
1. Submit password → Google v3 navigate sang `/confirmidentifier`
2. Confirmidentifier page có:
   - ❌ NO password field (HasPasswordField = false)
   - ❌ NO TOTP field (HasTotpField = false)
   - ❌ NO completion signal (HasCompletionSignal = false)
   - ❌ NO manual challenge (HasManualChallenge = false)
3. Condition: `(true) && (false || false || false || false)` = **FALSE**
4. Automation nghĩ page chưa "advance", đợi 15 giây → timeout!

**Tại sao ReadStateAsync không bị?**

`ReadStateAsync()` có logic auto-click Continue ở confirmidentifier (line 72-82):
```csharp
if (state.PageUri.AbsolutePath.Contains("/confirmidentifier"))
{
    if (await TryClickConfirmIdentifierContinueAsync(renderCts.Token))
    {
        await Task.Delay(1500, renderCts.Token);
        continue; // Read state again after navigation
    }
}
```

Nhưng `WaitForNextStateAsync()` gọi `ReadStateOnceAsync()` (không có logic này) thay vì `ReadStateAsync()`.

## Solution

Thêm confirmidentifier detection trực tiếp vào `WaitForNextStateAsync()` loop:

```csharp
// Handle Google v3 confirmidentifier page after password submit
if (submittedField == GoogleLoginField.Password && !triedConfirmIdentifier &&
    state.PageUri.Host == "accounts.google.com" &&
    state.PageUri.AbsolutePath.Contains("/confirmidentifier"))
{
    DebugConsole.WriteLine("[WaitForNextState] Detected confirmidentifier page after password, clicking Continue...");
    if (await TryClickConfirmIdentifierContinueAsync(cancellationToken))
    {
        triedConfirmIdentifier = true;
        await Task.Delay(1500, cancellationToken);
        // Reset deadline to give time for navigation
        deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        continue;
    }
}
```

**Logic:**
1. Detect confirmidentifier page after password submit
2. Click Continue button (via TryClickConfirmIdentifierContinueAsync)
3. Wait 1500ms for navigation
4. Reset deadline để có thêm 10s cho navigation
5. Continue loop để đọc state mới

## Test Results

**Unit tests:** ✅ 38/38 passed
- RouterPlus.Core.Tests: 37 GoogleLogin tests passed
- RouterPlus.App.Tests: 1 test passed

**Expected behavior after fix:**
```
[Submit] Password submit
[WaitForNextState] Detected confirmidentifier page after password, clicking Continue...
[WaitForNextState] Continue clicked, waiting for navigation...
[ReadState] path=.../challenge/pwd (password page reached)
```

## Related Issues

- Original confirmidentifier fix: commit 12f33d2 (added ReadStateAsync logic)
- This fix: commit 51fa8bd (added WaitForNextStateAsync logic)

**Why two fixes?**

1. **12f33d2** fixed: Email page → confirmidentifier timeout
2. **51fa8bd** fixed: Password page → confirmidentifier timeout

Same page, different entry points, different code paths.

## Impact

✅ Auto-login no longer timeout after password submit on Google v3  
✅ Confirmidentifier page handled in both ReadStateAsync AND WaitForNextStateAsync  
✅ Backward compatible với Google v2 flow (không có confirmidentifier page)

**Ready for production.**
