# Recent Profiles and Quick Launch

## Goal

Surface recently and frequently used Chrome profiles so the user can launch 9Router in one click or one keystroke. Tracking persists across sessions, the sidebar exposes a pinned-then-recent list with launch counts and hotkey hints, and keyboard shortcuts cover all ten slots plus a search palette for older entries.

## Behavior

- Every successful profile launch records the profile in `RecentProfile` storage with the current UTC timestamp and an incremented launch count.
- Profiles that no longer exist on disk are dropped on the next refresh without breaking the list.
- The sidebar Recent section shows up to ten entries: pinned first (sorted by `LastUsedUtc` desc), then unpinned (sorted by `LastUsedUtc` desc). Each row shows the keyboard hint (`Ctrl+1`..`Ctrl+0`), the profile name, last-used relative time, and launch count.
- The Recent section hides itself when empty (currently the case after a fresh install).
- Clicking a Recent row launches that profile through the existing Chrome launcher. Pinned status is preserved.
- The pin toggle on each row flips `IsPinned`, reorders the list, and persists to settings.
- `Ctrl+1`..`Ctrl+9` launch slots 1..9. `Ctrl+0` launches slot 10. Shortcuts outside the visible range are ignored and produce a status message.
- `Ctrl+Shift+K` opens a Quick Launch palette overlay with a text filter that narrows `Profiles` by name; Enter launches the highlighted row, Escape closes the palette, arrow keys move selection, Down from the last row wraps to the first.
- `F5` refreshes the profile catalog. `Ctrl+Shift+R` clears all recent entries after a one-step confirmation dialog.

## Storage

`RecentProfile` (already a record in `RouterSettings`) holds:
- `ProfileId`, `ProfileName`, `UserDataDirectory`, `LastUsedUtc`, `LaunchCount`, `IsPinned`.

`SettingsStore` persists `RouterSettings.RecentProfiles` via the existing JSON serializer. The maximum stored list is 10; older unpinned entries are dropped during tracking.

## Architecture

- `MainViewModel` owns tracking (`TrackProfileLaunch`), ordering, persistence triggers, command surface, and Quick Launch palette state.
- `MainViewModel.RecentProfileRows` exposes observable view-model rows used by the sidebar and palette. Each row carries `Profile`, `KeyboardHint` (string), `LastUsedText`, `LaunchCountText`, `IsPinned`, and the commands needed for click, pin, and launch-by-hotkey.
- `MainWindow` adds `KeyBinding` entries for `Ctrl+0` and `Ctrl+1`..`Ctrl+9`, plus `Ctrl+Shift+K` and `F5`. It hosts the Quick Launch overlay as a `Window` or border above the main grid, sharing the same data context as `MainView`.
