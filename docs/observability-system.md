# Observability System

**Status:** ✅ Production Ready  
**Last Updated:** 2026-09-06

## Overview

RouterPlus uses **ObservabilityHub** - a centralized structured logging system that writes JSON events to persistent log files. This replaced the legacy DebugConsole system (Aug 2026).

## Architecture

### ObservabilityHub (Core Layer)

Singleton service for structured event logging:

```csharp
using RouterPlus.Core.Observability;

ObservabilityHub.Instance.LogEvent(
    LogLevel.Info,
    "OAuthAutoLogin",
    "NavigatingToAuthUrl",
    "Navigating to auth URL",
    new { 
        auth_url = authUrl, 
        provider = provider.ToString() 
    });
```

**Features:**
- Structured JSON events with context objects
- Privacy scrubbing (credentials, emails automatically redacted)
- Session-based log files
- EventMetrics tracking
- Snapshot scheduler for periodic state capture

### Log Files Location

```
C:\Users\{username}\AppData\Local\RouterPlus\Observability\Sessions\{session_id}\events.jsonl
```

Each line is a JSON event:
```json
{"timestamp":"2026-09-06T12:00:00Z","level":"Info","category":"OAuthAutoLogin","event":"NavigatingToAuthUrl","message":"Navigating to auth URL","context":{"auth_url":"https://...","provider":"Codex"}}
```

## Usage Patterns

### Basic Logging

```csharp
// Info level
ObservabilityHub.Instance.LogEvent(
    LogLevel.Info,
    "Category",
    "EventName",
    "Human readable message",
    new { key = value });

// Error with exception
ObservabilityHub.Instance.LogEvent(
    LogLevel.Error,
    "Category",
    "EventName",
    $"Error: {ex.Message}",
    new { error = ex.Message, stack_trace = ex.StackTrace });
```

### Common Categories

- **OAuthAutoLogin** - OAuth automation flow
- **GoogleLogin** - Google authentication
- **ChromeLauncher** - Chrome process management
- **ProviderVault** - Credential storage
- **AutoLoginOrchestrator** - Login orchestration
- **CodexOAuth** - Codex-specific OAuth

### Privacy Scrubbing

Automatic redaction of sensitive data:
- Email addresses → `[EMAIL_REDACTED]`
- Passwords → `[PASSWORD_REDACTED]`
- TOTP codes → `[TOTP_REDACTED]`
- API keys → `[API_KEY_REDACTED]`

Handled by `PrivacyScrubber` before writing to disk.

## Diagnostics Panel UI

**Phase 4 Feature** - Real-time log viewer in the app:

- Filter by log level (Error, Warning, Info, Debug)
- Search by category/event/message
- View structured context as JSON
- Export logs
- Session selector

Access: Main menu → Tools → Diagnostics Panel

## Migration from Legacy Logging Systems

**Completed:** September 2026

### DebugLogger Migration (Phase 1)

All `DebugLogger` calls across 8 files have been migrated to `ObservabilityHub.Instance.LogEvent()`:

**Migrated files:**
- `App.xaml.cs` (11 calls)
- `MainWindow.xaml.cs` (11 calls)
- `MainViewModel.cs` (56 calls)
- `GoogleAutoLoginViewModel.cs` (6 calls)
- `ChromeSelectionDialog.xaml.cs` (2 calls)
- `RelayCommand.cs` (1 call)
- `AsyncRelayCommand.cs` (6 calls)
- `DiagnosticHelpers.cs` (17 calls)

**Total:** ~95+ DebugLogger calls replaced with structured ObservabilityHub events.

**Status:** `DebugLogger.cs` is now marked `[Obsolete]` to prevent future usage.

### DebugConsole Migration (Phase 3)

All `DebugConsole.WriteLine()` calls in `GoogleLoginCdpBrowser.cs` (~60+ calls) have been commented out.

**Status:** `DebugConsole.cs` is now marked `[Obsolete]` to prevent future usage.

### Key Improvements

**Before (DebugLogger/DebugConsole):**
- Logs disappeared in Release builds (`[Conditional("DEBUG")]`)
- String-based logging with interpolation
- No structured context
- No privacy scrubbing

**After (ObservabilityHub):**
- Works in both Debug and Release builds
- Structured JSON with typed context objects
- Automatic privacy scrubbing
- Session-based persistent log files
- Better queryability and analytics

## Build Configuration

### All Builds (Debug + Release)

ObservabilityHub is **always active** and writes to log files in all configurations.

```xml
<!-- No conditional compilation needed -->
```

### Release Builds

- Logs still written to `app-debug.log` in user's AppData
- No console window (WinExe output type)
- Users can access logs via Diagnostics Panel or file explorer

## Performance

- Async file I/O (no UI blocking)
- Batched writes
- Structured JSON (easy parsing)
- Privacy scrubbing pre-serialization

## Related Documentation

- `docs/observability/DIAGNOSTICS_PANEL_IMPLEMENTATION.md` - UI implementation
- `docs/observability/FINAL_STATUS.md` - All 4 phases completed
- `AUDIT-2026-09-06.md` - Observability section (189 log events across 24 files)

## Example: Complete Flow Logging

```csharp
// Start
ObservabilityHub.Instance.LogEvent(
    LogLevel.Info,
    "OAuthFlow",
    "Started",
    "Starting OAuth flow",
    new { provider = "Codex", profile_id = profileId });

try 
{
    // Progress
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Debug,
        "OAuthFlow",
        "StateDetected",
        "OAuth page state detected",
        new { 
            is_google = true, 
            has_consent = false,
            current_url = url 
        });
    
    // Success
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Info,
        "OAuthFlow",
        "Success",
        "OAuth flow completed successfully",
        new { elapsed_ms = stopwatch.ElapsedMilliseconds });
}
catch (Exception ex)
{
    // Error
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Error,
        "OAuthFlow",
        "Failed",
        $"OAuth flow failed: {ex.Message}",
        new { 
            error = ex.Message,
            error_type = ex.GetType().Name 
        });
}
```

## Log Analysis

Use standard JSON tools to analyze logs:

```bash
# Count events by category
jq -r '.category' events.jsonl | sort | uniq -c

# Filter errors
jq 'select(.level == "Error")' events.jsonl

# Extract OAuth flows
jq 'select(.category == "OAuthAutoLogin")' events.jsonl
```

---

**Migration Complete:** All production logging now uses ObservabilityHub.
