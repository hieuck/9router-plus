# Observability System Design Specification

**Date:** 2026-09-04  
**Status:** Draft  
**Owner:** Dev Team

---

## 1. Overview

### 1.1 Purpose
Implement a comprehensive observability system that captures all runtime information about RouterPlus application, enabling:
- AI-assisted debugging by providing complete app state and execution flow
- User bug reports with attached diagnostic data
- Performance analysis and optimization
- Automated error detection and alerting

### 1.2 Goals
- **Complete Visibility:** Capture all significant events, state changes, metrics, and errors
- **Privacy-Safe:** Never log sensitive data (passwords, API keys, tokens)
- **Low Overhead:** Minimal performance impact (<5% CPU, <50MB memory)
- **AI-Friendly:** Structured format that LLMs can query and analyze
- **Local-First:** All data stays on user's machine by default
- **Queryable:** Support searching and filtering diagnostic data

### 1.3 Non-Goals
- Remote telemetry server (Phase 1 - local only)
- Real-time dashboards (file-based analysis only)
- User behavior analytics (focus on technical diagnostics)

---

## 2. Architecture

### 2.1 Three Pillars of Observability

```
┌─────────────────────────────────────────────────────────────┐
│                    RouterPlus Application                    │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   LOGS       │  │   METRICS    │  │   TRACES     │     │
│  │ Structured   │  │ Counters     │  │ Request      │     │
│  │ Events       │  │ Gauges       │  │ Flows        │     │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘     │
│         │                  │                  │              │
│         └──────────────────┴──────────────────┘              │
│                            │                                 │
└────────────────────────────┼─────────────────────────────────┘
                             ▼
                  ┌──────────────────────┐
                  │  ObservabilityHub    │
                  │  (Central Collector) │
                  └──────────┬───────────┘
                             ▼
                  ┌──────────────────────┐
                  │  Local Storage       │
                  │  (Structured Files)  │
                  └──────────────────────┘
```

### 2.2 Data Flow

1. **Instrumentation Points:** Code locations that emit observability data
2. **ObservabilityHub:** Central collector that aggregates and writes data
3. **Storage Layer:** Structured JSON Lines files with rotation
4. **Query Layer:** Tools to search and analyze collected data

---

## 3. Components

### 3.1 Logs (Structured Events)

**Purpose:** Record what happened, when, and why

**Format:** JSON Lines (`.jsonl`)
```json
{"timestamp":"2026-09-04T17:33:01.234Z","level":"Info","category":"Chrome","event":"RefreshProfiles","message":"Found 5 profiles","duration_ms":142,"context":{"user_data_dir":"C:\\...","profile_count":5}}
{"timestamp":"2026-09-04T17:33:01.456Z","level":"Error","category":"Security","event":"VaultUnlockFailed","message":"Invalid passphrase","error_type":"AuthenticationException"}
```

**Schema:**
- `timestamp`: ISO 8601 UTC
- `level`: Debug | Info | Warning | Error
- `category`: Chrome | Security | ViewModel | UI | Commands | Network | etc.
- `event`: PascalCase event name (e.g., ProfileSelected, LoginStarted)
- `message`: Human-readable description
- `duration_ms`: Optional, for timed operations
- `context`: Optional object with event-specific data
- `error_type`: Optional, exception type name
- `stack_trace`: Optional, for errors

**Log Levels:**
- **Debug:** Verbose flow control, variable values (only in debug builds)
- **Info:** Significant state changes, user actions, completion events
- **Warning:** Recoverable errors, degraded functionality
- **Error:** Exceptions, failures requiring user intervention

**Privacy Rules:**
- ✅ Can log: Profile names, email addresses (user-visible identifiers)
- ❌ Never log: Passwords, API keys, tokens, TOTP secrets, cookie values
- ❌ Never log: Full file paths with username (use relative or sanitized paths)

### 3.2 Metrics (Counters & Gauges)

**Purpose:** Measure quantities and performance

**Format:** Time-series snapshots in JSON Lines
```json
{"timestamp":"2026-09-04T17:33:00Z","metrics":{"profiles_loaded":5,"active_logins":2,"quota_kiro_remaining":150000,"memory_mb":245,"cpu_percent":3.2}}
```

**Metric Types:**

**Counters** (cumulative):
- `logins_attempted_total`
- `logins_succeeded_total`
- `logins_failed_total`
- `health_checks_run_total`
- `api_requests_total{provider=kiro|openrouter|github}`

**Gauges** (point-in-time):
- `profiles_loaded_count`
- `active_logins_count`
- `quota_kiro_remaining`
- `quota_openrouter_remaining`
- `memory_mb`
- `cpu_percent`

**Histograms** (distribution):
- `login_duration_ms` (p50, p90, p99)
- `health_check_duration_ms`
- `api_request_duration_ms{provider}`

**Collection Frequency:**
- Counters: Increment on event
- Gauges: Sample every 10 seconds
- Histograms: Record on operation completion

### 3.3 Traces (Request Flows)

**Purpose:** Track multi-step operations from start to finish

**Format:** Trace spans with parent-child relationships
```json
{"trace_id":"a1b2c3d4","span_id":"span001","parent_span_id":null,"operation":"BatchLogin","start_time":"2026-09-04T17:33:00.000Z","end_time":"2026-09-04T17:33:15.234Z","status":"Success","tags":{"profile_count":3}}
{"trace_id":"a1b2c3d4","span_id":"span002","parent_span_id":"span001","operation":"LoginProfile","start_time":"2026-09-04T17:33:00.100Z","end_time":"2026-09-04T17:33:05.200Z","status":"Success","tags":{"profile":"Harness Alpha"}}
```

**Trace Operations:**
- **BatchLogin:** Multi-profile login sequence
- **LoginProfile:** Single profile login with Chrome automation
- **HealthCheck:** Profile health validation
- **ApiRequest:** External API call (Kiro, OpenRouter, GitHub)
- **VaultOperation:** Open, unlock, save credential

**Status Codes:**
- `Success`: Operation completed successfully
- `Error`: Operation failed with exception
- `Cancelled`: User cancelled operation
- `Timeout`: Operation exceeded time limit

**Tags (contextual metadata):**
- `profile`: Profile name
- `provider`: API provider name
- `error_type`: Exception type (if failed)
- `retry_count`: Number of retries attempted

### 3.4 State Snapshots

**Purpose:** Capture complete application state at specific moments

**Format:** Full ViewModel state in JSON
```json
{
  "timestamp": "2026-09-04T17:33:01.234Z",
  "snapshot_type": "Periodic",
  "main_view_model": {
    "status_text": "Đã đọc 5 Chrome profile.",
    "selected_profile": "Harness Alpha",
    "profile_rows": [
      {
        "name": "Harness Alpha",
        "directory_name": "Default",
        "health_status": {"level": "Healthy", "message": "All checks passed", "issue_count": 0},
        "is_checking_health": false,
        "active_logins": ["kiro", "github"]
      }
    ],
    "quota_info": {
      "kiro_remaining": 150000,
      "openrouter_remaining": 25000
    }
  }
}
```

**Snapshot Triggers:**
- **Periodic:** Every 60 seconds (if state changed)
- **OnCommand:** Before/after user command execution
- **OnError:** When exception occurs
- **OnDemand:** Manual snapshot via debug command

**Privacy:** Same rules as logs - no sensitive values

---

## 4. Storage

### 4.1 Directory Structure

```
%LOCALAPPDATA%\RouterPlus\Observability\
├── sessions\
│   ├── 2026-09-04_173301_abc123\
│   │   ├── session.json          # Session metadata
│   │   ├── events.jsonl          # Structured logs
│   │   ├── metrics.jsonl         # Time-series metrics
│   │   ├── traces.jsonl          # Trace spans
│   │   └── snapshots.jsonl       # State snapshots
│   └── 2026-09-04_180512_def456\
│       └── ...
├── crashes\                       # Crash dumps (future)
└── README.txt                     # User-facing documentation
```

### 4.2 Session Lifecycle

**Start:**
1. App starts
2. Create new session directory: `{date}_{time}_{short-id}`
3. Write `session.json` metadata:
   ```json
   {
     "session_id": "2026-09-04_173301_abc123",
     "start_time": "2026-09-04T17:33:01.000Z",
     "app_version": "1.2.0",
     "os": "Windows 11 Pro",
     "dotnet_version": "8.0.8",
     "profiles_count": 5
   }
   ```
4. Open file handles for `.jsonl` files

**Runtime:**
- Append lines to `.jsonl` files (buffered writes)
- Flush every 5 seconds or on shutdown

**End:**
1. Write `end_time` to `session.json`
2. Flush and close all file handles
3. Compress old sessions (optional)

### 4.3 Retention Policy

- Keep last **7 days** of sessions
- Keep last **3 crash dumps** (regardless of age)
- Auto-delete on app startup (async, low priority)
- User can manually clear via Settings

### 4.4 File Size Limits

- `events.jsonl`: Max 50MB per session (auto-rotate if exceeded)
- `metrics.jsonl`: Max 10MB per session
- `traces.jsonl`: Max 20MB per session
- `snapshots.jsonl`: Max 30MB per session
- Total session size: ~100MB typical, 200MB max

---

## 5. Implementation

### 5.1 Core Classes

**ObservabilityHub** (Singleton)
```csharp
public sealed class ObservabilityHub : IDisposable
{
    public static ObservabilityHub Instance { get; }
    
    // Logs
    public void LogEvent(LogLevel level, string category, string eventName, string message, object? context = null);
    public void LogError(string category, string eventName, Exception exception, object? context = null);
    
    // Metrics
    public void IncrementCounter(string name, Dictionary<string, string>? tags = null);
    public void SetGauge(string name, double value, Dictionary<string, string>? tags = null);
    public void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null);
    
    // Traces
    public TraceScope StartTrace(string operation, Dictionary<string, string>? tags = null);
    
    // Snapshots
    public void CaptureSnapshot(string type, object state);
    
    // Query (future)
    public IEnumerable<LogEvent> QueryLogs(LogQuery query);
}
```

**TraceScope** (IDisposable for automatic timing)
```csharp
public sealed class TraceScope : IDisposable
{
    public string TraceId { get; }
    public string SpanId { get; }
    
    public void SetTag(string key, string value);
    public void SetStatus(TraceStatus status);
    public TraceScope CreateChild(string operation);
    
    public void Dispose(); // Auto-records end time
}
```

**Usage Example:**
```csharp
// Logs
ObservabilityHub.Instance.LogEvent(
    LogLevel.Info, 
    "Chrome", 
    "ProfileSelected", 
    "User selected profile", 
    new { profile_name = profile.Name, directory = profile.DirectoryName }
);

// Metrics
ObservabilityHub.Instance.IncrementCounter("logins_attempted_total", new { profile = profile.Name });
ObservabilityHub.Instance.SetGauge("active_logins_count", activeLogins.Count);

// Traces
using var trace = ObservabilityHub.Instance.StartTrace("LoginProfile", new { profile = profile.Name });
try
{
    // ... login logic ...
    trace.SetStatus(TraceStatus.Success);
}
catch (Exception ex)
{
    trace.SetStatus(TraceStatus.Error);
    trace.SetTag("error_type", ex.GetType().Name);
    throw;
}

// Snapshots
ObservabilityHub.Instance.CaptureSnapshot("OnCommand", new
{
    StatusText = _mainViewModel.StatusText,
    SelectedProfile = _mainViewModel.SelectedProfile?.Name,
    ProfileCount = _mainViewModel.ProfileRows.Count
});
```

### 5.2 Instrumentation Points

**High-Value Locations:**

1. **User Commands:**
   - AsyncRelayCommand.ExecuteAsync enter/exit
   - Command parameters (sanitized)
   - Success/failure outcome

2. **Chrome Operations:**
   - RefreshProfiles start/end with profile count
   - Profile launch/close events
   - Health check execution

3. **Login Flow:**
   - Login attempt start (profile + provider)
   - Chrome automation steps (navigate, fill, click, verify)
   - Login success/failure with reason

4. **API Requests:**
   - Request start (provider, endpoint)
   - Response status (success, error, timeout)
   - Duration, retry count

5. **State Changes:**
   - SelectedProfile changed
   - HealthStatus updated
   - QuotaInfo refreshed

6. **Errors:**
   - All caught exceptions
   - Validation failures
   - Timeout events

### 5.3 Privacy Implementation

**PII Scrubber:**
```csharp
public static class PrivacyScrubber
{
    private static readonly Regex PasswordPattern = new(@"(password|pwd|pass)\s*[:=]\s*""[^""]+""", RegexOptions.IgnoreCase);
    private static readonly Regex ApiKeyPattern = new(@"(api[_-]?key|token|secret)\s*[:=]\s*""[^""]+""", RegexOptions.IgnoreCase);
    private static readonly string[] SensitivePropertyNames = { "Password", "ApiKey", "Token", "TotpSecret", "Cookie" };
    
    public static object Scrub(object? obj)
    {
        // Recursively sanitize object graph
        // Replace sensitive values with "[REDACTED]"
    }
    
    public static string ScrubString(string text)
    {
        // Regex-based redaction of sensitive patterns
    }
}
```

**Usage:** Auto-applied in ObservabilityHub before writing

---

## 6. Query Interface

### 6.1 CLI Tool (Future Phase)

```powershell
# Search logs
routerplus-observe query logs --category Chrome --event ProfileSelected --since "1 hour ago"

# Show metrics
routerplus-observe metrics --name login_duration_ms --percentiles 50,90,99

# Trace visualization
routerplus-observe trace {trace_id} --format tree

# Export session
routerplus-observe export --session {session_id} --output report.zip
```

### 6.2 AI Integration

**Attach to bug report:**
```
User: "Login failed on Harness Alpha"

AI: Let me check the observability data...
[Reads latest session events.jsonl]
[Filters to profile="Harness Alpha" and category="Security"]
[Finds error: VaultUnlockFailed with error_type="AuthenticationException"]

AI: I found the issue - the vault unlock failed with authentication error at 17:33:45. 
The trace shows the login flow got to the "UnlockVault" step but the passphrase was invalid.
```

---

## 7. Performance Considerations

### 7.1 Overhead Targets
- **CPU:** <5% average, <10% peak during intensive operations
- **Memory:** <50MB for observability buffers
- **Disk I/O:** Batched writes (5-second buffer), async flush
- **Startup Time:** <100ms additional overhead

### 7.2 Optimization Strategies
- **Buffered Writes:** Collect in memory, batch write every 5 seconds
- **Sampling:** High-frequency metrics sampled (not every call)
- **Lazy Serialization:** Only serialize context objects when actually written
- **Async Background Thread:** Separate thread for I/O operations
- **Conditional Compilation:** Debug-only verbose logs (`#if DEBUG`)

### 7.3 Disable Switch
- Environment variable: `ROUTERPLUS_DISABLE_OBSERVABILITY=1`
- Settings UI: "Enable diagnostic data collection" checkbox
- Default: **Enabled** (opt-out, not opt-in)

---

## 8. User Experience

### 8.1 Settings UI

**New section in Settings:**
```
┌─────────────────────────────────────────────┐
│ ⚙️ Diagnostic Data                          │
├─────────────────────────────────────────────┤
│                                             │
│ ☑ Enable diagnostic data collection        │
│   Help improve RouterPlus by collecting    │
│   anonymous usage and error data.          │
│                                             │
│ Data location:                             │
│ C:\Users\...\AppData\Local\RouterPlus\... │
│ [Open Folder] [Clear Data]                │
│                                             │
│ Storage used: 142 MB                       │
│ Sessions: 15 (last 7 days)                │
│ [View Latest Session] [Export Report]     │
└─────────────────────────────────────────────┘
```

### 8.2 Bug Report Flow

**When user reports bug:**
1. User describes issue
2. AI/Support asks: "Can you share your diagnostic data?"
3. User clicks "Export Report" → creates `routerplus-diagnostics-{date}.zip`
4. User attaches ZIP to GitHub issue / email
5. Dev/AI analyzes data to diagnose issue

**ZIP Contents:**
- Latest session files (events, metrics, traces, snapshots)
- App version, OS info
- README explaining privacy (no passwords/keys included)

---

## 9. Security & Privacy

### 9.1 Data Classification

| Data Type | Contains PII? | Logged? |
|-----------|---------------|---------|
| Profile names | ✅ Yes (user-chosen) | ✅ Yes |
| Email addresses | ✅ Yes | ✅ Yes |
| Passwords | ❌ **NEVER** | ❌ **NEVER** |
| API keys/tokens | ❌ **NEVER** | ❌ **NEVER** |
| TOTP secrets | ❌ **NEVER** | ❌ **NEVER** |
| File paths | ⚠️ May contain username | ⚠️ Sanitized |
| Exception messages | ⚠️ May leak sensitive data | ⚠️ Scrubbed |

### 9.2 Privacy Policy Update

Add section to documentation:
> **Diagnostic Data Collection**
> 
> RouterPlus collects diagnostic data locally on your device to help debug issues. This data includes:
> - Actions you perform (e.g., login attempts, profile switches)
> - Performance metrics (memory, CPU usage)
> - Error messages and stack traces
> - Application state (profile names, UI status)
> 
> **We do NOT collect:**
> - Passwords or passphrases
> - API keys or authentication tokens
> - 2FA secrets
> - Cookie values or session data
> 
> All data stays on your computer unless you explicitly export and share it.

---

## 10. Testing Strategy

### 10.1 Unit Tests
- `ObservabilityHubTests`: Verify log/metric/trace recording
- `PrivacyScubberTests`: Ensure sensitive data redaction
- `SessionStorageTests`: File creation, rotation, retention

### 10.2 Integration Tests
- Instrument test app, verify files written correctly
- Simulate crash, verify state snapshot captured
- Verify performance overhead <5% CPU

### 10.3 Privacy Tests
- **Audit test:** Log a credential object, verify password is `[REDACTED]`
- **Regex test:** Feed sensitive strings, verify scrubbing works
- **Whole-session test:** Run full login flow, grep output for "password|api_key|secret" (should find 0 matches)

---

## 11. Future Enhancements (Post-MVP)

### Phase 2: Query & Analysis
- CLI tool for querying local data
- Web UI for session visualization
- Crash dump analysis (Windows Error Reporting integration)

### Phase 3: Remote Telemetry (Opt-in)
- Backend API to receive anonymized telemetry
- Aggregate statistics across all users
- Proactive issue detection ("10% of users hitting this error")

### Phase 4: Distributed Tracing
- Trace Chrome DevTools Protocol requests
- Visualize request waterfalls
- Correlate app traces with browser network logs

---

## 12. Success Criteria

**MVP (Phase 1) is successful if:**
1. ✅ AI can diagnose bugs from observability data alone (no user back-and-forth)
2. ✅ Performance overhead <5% CPU, <50MB memory
3. ✅ Zero password/API key leaks in 100-session audit
4. ✅ User can export diagnostic report in <30 seconds
5. ✅ Observability data helps resolve at least 80% of bug reports faster

**Metrics to Track:**
- Time-to-diagnosis for bug reports (before/after observability)
- Percentage of bugs resolved without repro steps from user
- Performance overhead (CPU, memory, disk I/O)
- False positive rate for PII scrubber (sensitive data leaked)
- User opt-out rate (should be <10% if UX is good)

---

## 13. Open Questions

1. **Compression:** Should we gzip `.jsonl` files to save space? (Tradeoff: CPU vs disk)
2. **Structured vs Plain Text:** JSON Lines vs human-readable format? (JSON Lines for AI, but harder for users to read)
3. **Sampling Rate:** What percentage of high-frequency events to log? (100% for MVP, tune based on performance)
4. **Cloud Sync:** Any scenario where auto-uploading diagnostics is acceptable? (No - always require explicit user action)
5. **Performance Profiling:** Should we integrate .NET ETW events? (Future phase - complex)

---

## Appendix A: Data Schema Reference

### Log Event Schema
```typescript
interface LogEvent {
  timestamp: string;        // ISO 8601 UTC
  level: "Debug" | "Info" | "Warning" | "Error";
  category: string;         // Chrome, Security, ViewModel, etc.
  event: string;           // PascalCase event name
  message: string;         // Human-readable
  duration_ms?: number;    // For timed operations
  context?: object;        // Event-specific data
  error_type?: string;     // Exception type name
  stack_trace?: string;    // Full stack trace
}
```

### Metric Snapshot Schema
```typescript
interface MetricSnapshot {
  timestamp: string;       // ISO 8601 UTC, rounded to 10-second interval
  metrics: {
    [key: string]: number; // Metric name -> value
  };
}
```

### Trace Span Schema
```typescript
interface TraceSpan {
  trace_id: string;        // UUID for entire operation
  span_id: string;         // UUID for this span
  parent_span_id: string | null; // Parent span (null for root)
  operation: string;       // Operation name
  start_time: string;      // ISO 8601 UTC
  end_time: string;        // ISO 8601 UTC
  duration_ms: number;     // Computed from start/end
  status: "Success" | "Error" | "Cancelled" | "Timeout";
  tags: {
    [key: string]: string; // Contextual metadata
  };
}
```

### State Snapshot Schema
```typescript
interface StateSnapshot {
  timestamp: string;
  snapshot_type: "Periodic" | "OnCommand" | "OnError" | "OnDemand";
  main_view_model: {
    status_text: string;
    selected_profile: string | null;
    profile_rows: Array<{
      name: string;
      directory_name: string;
      health_status: {
        level: "Healthy" | "Warning" | "Error" | "Unknown";
        message: string;
        issue_count: number;
      } | null;
      is_checking_health: boolean;
      active_logins: string[]; // provider names
    }>;
    quota_info: {
      kiro_remaining: number;
      openrouter_remaining: number;
    };
  };
}
```

---

**End of Specification**
