# Observability System - Implementation Plan

**Date:** 2026-09-04  
**Status:** Planning  
**Design Spec:** [2026-09-04-observability-system-design.md](../specs/2026-09-04-observability-system-design.md)

---

## Phased Implementation

### Phase 1: Foundation (MVP)
**Goal:** Basic structured logging to local files, AI can read to debug issues

**Deliverables:**
1. ObservabilityHub singleton with log writing
2. JSON Lines storage to `%LOCALAPPDATA%\RouterPlus\Observability\`
3. Session lifecycle management
4. Privacy scrubber for sensitive data
5. Instrumentation at key points

**Priority:** High (enables AI-assisted debugging)

---

### Phase 2: Metrics & Traces
**Goal:** Add performance metrics and operation tracing

**Deliverables:**
1. Counter/Gauge/Histogram support
2. TraceScope for multi-step operations
3. Automatic timing and duration calculation
4. Metrics collection (10-second intervals)

**Priority:** Medium (nice-to-have for performance analysis)

---

### Phase 3: State Snapshots
**Goal:** Capture complete app state periodically

**Deliverables:**
1. ViewModel state serialization
2. Periodic snapshot (60s if changed)
3. On-command and on-error snapshots
4. Snapshot compression for large states

**Priority:** Medium (useful but not critical)

---

### Phase 4: User Features
**Goal:** User-facing tools for managing diagnostic data

**Deliverables:**
1. Settings UI for enable/disable
2. Data viewer (show sessions, size)
3. Export diagnostic report (ZIP)
4. Clear old data manually

**Priority:** Low (can manually access files initially)

---

## DebugLogger Integration Strategy

### Current State

RouterPlus has existing `DebugLogger` (src/RouterPlus.App/Diagnostics/DebugLogger.cs):
- Compiled out in Release builds (`[Conditional("DEBUG")]`)
- Writes plain text to `app-debug.log`
- Used throughout codebase for development debugging

### Integration Approach: Parallel Operation

**DO NOT modify DebugLogger in Phase 1**

Run both systems in parallel:

```
DEBUG builds:
  ✅ DebugLogger → app-debug.log (developer console output)
  ✅ ObservabilityHub → sessions/*/events.jsonl (AI analysis)

RELEASE builds:
  ✅ ObservabilityHub → sessions/*/events.jsonl (AI analysis)
```

**Rationale:**
- ✅ Zero risk to existing functionality
- ✅ Developers keep familiar console output
- ✅ AI gets structured data immediately
- ✅ Easy rollback if issues found
- ⚠️ Small duplication in DEBUG builds (acceptable)

**New code guidelines:**
- Use `ObservabilityHub` for new instrumentation
- Leave existing `DebugLogger` calls unchanged
- Migrate gradually in future phases

### Future Migration Path (Post Phase 1)

**Phase 2:** Gradually replace DebugLogger calls with ObservabilityHub

**Phase 3:** Make DebugLogger a thin wrapper around ObservabilityHub

This is explicitly a **non-goal for Phase 1** to minimize risk and speed up delivery.

---

## Phase 1 Detailed Tasks

### Task 1: Core Infrastructure

**1.1 Create ObservabilityHub**
```
src/RouterPlus.Core/Observability/
├── ObservabilityHub.cs          # Main singleton
├── LogLevel.cs                  # Enum: Debug, Info, Warning, Error
├── LogEvent.cs                  # Log event model
└── IObservabilityWriter.cs      # Interface for writers
```

**ObservabilityHub.cs:**
- Singleton pattern with lazy initialization
- `LogEvent(level, category, eventName, message, context?)` method
- `LogError(category, eventName, exception, context?)` method
- In-memory buffer (queue) for pending events
- Background thread for file writes
- Graceful shutdown on app exit

**1.2 Create Storage Writer**
```
src/RouterPlus.Infrastructure/Observability/
├── JsonLinesWriter.cs           # Writes to .jsonl files
├── SessionManager.cs            # Manages session directories
└── ObservabilityPaths.cs        # Path helpers
```

**JsonLinesWriter.cs:**
- Append JSON line to file
- Buffered writes (flush every 5 seconds)
- Thread-safe file access
- Auto-rotate if file exceeds 50MB

**SessionManager.cs:**
- Create session directory on app start
- Generate session ID: `{date}_{time}_{short-guid}`
- Write `session.json` metadata
- Update `end_time` on app close
- Clean up old sessions (>7 days)

### Task 2: Privacy Protection

**2.1 Create PrivacyScrubber**
```
src/RouterPlus.Core/Observability/
└── PrivacyScrubber.cs           # Sanitize sensitive data
```

**Rules:**
- Detect properties: Password, ApiKey, Token, TotpSecret, Cookie
- Replace values with `[REDACTED]`
- Regex patterns for sensitive strings
- Recursively scan objects
- Whitelist: ProfileName, Email (user-visible)

**2.2 Unit Tests**
```
tests/RouterPlus.Core.Tests/Observability/
└── PrivacyScruberTests.cs
```

Test cases:
- Scrub password property
- Scrub API key in string
- Preserve allowed properties
- Handle nested objects
- Handle collections

### Task 3: Instrumentation

**3.1 High-Value Points**

**MainViewModel.cs:**
```csharp
public async Task InitializeAsync()
{
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Info,
        "Startup",
        "InitializeAsync",
        "Initializing MainViewModel"
    );
    
    try
    {
        // ... existing code ...
        
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Startup",
            "InitializeComplete",
            $"Initialization completed: {ProfileRows.Count} profiles",
            new { profile_count = ProfileRows.Count }
        );
    }
    catch (Exception ex)
    {
        ObservabilityHub.Instance.LogError(
            "Startup",
            "InitializeFailed",
            ex
        );
        throw;
    }
}
```

**AsyncRelayCommand wrapper:**
```csharp
// In MainViewModel command handlers
private async Task CheckProfileHealthAsync(ProfileRowViewModel? row)
{
    var context = new { profile = row?.Name };
    ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Commands", "CheckHealthStarted", "Health check started", context);
    
    try
    {
        // ... existing logic ...
        ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Commands", "CheckHealthCompleted", "Health check completed", context);
    }
    catch (Exception ex)
    {
        ObservabilityHub.Instance.LogError("Commands", "CheckHealthFailed", ex, context);
        throw;
    }
}
```

**ProfileHealthChecker.cs:**
```csharp
public async Task<ProfileHealthStatus> GetHealthStatusAsync(ChromeProfile profile, bool forceRefresh)
{
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Info,
        "HealthCheck",
        "HealthCheckStarted",
        $"Starting health check for profile",
        new { profile = profile.Name, force_refresh = forceRefresh }
    );
    
    // ... existing logic ...
    
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Info,
        "HealthCheck",
        "HealthCheckCompleted",
        $"Health check completed",
        new { 
            profile = profile.Name, 
            level = status.Level.ToString(),
            issue_count = status.Issues.Count
        }
    );
    
    return status;
}
```

**ChromeProfileManager.cs:**
```csharp
public async Task<List<ChromeProfile>> RefreshProfilesAsync()
{
    ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Chrome", "RefreshProfilesStarted", "Refreshing Chrome profiles");
    
    var stopwatch = Stopwatch.StartNew();
    var profiles = // ... existing logic ...
    stopwatch.Stop();
    
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Info,
        "Chrome",
        "RefreshProfilesCompleted",
        $"Found {profiles.Count} profiles",
        new { 
            profile_count = profiles.Count,
            duration_ms = stopwatch.ElapsedMilliseconds
        }
    );
    
    return profiles;
}
```

**GoogleAutoLoginViewModel.cs:**
```csharp
public async Task LoginAsync()
{
    ObservabilityHub.Instance.LogEvent(
        LogLevel.Info,
        "Security",
        "GoogleLoginStarted",
        "Google auto-login started",
        new { profile = _profile.Name }
    );
    
    try
    {
        // ... existing logic ...
        
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Security",
            "GoogleLoginSuccess",
            "Google login succeeded",
            new { profile = _profile.Name, duration_ms = stopwatch.ElapsedMilliseconds }
        );
    }
    catch (Exception ex)
    {
        ObservabilityHub.Instance.LogError(
            "Security",
            "GoogleLoginFailed",
            ex,
            new { profile = _profile.Name }
        );
        throw;
    }
}
```

### Task 4: Integration & Testing

**4.1 Initialize in App Startup**

**App.xaml.cs:**
```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    
    // Initialize observability FIRST (before anything else)
    var observability = ObservabilityHub.Instance;
    observability.LogEvent(LogLevel.Info, "Startup", "AppStarted", "Application starting", new
    {
        version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(),
        os = Environment.OSVersion.ToString(),
        dotnet_version = Environment.Version.ToString()
    });
    
    // ... rest of startup ...
}

protected override void OnExit(ExitEventArgs e)
{
    ObservabilityHub.Instance.LogEvent(LogLevel.Info, "Shutdown", "AppExiting", "Application exiting");
    ObservabilityHub.Instance.Dispose(); // Flush and close files
    base.OnExit(e);
}
```

**4.2 Unit Tests**
```
tests/RouterPlus.Core.Tests/Observability/
├── ObservabilityHubTests.cs
├── JsonLinesWriterTests.cs
└── SessionManagerTests.cs
```

Test cases:
- Log event writes to file
- Buffer flushes after 5 seconds
- Session directory created correctly
- Old sessions cleaned up
- Thread-safe concurrent writes
- Graceful shutdown

**4.3 Manual Testing**

1. Run app
2. Perform actions: refresh profiles, check health, login
3. Check `%LOCALAPPDATA%\RouterPlus\Observability\sessions\`
4. Verify `events.jsonl` contains expected logs
5. Verify no passwords/keys in logs (grep test)
6. Check file sizes reasonable

**4.4 AI Integration Test**

1. Simulate a bug (e.g., force health check failure)
2. Export session files
3. Ask AI to read `events.jsonl` and diagnose
4. Verify AI can identify root cause

---

## Acceptance Criteria (Phase 1)

**Must Have:**
- ✅ ObservabilityHub logs events to JSON Lines files
- ✅ Session directory created on app start
- ✅ Privacy scrubber removes passwords/keys/tokens
- ✅ Instrumentation at: startup, commands, health checks, logins, Chrome ops
- ✅ Performance overhead <5% CPU
- ✅ Old sessions auto-deleted after 7 days
- ✅ AI can read logs and diagnose issues

**Should Have:**
- ✅ Buffered writes (5-second flush)
- ✅ Thread-safe file access
- ✅ Graceful shutdown flushes all data
- ✅ File rotation at 50MB

**Nice to Have:**
- Compression of old sessions
- Pretty-print JSON for readability
- Log viewer UI

---

## Non-Functional Requirements

**Performance:**
- <5% CPU overhead average
- <50MB memory for buffers
- <100ms startup overhead
- Async I/O (non-blocking)

**Reliability:**
- Never crash app due to logging failure
- Graceful degradation if disk full
- Thread-safe (concurrent access)

**Privacy:**
- Zero leaks of passwords/keys/tokens in 100-session audit
- User can disable via environment variable
- All data stays local (no network calls)

**Maintainability:**
- Clear separation: Core (models) vs Infrastructure (I/O)
- Extensible writer interface
- Well-documented public APIs

---

## Implementation Order

**Week 1:**
1. Create ObservabilityHub skeleton
2. Create JsonLinesWriter
3. Create SessionManager
4. Wire up in App.xaml.cs
5. Basic "hello world" log test

**Week 2:**
1. Add PrivacyScrubber
2. Unit tests for privacy
3. Instrumentation: Startup, Commands
4. Instrumentation: Chrome operations
5. Manual testing

**Week 3:**
1. Instrumentation: Health checks, Logins
2. Performance testing (measure overhead)
3. Audit test (grep for sensitive data)
4. AI integration test
5. Polish and documentation

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Performance overhead too high | High | Profile early, optimize buffering, add sampling |
| Disk space fills up | Medium | Retention policy (7 days), file size limits, compression |
| Privacy leak (password in logs) | **Critical** | Mandatory scrubber, audit tests, code review |
| Thread contention on file writes | Medium | Use async I/O, separate background thread |
| Observability code crashes app | High | Try-catch all logging, graceful degradation |

---

## Future Phases (Post-MVP)

**Phase 2: Metrics & Traces**
- Counter/Gauge/Histogram support
- TraceScope for operation flows
- Metrics dashboard (text-based)

**Phase 3: State Snapshots**
- ViewModel state serialization
- Periodic snapshots (60s)
- On-error snapshots

**Phase 4: User Tools**
- Settings UI (enable/disable, clear data)
- Export report (ZIP)
- Log viewer (read-only UI)

**Phase 5: Query CLI**
- `routerplus-observe query` command
- Filter by category, event, time range
- Export to CSV/JSON

---

## Success Metrics

**Phase 1 Success:**
- ✅ 80% of bug reports diagnosed from logs alone (no user back-and-forth)
- ✅ Performance overhead <5% CPU measured
- ✅ Zero privacy leaks in audit
- ✅ AI can identify root cause in <2 minutes from reading logs

**Long-term:**
- Time-to-diagnosis reduced by 50%
- User satisfaction with bug report process increases
- Fewer "cannot reproduce" bug reports

---

## References

- [Design Spec](../specs/2026-09-04-observability-system-design.md)
- OpenTelemetry: https://opentelemetry.io/
- Serilog (inspiration): https://serilog.net/
- Structured Logging Best Practices: https://www.loggly.com/blog/why-json-is-the-best-application-log-format-and-how-to-switch/

---

**End of Plan**
