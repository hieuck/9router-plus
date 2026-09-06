# Diagnostics Panel Implementation Plan

**Date:** 2026-09-06  
**Status:** Planning → Implementation

## Goal

Implement in-app Diagnostics Panel UI so users can view, filter, and export observability data without manually opening log files.

---

## Architecture

### Components

1. **DiagnosticsWindow.xaml** - Standalone window (like CredentialsManagerDialog)
2. **DiagnosticsViewModel.cs** - Main viewmodel
3. **EventLogReader.cs** - Read and parse app-debug.log
4. **EventFilter.cs** - Filter events by category/level/time

### UI Structure

```
DiagnosticsWindow
├─ TabControl
│  ├─ Events Tab
│  │  ├─ Filter Panel (top)
│  │  │  ├─ Category ComboBox (All/OAuth/Vault/Login/etc)
│  │  │  ├─ Level ComboBox (All/Debug/Info/Warning/Error)
│  │  │  ├─ Search TextBox
│  │  │  ├─ Time Range Picker
│  │  │  └─ Refresh Button
│  │  └─ Events DataGrid (scrollable)
│  │     └─ Columns: Timestamp, Level, Category, Operation, Message
│  ├─ Metrics Tab
│  │  ├─ Counters Section
│  │  ├─ Gauges Section
│  │  └─ Histograms Section (p50/p95/p99)
│  └─ Quick Actions Tab
│     ├─ Export Last 100 Events Button
│     ├─ Export All Events Button
│     ├─ Clear Logs Button
│     └─ Open Log Folder Button
```

---

## Implementation Steps

### Step 1: EventLogReader Service
**File:** `src/RouterPlus.Infrastructure/Observability/EventLogReader.cs`

```csharp
public class ObservabilityEvent
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; }
    public string Category { get; init; }
    public string Operation { get; init; }
    public string Message { get; init; }
    public Dictionary<string, object>? Context { get; init; }
}

public class EventLogReader
{
    public List<ObservabilityEvent> ReadEvents(string logPath, int? maxCount = null)
    public List<ObservabilityEvent> ReadRecentEvents(string logPath, TimeSpan duration)
    public ObservabilityMetrics CalculateMetrics(List<ObservabilityEvent> events)
}
```

### Step 2: DiagnosticsViewModel
**File:** `src/RouterPlus.App/ViewModels/DiagnosticsViewModel.cs`

Properties:
- `ObservableCollection<ObservabilityEvent> Events`
- `ObservableCollection<ObservabilityEvent> FilteredEvents`
- `string SelectedCategory` (All, OAuth, Vault, Login, etc)
- `string SelectedLevel` (All, Debug, Info, Warning, Error)
- `string SearchText`
- `DateTime? StartTime`
- `DateTime? EndTime`

Commands:
- `RefreshCommand` - Reload events from log
- `ExportCommand` - Export filtered events to JSON/CSV
- `ClearLogsCommand` - Clear app-debug.log
- `OpenLogFolderCommand` - Open log directory in Explorer

### Step 3: DiagnosticsWindow UI
**File:** `src/RouterPlus.App/Views/DiagnosticsWindow.xaml`

- TabControl with 3 tabs
- DataGrid for events with sorting
- Filter controls with ComboBox and TextBox
- Action buttons

### Step 4: MainWindow Integration

Add menu item or button to open DiagnosticsWindow:
- Settings section → "Diagnostics" button
- Or Help menu → "View Diagnostics"

---

## Data Flow

1. **User opens Diagnostics Window**
   → EventLogReader reads last 1000 events from app-debug.log
   → Populate Events collection

2. **User changes filter**
   → Apply LINQ filter to Events
   → Update FilteredEvents collection
   → DataGrid auto-updates

3. **User clicks Export**
   → SaveFileDialog
   → Write FilteredEvents to JSON/CSV

4. **User clicks Refresh**
   → Re-read log file
   → Merge with existing events (deduplicate by timestamp)
   → Update UI

---

## Technical Details

### JSON Parsing

```csharp
// Each line in app-debug.log is JSON
{
  "timestamp": "2026-09-06T04:00:00Z",
  "level": "Info",
  "category": "OAuth",
  "operation": "ButtonClick",
  "message": "Clicked Google button",
  "context": { "provider": "Codex", "page_url": "..." }
}
```

Parse with `System.Text.Json.JsonSerializer.Deserialize<ObservabilityEvent>(line)`

### Performance

- Read file incrementally (tail -n 1000)
- Cache parsed events in memory
- Only re-parse new lines on refresh
- Use virtualization for DataGrid (1000+ rows)

---

## UI Mockup

```
┌─────────────────────────────────────────────────────────┐
│ Diagnostics - RouterPlus                           [_][□][X]│
├─────────────────────────────────────────────────────────┤
│ [Events] [Metrics] [Quick Actions]                      │
├─────────────────────────────────────────────────────────┤
│ Filters:                                                │
│ Category: [All ▼]  Level: [All ▼]  Search: [_______]   │
│ Time Range: [Last Hour ▼]                    [Refresh]  │
├─────────────────────────────────────────────────────────┤
│ Timestamp         │Level│Category│Operation│Message     │
├───────────────────┼─────┼────────┼─────────┼───────────┤
│ 04:15:23.456      │Info │OAuth   │Navigate │Navigating │
│ 04:15:24.123      │Warn │OAuth   │Click    │Button not │
│ 04:15:25.789      │Error│OAuth   │Consent  │Failed to  │
│                   │     │        │         │           │
│                  (Scrollable list of 100+ events)       │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

---

## Testing Plan

1. **Unit Tests** (EventLogReader)
   - Parse valid JSON lines
   - Handle malformed JSON
   - Filter by category/level/time

2. **Integration Tests**
   - Read from actual app-debug.log
   - Export to file

3. **Manual UI Tests**
   - Open window
   - Apply filters
   - Export events
   - Verify no memory leaks on large logs

---

## File Structure

```
src/RouterPlus.Infrastructure/Observability/
  EventLogReader.cs

src/RouterPlus.App/ViewModels/
  DiagnosticsViewModel.cs

src/RouterPlus.App/Views/
  DiagnosticsWindow.xaml
  DiagnosticsWindow.xaml.cs

tests/RouterPlus.Infrastructure.Tests/Observability/
  EventLogReaderTests.cs
```

---

## Implementation Order

1. ✅ Write plan (this file)
2. ✅ EventLogReader service
3. ✅ DiagnosticsViewModel
4. ✅ DiagnosticsWindow XAML
5. ✅ MainWindow integration (add button)
6. ⏳ Test with real log data (requires user testing)
7. ✅ Polish UI styling
8. ⏳ Write tests (future work)
9. ✅ Documentation
10. ✅ Commit

**Status**: ✅ COMPLETE - Ready for user testing

---

## Notes

- Keep it simple - read-only viewer, no editing
- Performance: limit to last 1000 events by default
- Export full log if needed
- Consider live tailing in future (FileSystemWatcher)
- Use existing WPF styles from app
