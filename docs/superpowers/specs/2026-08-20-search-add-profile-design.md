# Add Chrome Profile From Search

## Goal

Allow the profile search field to create a usable Chrome profile when the requested name is not already discovered. After creation, the profile must appear in the list, become selected, and work with the existing launch and provider workflows.

## User flow

1. The user enters a non-empty profile name in the profile search field.
2. If no discovered profile has that exact name, the sidebar shows an action labeled `Thêm profile "<name>"`.
3. Activating the action allocates the next unused Chromium directory name (`Profile N`), creates the directory under the configured user-data directory, and stores the requested display name and directory mapping in RouterPlus settings.
4. The app reloads the discovered and managed profiles, selects the new profile, and shows a success status.
5. Existing double-click, 9Router, provider-dashboard, OAuth, and API-key workflows use the selected profile through the existing `ChromeLauncher` path.

## Design

- Keep Chrome's `Local State` and the 9Router database read-only. The app stores only its own managed-profile metadata in the existing RouterPlus settings file.
- Merge profiles discovered from Chrome with managed profiles by stable profile id. Managed metadata wins when Chrome later discovers the same directory, preserving the user-entered display name.
- Allocate a directory that is not present in the discovered profiles, managed mappings, or filesystem. Use the first available `Profile 1`, `Profile 2`, and so on.
- Reject blank names and exact case-insensitive duplicates. Trim surrounding whitespace before validation and persistence.
- Create the directory before exposing the profile to launch workflows. Chrome initializes the profile on its first launch.
- Show the add action only for a non-empty query that has no exact existing name; an existing partial match remains visible as normal search results.

## Persistence

Extend `RouterSettings` with a collection of managed profile records containing:

- `Name`
- `DirectoryName`
- `UserDataDirectory`

The stable `ChromeProfile.Id` remains derived from user-data path and directory name, so provider matching and saved API keys continue to work for the new profile.

## Acceptance criteria

- Typing a new name exposes one add action with that exact trimmed name.
- Activating the action creates and persists a managed profile record.
- The new profile is visible, selected, and launchable without restarting the app.
- Refreshing profiles preserves the managed profile and does not duplicate it when Chrome adds metadata for the directory.
- Duplicate names are not created, and blank/whitespace-only queries do not expose the action.
- Existing profile filtering, status matching, provider actions, and full test/build workflows remain green.

## Non-goals

- Editing Chrome's profile avatar/name in `Local State`.
- Reading or modifying the 9Router SQLite database.
- Creating a new Chrome user-data root; the configured root remains the source of truth.
