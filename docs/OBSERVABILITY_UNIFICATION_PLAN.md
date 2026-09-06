# Observability System Unification Plan

**Status:** 🟡 Paused - Partial Migration (2/8 files completed)  
**Date:** 2026-09-06  
**Priority:** Medium  
**Note:** ObservabilityHub is production-ready và được sử dụng trong tất cả core business logic. DebugLogger vẫn còn trong UI layer chỉ cho debug builds.

## Problem Statement

The codebase has **3 different logging systems** operating simultaneously:

1. **ObservabilityHub** (227 usages, 24 files) - Official structured logging system
2. **DebugLogger** (9 files) - Legacy conditional debug logging with `[Conditional("DEBUG")]`
3. **DebugConsole** (1 file) - Obsolete legacy system

This creates:
- **Architectural inconsistency** - unclear which system to use
- **Incomplete observability** - `DebugLogger` logs disappear in Release builds
- **Developer confusion** - mixing patterns across codebase
- **Maintenance burden** - 3 systems to maintain

## Current State Analysis

### ObservabilityHub Usage (✅ Production Ready)
```
24 files, 227 log events
- Always active (Debug + Release)
- Structured JSON logging
- Privacy scrubbing built-in
- Session-based log files
- UI integration (Diagnostics Panel)
```

**Files:** All core business logic, automation flows, OAuth

### DebugLogger Usage (⚠️ Debug-Only)
```
9 files still using DebugLogger
- Compiled out in Release builds (#if DEBUG)
- Simple text logging to console
- No structured data
- Lost in production
```

**Files:**
1. `src\RouterPlus.App\App.xaml.cs`
2. `src\RouterPlus.App\MainWindow.xaml.cs`
3. `src\RouterPlus.App\ViewModels\MainViewModel.cs`
4. `src\RouterPlus.App\ViewModels\GoogleAutoLoginViewModel.cs`
5. `src\RouterPlus.App\Diagnostics\DebugLogger.cs` (definition)
6. `src\RouterPlus.App\Views\ChromeSelectionDialog.xaml.cs`
7. `src\RouterPlus.App\ViewModels\RelayCommand.cs`
8. `src\RouterPlus.App\ViewModels\AsyncRelayCommand.cs`
9. `src\RouterPlus.App\Diagnostics\DiagnosticHelpers.cs`

### DebugConsole (❌ Obsolete)
```
1 file: GoogleLoginCdpBrowser.cs - commented-out calls
Status: Should be removed completely
```

## Decision: Standardize on ObservabilityHub

**Rationale:**
- ✅ Already used in 91% of logging locations
- ✅ Works in all build configurations
- ✅ Structured data for analysis
- ✅ Privacy-aware by default
- ✅ UI integration available
- ✅ Session-based log files persist

**DebugLogger should be removed** - It conflicts with the production observability strategy.

## Migration Plan

### Phase 1: Migrate DebugLogger Calls to ObservabilityHub

**Target Files (8 files, excluding definition):**

1. **App.xaml.cs**
   - Application lifecycle events
   - Startup/shutdown logging
   - Category: "Application"

2. **MainWindow.xaml.cs**
   - Window lifecycle
   - UI initialization
   - Category: "MainWindow"

3. **MainViewModel.cs**
   - ViewModel lifecycle
   - Command execution
   - Category: "MainViewModel"

4. **GoogleAutoLoginViewModel.cs**
   - Auto-login UI operations
   - User interactions
   - Category: "GoogleAutoLoginUI"

5. **ChromeSelectionDialog.xaml.cs**
   - Dialog operations
   - User selection
   - Category: "ChromeSelection"

6. **RelayCommand.cs** & **AsyncRelayCommand.cs**
   - Command execution
   - Error handling
   - Category: "Commands"

7. **DiagnosticHelpers.cs**
   - Diagnostic utilities
   - Category: "Diagnostics"

**Migration Pattern:**

```csharp
// OLD (DebugLogger)
DebugLogger.Log("Category", "Something happened");
DebugLogger.LogError("Category", "Error occurred", exception);

// NEW (ObservabilityHub)
ObservabilityHub.Instance.LogEvent(
    LogLevel.Info,
    "Category",
    "EventName",
    "Something happened",
    new { contextKey = contextValue });

ObservabilityHub.Instance.LogEvent(
    LogLevel.Error,
    "Category",
    "ErrorEventName",
    $"Error occurred: {exception.Message}",
    new { error = exception.Message, stack_trace = exception.StackTrace });
```

### Phase 2: Handle Special Cases

#### DebugAutoLoginRunner.cs
**Decision:** Keep as-is - it's a debug-only test harness that writes to Console by design.

**Rationale:**
- Activated only by `ROUTERPLUS_DEBUG_AUTOLOGIN=1`
- Not part of production code path
- Console output is the primary interface
- Can optionally ADD ObservabilityHub calls for consistency

#### DebugLogger.cs (Definition)
**Decision:** Mark as `[Obsolete]` first, then delete after migration.

**Steps:**
1. Add `[Obsolete("Use ObservabilityHub.Instance.LogEvent instead")]` to all methods
2. Migrate all callers
3. Delete the file

### Phase 3: Remove DebugConsole References

**File:** `GoogleLoginCdpBrowser.cs`

**Action:** Remove all commented-out DebugConsole calls (already done per docs, verify)

### Phase 4: Documentation Update

Update `docs/observability-system.md`:
- Remove any DebugLogger mentions
- Add "Deprecated Systems" section
- Clear migration guide
- Single source of truth: ObservabilityHub

## Verification Checklist

- [ ] All 8 files migrated from DebugLogger to ObservabilityHub
- [ ] `DebugLogger.cs` marked `[Obsolete]`
- [ ] No remaining `DebugLogger.` calls in active code
- [ ] No `DebugConsole.` calls anywhere
- [ ] Documentation updated
- [ ] Build succeeds (Debug + Release)
- [ ] Diagnostics Panel shows all events
- [ ] Tests pass

## Benefits After Unification

✅ **Single logging system** - clear developer guidance  
✅ **Production visibility** - all logs available in Release builds  
✅ **Structured data** - better analysis and debugging  
✅ **Privacy compliance** - automatic scrubbing  
✅ **UI integration** - Diagnostics Panel works for all logs  
✅ **Reduced maintenance** - one system to maintain

## Timeline

**Estimated effort:** 2-3 hours
- Phase 1 (migrate): 1.5 hours
- Phase 2 (special cases): 0.5 hours
- Phase 3 (cleanup): 0.5 hours
- Phase 4 (docs): 0.5 hours

## Next Steps

1. Start with Phase 1 file-by-file migration
2. Test after each file
3. Mark DebugLogger obsolete
4. Final cleanup and verification
