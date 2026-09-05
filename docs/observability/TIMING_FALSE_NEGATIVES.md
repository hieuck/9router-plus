# Observability Timing False Negatives

## Issue: Premature State Check Giving False "Stuck" Signal

**Date:** 2026-09-05

### What Happened

During Google auto-login testing at 09:56:12-09:56:30:
- 09:56:18 - System clicked "Continue" button on confirmidentifier page
- 09:56:21 - System checked state after 3 seconds, logged "ConfirmIdentifierStuck" warning
- 09:56:30 - **Login actually SUCCEEDED** (9 seconds after click)

### Root Cause

**Premature state verification** - checking too soon after action:

```csharp
// BEFORE (WRONG):
await TryClickConfirmIdentifierContinueAsync(renderCts.Token);
await Task.Delay(3000, renderCts.Token);  // ❌ Too short

// Check state immediately
var checkState = await ReadStateOnceAsync(renderCts.Token);
if (checkState.PageUri.AbsolutePath.Contains("/confirmidentifier"))
{
    // ❌ FALSE NEGATIVE - Google just needs more time!
    ObservabilityHub.Instance.LogEvent(LogLevel.Warning, "GoogleLogin", 
        "ConfirmIdentifierStuck", "Page still stuck...");
}
```

**Reality:** Google authentication can take 5-10 seconds to process after clicking Continue. Checking at 3 seconds gave false "stuck" signal while operation was still in progress.

### The Fix

**Trust the polling loop** - don't verify immediately after action:

```csharp
// AFTER (CORRECT):
await TryClickConfirmIdentifierContinueAsync(renderCts.Token);
// ✅ Don't check state immediately
// ✅ Let natural polling loop detect navigation when it happens
// ✅ No false negatives
```

### Lesson Learned

**Observability timing rules:**

1. **Don't verify actions immediately** - external systems need processing time
2. **Trust polling loops** - they'll detect state changes when they happen naturally
3. **False negatives are worse than no signal** - they create confusion and wrong diagnosis
4. **Google timing:** 5-10 seconds for authentication steps is normal, not stuck

### Impact

- **Before:** False "stuck" warnings led to unnecessary code changes (trying Enter key, etc.)
- **After:** Clean success detection, accurate metrics, correct root cause analysis

### Related Events

- `ConfirmIdentifierDetected` - Page reached, valid signal
- `ConfirmIdentifierContinueClicked` - Action taken, valid signal
- `ConfirmIdentifierStuck` - **REMOVED** - was false negative
- Login success detected naturally by polling loop

### Verification Pattern

When you need to verify an action:

```csharp
// ❌ DON'T: Immediate check after action
await PerformAction();
await Task.Delay(shortTime);
if (not changed) { log error; }  // False negative!

// ✅ DO: Let polling loop detect naturally
await PerformAction();
// Polling loop will detect state change when it happens
// Or timeout will trigger if truly stuck
```
