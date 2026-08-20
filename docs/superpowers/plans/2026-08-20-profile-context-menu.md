# Profile Context Menu Implementation Plan

> **For agentic workers:** Inline execution in this session. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe profile-scoped context-menu actions for Google login, folder access, name copying, and confirmed deletion.

**Architecture:** Keep the WPF context menu in the profile row template and use code-behind only for menu event routing, clipboard access, Explorer launch, confirmation, and selection of the clicked row. Put filesystem deletion in an infrastructure service with an explicit root/path guard; keep Google-login launching, profile persistence, and refresh orchestration in `MainViewModel`.

**Tech Stack:** .NET 8, WPF/XAML, existing `ChromeLauncher`, `SettingsStore`, xUnit.

---

### Task 1: Add safe profile-directory deletion service

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeProfileDeleter.cs`
- Test: `tests/RouterPlus.Core.Tests/ChromeProfileDeleterTests.cs`

- [ ] **Step 1: Write failing tests**

Test that deleting a profile removes its directory but preserves the User Data root, and that a target outside the root or equal to the root is rejected without deleting anything.

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:
`& .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~ChromeProfileDeleterTests'`

Expected: compile failure because `ChromeProfileDeleter` does not exist.

- [ ] **Step 3: Implement the minimal deleter**

Add `Delete(ChromeProfile profile, string userDataDirectory)`; normalize full paths, require the profile directory parent to equal the configured root, allow a missing target, and call `Directory.Delete(profile.ProfilePath, recursive: true)` only after validation.

- [ ] **Step 4: Run the focused tests and verify they pass**

Run the same command; expected result is all deleter tests passing.

### Task 2: Add view-model commands and deletion persistence

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs`
- Test: `tests/RouterPlus.Core.Tests/MainViewModelProfileContextMenuTests.cs`

- [ ] **Step 1: Write failing view-model tests**

Using a temporary Chrome root, fake `chrome.exe`, temporary `SettingsStore`, and a minimal `Local State`, test that deleting the selected profile removes the directory, removes its managed mapping, refreshes the list, and selects the next remaining profile; test that Google-login launching reports the missing-selection status when no profile is selected.

- [ ] **Step 2: Run the focused tests and verify they fail**

Run:
`& .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~MainViewModelProfileContextMenuTests'`

Expected: compile failure because the commands and delete method do not exist.

- [ ] **Step 3: Implement the public operations and persistence flow**

Inject `ChromeProfileDeleter`, add `OpenSelectedGoogleLoginAsync` and `DeleteSelectedProfileAsync`, reuse `LaunchUrlAsync` for Google, invoke the deleter, remove matching managed records, save `BuildSettings()`, refresh profiles, and preserve the existing error/status path. Keep Explorer launch in `MainWindow` so it can use the row context-menu event directly.

- [ ] **Step 4: Exclude missing profile directories during refresh**

Filter discovered and managed profiles by `Directory.Exists(profile.ProfilePath)` before merging so a deleted profile does not reappear from stale Chrome `Local State` metadata.

- [ ] **Step 5: Run the focused tests and verify they pass**

Run the same command; expected result is all context-menu view-model tests passing.

### Task 3: Add the WPF context menu and confirmation flow

**Files:**
- Modify: `src/RouterPlus.App/MainWindow.xaml`
- Modify: `src/RouterPlus.App/MainWindow.xaml.cs`

- [ ] **Step 1: Add the row context menu**

Add menu items for `Đăng nhập Google bằng Chrome`, `Mở thư mục profile`, `Sao chép tên profile`, and `Xóa profile…` to the profile-row template; handle `ContextMenuOpening` on the row root by assigning `SelectedProfile` from the row's `Profile` value before the menu opens.

- [ ] **Step 2: Wire non-destructive actions**

Route Google to `OpenSelectedGoogleLoginAsync`; open `SelectedProfile.ProfilePath` with Explorer; copy the row name with `System.Windows.Clipboard.SetText`; report success or failure through the existing status/log path.

- [ ] **Step 3: Add delete confirmation**

Show a `MessageBox` containing the profile name and exact profile directory, continue only for `Yes`, then await `DeleteSelectedProfileAsync`; leave the row and settings unchanged on `No`.

- [ ] **Step 4: Build the WPF app**

Run:
`& .\\.dotnet\\dotnet.exe build RouterPlus.sln --no-restore --configuration Debug`

Expected: build succeeds with zero warnings and zero errors.

### Task 4: Verify and document

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Run the full test suite**

Run:
`& .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --no-restore`

Expected: all tests pass.

- [ ] **Step 2: Update the feature list**

Document the right-click actions and clarify that deletion removes only the selected profile directory after confirmation.

- [ ] **Step 3: Run final checks**

Run `git diff --check` and `scripts/build.ps1`; expected result is clean diff checks, passing tests, Release build, and publish. Do not create a commit unless the user explicitly requests one.
