# Recent Profiles and Quick Launch

## Goal

Surface recently and frequently used Chrome profiles so the user can launch 9Router by clicking a profile. Tracking persists across sessions, and the sidebar exposes a pinned-then-recent list with launch counts.

## Behavior

- Every successful profile launch records the profile in `RecentProfile` storage with the current UTC timestamp and an incremented launch count.
- Profiles that no longer exist on disk are dropped on the next refresh without breaking the list.
- The sidebar Recent section shows up to ten entries: pinned first (sorted by `LastUsedUtc` desc), then unpinned (sorted by `LastUsedUtc` desc). Each row shows the profile name, last-used relative time, and launch count.
- The Recent section hides itself when empty (currently the case after a fresh install).
- Clicking a Recent row launches that profile through the existing Chrome launcher. Pinned status is preserved.
- The pin toggle on each row flips `IsPinned`, reorders the list, and persists to settings.
- The Quick Launch palette uses a text filter to narrow `Profiles` by name; Enter launches the highlighted row, Escape closes the palette, and arrow keys move selection. Down from the last row wraps to the first.
- The profile refresh control refreshes the profile catalog, and the Recent clear button clears all recent entries.

## Storage

`RecentProfile` (already a record in `RouterSettings`) holds:
- `ProfileId`, `ProfileName`, `UserDataDirectory`, `LastUsedUtc`, `LaunchCount`, `IsPinned`.

`SettingsStore` persists `RouterSettings.RecentProfiles` via the existing JSON serializer. The maximum stored list is 10; older unpinned entries are dropped during tracking.

## Architecture

- `MainViewModel` owns tracking (`TrackProfileLaunch`), ordering, persistence triggers, command surface, and Quick Launch palette state.
- `MainViewModel.RecentProfileRows` exposes observable view-model rows used by the sidebar and palette. Each row carries `Profile`, `LastUsedText`, `LaunchCountText`, `IsPinned`, and the commands needed for click and pin.
- `MainWindow` hosts the Quick Launch overlay as a border above the main grid, sharing the same data context as `MainView`; palette-local Escape, arrow, and Enter bindings remain static.
