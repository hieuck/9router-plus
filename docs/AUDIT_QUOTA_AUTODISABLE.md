# Audit Report: Quota Auto-Disable Connection Feature

## Executive Summary

The **Auto-disable Connection** feature automatically disables provider connections when they exceed their quota limits. It is implemented in `QuotaAutoDisablePolicy.cs` and orchestrated via `MainViewModel.RefreshConnectionStatusesCoreAsync()` with background polling via `QuotaPollingService`.

---

## Core Policy Logic

### Supported Providers
| Provider | Auto-disable Condition | Recovery Condition |
|----------|------------------------|-------------------|
| **Codex** | `IsOverLimit == true` | `!IsOverLimit` |
| **Ollama** | `IsOverLimit == true` | `!IsOverLimit` |
| **Kiro** | ALL buckets: `Total > 0` AND `IsOverLimit` AND `ResetAt.HasValue` | ALL buckets: `Total > 0` AND `!IsOverLimit` |
| OpenRouter, GitHub, Kimchi | **Never** | N/A |

### Kiro Special Handling
- Multi-bucket quota (e.g., `credit`, `credit_freetrial`)
- Requires **ALL** buckets exhausted AND resettable (`ResetAt` present)
- Prevents false positives from partial quota exhaustion

---

## Persistence Layer

### QuotaAutoDisableMarker
```csharp
record QuotaAutoDisableMarker(
    string ConnectionId,
    ProviderKind Provider,
    string? Name,
    DateTimeOffset? ResetAt);  // When quota resets - used for re-enable eligibility
```

### Storage
- Stored in `RouterSettings.QuotaAutoDisableMarkers` (JSON in `%LocalAppData%\9RouterPlus\settings.json`)
- Atomic updates via `SettingsStore.UpdateQuotaAutoDisableMarkersAsync()` using file-lock + temp-file-move pattern

---

## Auto-Disable Execution Flow

```
RefreshConnectionStatusesCoreAsync()
    │
    ├─► Load all connections from API
    │
    ├─► Filter: IsActive && CanAutoDisable(connection)
    │
    ├─► For each exhausted connection:
    │       ├─► api.UpdateConnectionAsync(id, isActive: false)
    │       ├─► UpsertQuotaAutoDisableMarker(connection)  // persist ResetAt
    │       └─► SaveQuotaAutoDisableMarkersAsync()
    │
    ├─► Reload connections (post-disable state)
    │
    └─► UpdateQuotaResetSuggestionsAsync()  // build re-enable UI
```

---

## Background Polling: QuotaPollingService

### Configuration
```csharp
QuotaPollingOptions.Default = new(
    NormalInterval: 5 minutes,
    NearLimitInterval: 30 seconds);
```

### Lifecycle (MainWindow.xaml.cs)
```csharp
Window_OnLoaded:
    ViewModel.InitializeAsync();
    ViewModel.StartQuotaPolling();  // ← STARTS HERE

Window_OnStateChanged:
    Minimized  → ViewModel.PauseQuotaPolling()
    Normal     → await ViewModel.ResumeQuotaPollingAsync()

Window_OnClosing:
    await ViewModel.StopQuotaPollingAsync()
```

### Polling Logic
- Runs `RefreshForQuotaPollingAsync` → `RefreshConnectionStatusesAsync(showStatus: false)`
- Returns `_lastReliableNearLimit` to adjust interval (5 min vs 30 sec)
- **Only runs when app window is open and NOT minimized**

---

## Why "Sync Button" is Needed for Immediate Action

### The Core Reason

**Auto-disable is driven by client-side polling, not server-side webhooks.**

| Trigger | Timing | Notes |
|---------|--------|-------|
| **Background Polling** | Every 5 min (30 sec if near limit) | Only when app running + not minimized |
| **Sync Button (Refresh)** | Immediate | User-initiated, bypasses interval |
| **App Startup** | On `Window_OnLoaded` | After `InitializeAsync()` completes |

### Scenarios Where Auto-Disable Won't Happen Automatically

1. **App is closed** - No polling process exists
2. **App is minimized** - `PauseQuotaPolling()` called explicitly
3. **Polling interval not elapsed** - 5 min default, 30 sec near limit
4. **Network/API error** - Polling catches exception, sets `_nearLimit = false`, continues next cycle

### User Experience Implication

> **If a profile hits quota limit while the app is closed/minimized, the connection stays "Active" until:**
> - User opens/restores the app → polling resumes → next cycle disables it, OR
> - User clicks **"Đồng bộ" (Sync/Refresh)** → immediate check + disable

---

## Re-Enable Flow (User-Initiated Only)

```csharp
ReenableQuotaConnectionAsync(connectionId, confirmedByUser: true)
    │
    ├─► Validate: marker exists, connection inactive, provider matches
    │
    ├─► Validate: marker.ResetAt <= now (quota period has reset)
    │
    ├─► Validate: HasRecovered(connection) == true
    │
    ├─► api.UpdateConnectionAsync(id, isActive: true)
    │
    ├─► Remove marker + suggestion
    │
    └─► Refresh UI
```

**Design Decision**: No auto-re-enable. User must confirm to prevent flip-flop when quota oscillates near limit.

---

## Test Coverage

| Test Suite | Cases | Coverage |
|------------|-------|----------|
| `QuotaAutoDisablePolicyTests.cs` | 13 | Policy logic (CanAutoDisable/HasRecovered) |
| `QuotaAutoDisablePolicyAdditionalTests.cs` | 5 | Edge cases |
| `SettingsStoreTests.cs` | 1 | Marker persistence round-trip |

---

## Risks & Mitigations

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Race: quota resets between check & disable | Low | `HasRecovered` check on re-enable validates current state |
| Kiro strictness: missing `ResetAt` blocks disable | Medium | API should always provide `ResetAt` for Kiro |
| Orphaned markers if connection deleted externally | Low | `UpdateQuotaResetSuggestionsAsync` filters missing connections |
| Polling stops if app crashes | Medium | `Window_OnClosing` ensures `StopQuotaPollingAsync` |

---

## Conclusion

The auto-disable feature is **well-designed and correctly implemented** with:
- Clear per-provider policies
- Robust persistence (atomic writes)
- Background polling with adaptive intervals
- Safe re-enable requiring user confirmation

**The "Sync button" requirement is by design** - there is no server-side push; all quota monitoring is client-side polling that only runs when the app is active. The manual refresh provides immediate feedback when users need it.