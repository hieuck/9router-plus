# Add Chrome Profile From Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the profile search field create, persist, select, and launch a new usable Chromium profile by display name.

**Architecture:** Keep Chrome `Local State` and the 9Router database read-only. Store RouterPlus-managed profile metadata in `RouterSettings`, merge it with discovered profiles by stable profile id, and create only the new profile directory on disk. A dedicated provisioner owns validation and directory allocation; `MainViewModel` owns persistence, selection, command state, and status reporting.

**Tech Stack:** .NET 8, C# records, WPF data binding, `System.Text.Json`, xUnit, existing `ChromeLauncher` and `SettingsStore`.

---

### Task 1: Add managed-profile domain and persistence models

**Files:**
- Create: `src/RouterPlus.Core/Chrome/ManagedChromeProfile.cs`
- Create: `src/RouterPlus.Core/Chrome/ChromeProfileCatalog.cs`
- Modify: `src/RouterPlus.Infrastructure/Storage/RouterSettings.cs`
- Test: `tests/RouterPlus.Core.Tests/ChromeProfileCatalogTests.cs`
- Test: `tests/RouterPlus.Core.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: Write failing merge and persistence tests**

Test that a managed record replaces a discovered profile with the same stable id while preserving the requested name, ignores records from another user-data root, and round-trips through `SettingsStore` without losing `Name`, `DirectoryName`, or `UserDataDirectory`.

- [ ] **Step 2: Run focused tests and verify they fail**

Run:
`& .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~ChromeProfileCatalogTests|FullyQualifiedName~SettingsStoreTests'`

Expected: compile failure because the managed-profile type, merge helper, and settings property do not exist.

- [ ] **Step 3: Implement the models and merge helper**

Add:

```csharp
public sealed record ManagedChromeProfile(
    string Name,
    string DirectoryName,
    string UserDataDirectory);
```

Extend `RouterSettings` with `IReadOnlyList<ManagedChromeProfile> ManagedProfiles = []`. Implement `ChromeProfileCatalog.Merge` to compare canonical user-data paths, replace discovered entries by stable id, and return a name/directory-sorted list.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the same command; expected result is all focused tests passing with no warnings or errors.

- [ ] **Step 5: Commit the persistence unit**

```text
feat: persist managed chrome profiles
```

### Task 2: Allocate and create a new Chromium profile directory

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeProfileProvisioner.cs`
- Test: `tests/RouterPlus.Core.Tests/ChromeProfileProvisionerTests.cs`

- [ ] **Step 1: Write failing provisioner tests**

Use a temporary user-data directory and verify that provisioning trims the display name, creates the matching `Profile <tên>` directory, rejects an existing directory with that name, and rejects blank or case-insensitive duplicate names.

- [ ] **Step 2: Run the provisioner tests and verify they fail**

Run:
`& .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~ChromeProfileProvisionerTests'`

Expected: compile failure because `ChromeProfileProvisioner` does not exist.

- [ ] **Step 3: Implement minimal provisioning**

Implement `Create(string userDataDirectory, string name, IEnumerable<ChromeProfile> discovered, IEnumerable<ManagedChromeProfile> managed)` with path validation, exact-name duplicate checks, a case-insensitive directory-name set, `Profile <tên>` directory-name construction, collision rejection, and `Directory.CreateDirectory` before returning the managed record.

- [ ] **Step 4: Run the provisioner tests and verify they pass**

Run the same command; expected result is all provisioner tests passing.

- [ ] **Step 5: Commit the provisioner**

```text
feat: provision managed chrome profile directories
```

### Task 3: Expose add-profile search state and command

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs:30-415`
- Test: `tests/RouterPlus.Core.Tests/MainViewModelProfileSearchTests.cs`

- [ ] **Step 1: Write failing search-state tests**

Test that a trimmed new query exposes `CanAddProfile == true` and the exact button text, while blank input and an exact case-insensitive existing profile expose no add action. Test that changing the query raises the relevant property notifications.

- [ ] **Step 2: Run the search-state test and verify it fails**

Run:
`& .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~MainViewModelProfileSearchTests'`

Expected: compile failure because the add-profile properties and command do not exist.

- [ ] **Step 3: Implement command state and persistence integration**

Add `_managedProfiles`, `ChromeProfileProvisioner`, `AddProfileCommand`, `CanAddProfile`, and `ProfileAddButtonText`. Load managed records during `InitializeAsync`; include them in `RefreshProfiles` through `ChromeProfileCatalog.Merge`; include them in the existing settings save path; and raise command/property notifications whenever the query or profile collection changes.

Implement `AddProfileAsync` to provision the directory, append and save the managed record, refresh the merged list, select the created id, and report success. On errors, preserve the existing `SetError` logging path and do not add a partial record.

- [ ] **Step 4: Run the search-state tests and verify they pass**

Run the same command; expected result is all search-state tests passing.

- [ ] **Step 5: Commit the view-model behavior**

```text
feat: add profile creation command
```

### Task 4: Add the sidebar action and verify launchability

**Files:**
- Modify: `src/RouterPlus.App/MainWindow.xaml:328-424`
- Modify: `src/RouterPlus.App/MainWindow.xaml:414-560`

- [ ] **Step 1: Add the conditional sidebar action**

Insert an auto-height row between the search field and list. Bind a button to `AddProfileCommand`, bind its content to `ProfileAddButtonText`, and use a local style trigger to show it only when `CanAddProfile` is true. Move the existing `ListBox` to the following star-sized row so the list keeps its scroll behavior.

- [ ] **Step 2: Build the app and inspect XAML compilation**

Run:
`& .\\.dotnet\\dotnet.exe build RouterPlus.sln --no-restore --configuration Debug`

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 3: Launch the published app and verify the user flow**

Open the app, type a unique name into profile search, confirm the button text uses the trimmed query, click it, confirm the new row is selected, double-click it, and verify Chrome starts with the allocated `Profile <tên>` directory. Repeat `Làm mới` and confirm the row remains without duplication.

- [ ] **Step 4: Run the full build script and final checks**

Run:
`.\\scripts\\build.ps1`

Expected: all tests pass, publish succeeds, and `git diff --check` is clean.

- [ ] **Step 5: Commit the UI and final integration**

```text
feat: add chrome profile from search
```
