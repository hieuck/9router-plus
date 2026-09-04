# ObservabilityHub Phase 1 - Implementation Complete

**Date:** 2026-09-04  
**Status:** ✅ Complete and Verified  
**Goal:** Enable AI to diagnose bugs from log files alone

## Overview

ObservabilityHub provides structured logging with automatic privacy protection for AI-assisted debugging. When users report "cannot reproduce" bugs, developers can now share session logs for AI analysis without exposing credentials.

## Implementation

### Core Infrastructure

**ObservabilityHub** (`RouterPlus.Core.Observability`)
- Singleton pattern for centralized logging
- Background flush thread (5-second cycle)
- Concurrent queue for thread-safe event collection
- Automatic privacy scrubbing before logging
- Four severity levels: Debug, Info, Warning, Error

**PrivacyScrubber** (`RouterPlus.Core.Observability`)
- Automatic redaction of sensitive properties: Password, ApiKey, Token, TotpSecret, Authorization, Secret, Credential, AccessToken, RefreshToken
- Regex patterns for connection strings and config files
- Recursive object graph traversal for nested objects and collections
- Safe for sharing logs with external support/AI

**SessionManager** (`RouterPlus.Infrastructure.Observability`)
- Session lifecycle management with unique IDs (format: `2026-09-04_185418_68685aae`)
- Metadata tracking: start time, app version, OS, .NET version
- 7-day retention policy with automatic cleanup
- Session directory structure: `%LOCALAPPDATA%\RouterPlus\Observability\sessions\{session-id}\`

**JsonLinesWriter** (`RouterPlus.Infrastructure.Observability`)
- JSON Lines format (.jsonl): one JSON object per line
- Thread-safe with SemaphoreSlim
- Auto-rotate at 50MB per file
- CamelCase property names for consistency

### Integration Points

**App.xaml.cs** - Application Lifecycle
- Initialize ObservabilityHub on startup (before anything else)
- Log AppStarted event with version/OS/session context
- Log AppExiting event on shutdown
- Dispose hub to flush pending events
- Finalize session metadata

**ProfileHealthChecker** - Health Check Instrumentation
- FilesystemCheckStarted/Completed events with issue counts
- CredentialsCheckStarted/Completed events
- CredentialsFound event with email (safe to log)
- CredentialsNotFound event with diagnostic context

**GoogleAutoLoginViewModel** - Credential Operations
- TryAutoUnlockAsync: comprehensive credential lookup logging
  - VaultInventory: all available ProfileId→Email mappings
  - CredentialsNotFound: diagnosis for Profile ID mismatches with solution recommendation
- UnlockVaultAsync: vault open/create with credential status
- SaveInformationAsync: credential persistence tracking
- AutoLoginAsync: automation lifecycle with result category tracking
- ImportAsync/ExportAsync: vault transfer operations

### Storage Structure

```
%LOCALAPPDATA%\RouterPlus\Observability\
└── sessions\
    └── 2026-09-04_185418_68685aae\
        ├── session.json        # Session metadata
        └── events.jsonl        # Structured log events
```

**session.json** example:
```json
{
  "SessionId": "2026-09-04_185418_68685aae",
  "StartTime": "2026-09-04T18:54:18.4217299Z",
  "EndTime": null,
  "AppVersion": "0.2.0.0",
  "OperatingSystem": "Microsoft Windows NT 10.0.28000.0",
  "DotNetVersion": "8.0.30"
}
```

**events.jsonl** example:
```json
{"timestamp":"2026-09-04T18:50:01.6651797Z","level":1,"category":"Startup","event":"AppStarted","message":"Application starting","context":{"version":"0.2.0.0","os":"Microsoft Windows NT 10.0.28000.0","dotnet_version":"8.0.30","session_id":"2026-09-04_185001_3fae5664"}}
{"timestamp":"2026-09-04T18:50:03.2156421Z","level":1,"category":"HealthCheck","event":"FilesystemCheckStarted","message":"Starting filesystem health check","context":{"profile":"Work Profile","profile_path":"C:\\Users\\user\\AppData\\Local\\Google\\Chrome\\User Data\\Profile 1"}}
{"timestamp":"2026-09-04T18:50:03.2891033Z","level":2,"category":"HealthCheck","event":"CredentialsNotFound","message":"No credentials found for profile - Profile ID mismatch detected","context":{"lookup_profile_id":"C:\\Users\\user\\AppData\\Local\\Google\\Chrome\\User Data||Profile 1","available_profile_ids":["C:\\Users\\old-path\\Chrome\\User Data||Profile 1"],"diagnosis":"Credentials may have been saved with different User Data path or Directory Name","solution":"Delete old credential in Credentials Manager and save again"}}
```

## Test Coverage

**ObservabilityInstrumentationTests.cs** - 2 tests
- ✅ HealthCheck logs filesystem check events
- ✅ HealthCheck logs credentials not found

**PrivacyScubberTests.cs** - 9 tests
- ✅ Scrub removes password property
- ✅ Scrub removes apikey property
- ✅ Scrub removes token property
- ✅ Scrub removes totpsecret property
- ✅ ScrubString removes password patterns
- ✅ ScrubString removes apikey patterns
- ✅ Scrub handles nested objects
- ✅ Scrub handles collections
- ✅ Scrub preserves allowed properties

**Total: 11 tests, all passing**

## Verification

**End-to-End Test Results:**
- ✅ App starts and logs AppStarted event
- ✅ Events flush to events.jsonl after 5+ seconds
- ✅ JSON format correct with camelCase property names
- ✅ Session metadata created in session.json
- ✅ ProfileHealthChecker instrumentation logs all expected events
- ✅ Privacy scrubbing prevents credential leaks

## Usage for Debugging

When a user reports a bug:

1. Ask user to reproduce the issue once
2. Collect session logs from: `%LOCALAPPDATA%\RouterPlus\Observability\sessions\{latest-session}\`
3. Share `events.jsonl` with AI for analysis (safe - no credentials)
4. AI can trace the exact sequence of operations and diagnose the root cause

**Example diagnostic scenario:**
```
User: "Health check is stuck and never completes"

AI analyzes events.jsonl:
- ✅ FilesystemCheckStarted at 18:50:03
- ✅ FilesystemCheckCompleted at 18:50:03
- ✅ CredentialsCheckStarted at 18:50:03
- ❌ CredentialsCheckCompleted never logged

Diagnosis: CredentialsCheck is hanging. Looking at context...
- vault_loaded: false
- The check returns early when vault is null
- But the "Completed" event is after the early return

Root cause: Completed event placed after early return instead of in finally block.
```

## Future Enhancements (Not in Phase 1)

- Instrument MainViewModel commands
- Instrument ChromeProfileManager operations  
- E2E test for silent failure detection scenario
- Log aggregation viewer UI
- Performance metrics tracking
- Crash dump integration

## Commits

1. `1de55cc` - feat(observability): add structured logging with privacy protection
2. `0b1ad4c` - refactor(observability): migrate GoogleAutoLoginViewModel to ObservabilityHub

## Files Created

- `src/RouterPlus.Core/Observability/LogLevel.cs`
- `src/RouterPlus.Core/Observability/LogEvent.cs`
- `src/RouterPlus.Core/Observability/ObservabilityHub.cs`
- `src/RouterPlus.Core/Observability/PrivacyScrubber.cs`
- `src/RouterPlus.Infrastructure/Observability/ObservabilityPaths.cs`
- `src/RouterPlus.Infrastructure/Observability/SessionManager.cs`
- `src/RouterPlus.Infrastructure/Observability/JsonLinesWriter.cs`
- `tests/RouterPlus.Core.Tests/Observability/ObservabilityInstrumentationTests.cs`
- `tests/RouterPlus.Core.Tests/Observability/PrivacyScubberTests.cs`

## Files Modified

- `src/RouterPlus.App/App.xaml.cs` - Initialize ObservabilityHub
- `src/RouterPlus.Core/Chrome/ProfileHealthChecker.cs` - Add structured logging
- `src/RouterPlus.App/ViewModels/GoogleAutoLoginViewModel.cs` - Replace DebugLogger with ObservabilityHub
