# Debug Logging System

## Overview

RouterPlus includes a comprehensive debug logging system that provides realtime visibility into app operations with zero overhead in production builds.

## Features

- **Realtime console output** with millisecond timing
- **Zero overhead** in Release builds (compiler strips all log calls)
- **Structured logging** with categories and context
- **Visual Studio Output integration** (Debug.WriteLine)
- **Console window** in Debug builds only

## Architecture

### DebugConsole (Infrastructure Layer)

For automation flow logging:

```csharp
using RouterPlus.Infrastructure.Diagnostics;

DebugConsole.WriteLine("[Component] Message");
DebugConsole.WriteLine($"[Fill] Email - Found: tagName={tag} type={type}");
```

- Uses `[Conditional("DEBUG")]` attribute
- Compiler **completely removes** calls in Release builds
- No runtime overhead, no branching

### DebugLogger (App Layer)

For app lifecycle and UI events:

```csharp
using RouterPlus.App.Diagnostics;

DebugLogger.Log("Category", "Message");
DebugLogger.LogStart("Chrome", "LaunchProfile");
DebugLogger.LogEnd("Chrome", "LaunchProfile", elapsedMs);
DebugLogger.LogError("Security", "Vault unlock failed", exception);
```

- Includes **millisecond timing** since app start: `[00001234ms]`
- Logs to both Console and Visual Studio Output window
- Uses `[Conditional("DEBUG")]` - stripped in Release builds

## Build Configuration

### Debug Build
```xml
<!-- RouterPlus.App.csproj -->
<OutputType Condition="'$(Configuration)' == 'Debug'">Exe</OutputType>
```
- **Console window visible**
- All logs appear in realtime
- Startup timing, UI events, automation flow

### Release Build
```xml
<OutputType Condition="'$(Configuration)' != 'Debug'">WinExe</OutputType>
```
- **No console window**
- All `DebugConsole.WriteLine` and `DebugLogger.*` calls **removed by compiler**
- Zero CPU overhead, smaller binary

## Log Categories

### Automation Flow
- `[ChromeLauncher]` - Chrome process management
- `[ReadState]` - Google page state detection
- `[Fill]` - Form field filling
- `[Submit]` - Button clicks and form submission
- `[GoogleLogin]` - 2FA and speedbump handling
- `[ChromeCdpClient]` - CDP protocol errors

### App Lifecycle
- `[Startup]` - App initialization
- `[Chrome]` - Chrome operations
- `[Security]` - Vault and credential operations
- `[UI]` - User interface events
- `[Settings]` - Configuration loading/saving

## Example Output

```
[00001234ms] [Startup] App initializing...
[00002345ms] [Chrome] Loading profiles from G:\Program Files\CentBrowser\User Data
[00003456ms] [Settings] Settings loaded (45ms)
[ChromeLauncher] Closing Chrome processes using profile: Profile demo.profile@example.com
[ChromeLauncher] Killing process 35680 using profile Profile demo.profile@example.com
[ChromeLauncher] Killed 1 Chrome process(es)
[ReadState] path=https://accounts.google.com/v3/signin/identifier Email=True()
[ReadState] Empty page detected (attempt 1), reloading...
[Fill] Email - Finding visible field with selector: input[type="email"]
[Fill] Email - Inserting text (length=25, masked=demo.profile@example.com)...
[Submit] field=Email submittedByDom=True method=button_click
[ReadState] path=https://accounts.google.com/v3/signin/challenge/pwd Pwd=True
[Fill] Password - Inserting text (length=18, masked=******************)...
[Submit] field=Password submittedByDom=True method=button_click
[GoogleLogin] Using CDP mouse click at (702, 269)
[Fill] Totp - Inserting text (length=6, masked=******)...
[Submit] field=Totp submittedByDom=True method=button_click
[ReadState] path=https://myaccount.google.com/?pli=1
[00045678ms] [Security] Google auto-login result: Success
```

## Benefits

### For Development
- **See what's happening** without debugger
- **Find bottlenecks** with millisecond timing
- **Debug async flows** that are hard to step through
- **Reproduce user issues** by analyzing logs

### For Production
- **Zero overhead** - all logging code removed
- **Smaller binary** - no string constants for log messages
- **No branching** - compiler optimization, not runtime checks

### For Troubleshooting
- User encounters bug → switch to Debug build
- Reproduce issue → copy console output
- Send logs to developer → full context available

## Industry Standard

This pattern is used by:
- ✅ **Visual Studio** - Output window with timing
- ✅ **JetBrains IDEs** - Event Log panel
- ✅ **Chrome DevTools** - Console with performance marks
- ✅ **Electron apps** - DevTools console
- ✅ **Android/iOS** - logcat/os_log

## Implementation Details

### Conditional Compilation
```csharp
[Conditional("DEBUG")]
public static void WriteLine(string message)
{
    Console.WriteLine(message);
}
```

The `[Conditional("DEBUG")]` attribute tells the C# compiler:
- **Debug build** (`#define DEBUG`): Include method calls
- **Release build** (no `#define DEBUG`): **Remove method calls entirely**

This is **compile-time**, not runtime:
```csharp
// Debug build compiles to:
DebugConsole.WriteLine("[Component] Message");
Console.WriteLine("[Component] Message");

// Release build compiles to:
// (nothing - the entire line is gone)
```

### Performance Impact
- **Debug**: Minimal (Console.WriteLine is fast)
- **Release**: **Zero** (code doesn't exist)

No `if (DEBUG)` checks needed. The compiler does it for you.

## Adding New Logs

### Automation Flow (Infrastructure)
```csharp
using RouterPlus.Infrastructure.Diagnostics;

DebugConsole.WriteLine($"[Component] Operation: {details}");
```

### App Lifecycle (App)
```csharp
using RouterPlus.App.Diagnostics;

DebugLogger.Log("Category", $"Operation completed: {result}");
```

### Performance Measurement
```csharp
using var perf = DebugLogger.MeasurePerformance("Category", "OperationName");
// ... do work ...
// Automatically logs duration on dispose
```

## Best Practices

1. **Use prefix tags**: `[Component]` makes logs grep-able
2. **Include context**: URLs, file names, field names
3. **Mask sensitive data**: `masked=******************`
4. **Log state transitions**: "Email=True() → Email=False(email)"
5. **Log timing**: Helps identify bottlenecks
6. **Don't over-log**: Every line adds noise

## See Also

- [E2E Test Coverage](../tests/RouterPlus.App.E2E/COVERAGE.md) - Verified with debug logging
- [Developer Harness](developer-harness.md) - Testing infrastructure
