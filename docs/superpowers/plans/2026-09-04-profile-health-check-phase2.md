# Profile Health Check Phase 2: UI Integration

**Gate Context**:
- **Importers/Callers**: Implementation agents, user (requested "triển khai đúng quy trình"), future maintainers
- **Affected API**: MainViewModel (adds CheckAllProfilesHealthCommand, CheckProfileHealthCommand), MainWindow.xaml (adds Health column, context menu), App.xaml.cs (registers ProfileHealthService in DI)
- **Data Schemas**: Uses existing ProfileHealthStatus from Phase 1
- **User's Verbatim Instruction**: "triển khai đúng quy trình" (implement following proper process)

---

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Add UI integration to make Profile Health Check feature usable from the main window.

**Architecture:** Phase 1 built the foundation (models, checker, service). Phase 2 connects it to MainViewModel and XAML UI so users can check profile health and see results.

**Tech Stack:** C# 12, .NET 8, WPF, xUnit

**Spec:** `docs/superpowers/specs/2026-09-03-profile-health-check-design.md` (sections 6.2, 6.3)

## Global Constraints

- Target framework: .NET 8.0
- C# language version: 12
- Test framework: xUnit
- All public APIs must have XML documentation comments
- Health status icons: "✓" (healthy), "⚠" (warning), "✗" (error), "?" (unknown)
- Commands must use `AsyncRelayCommand` from existing codebase pattern
- XAML must follow existing MainWindow.xaml styling patterns

---

## Task 1: Dependency Injection Setup

**Files:**
- Modify: `src/RouterPlus.App/App.xaml.cs` (add ProfileHealthService registration)

**Interfaces:**
- Consumes: `ProfileHealthService` from Phase 1
- Produces: ProfileHealthService registered in DI container

**Steps:**

- [ ] **Step 1: Read existing DI setup**

Read `src/RouterPlus.App/App.xaml.cs` to understand current service registration pattern.

- [ ] **Step 2: Add ProfileHealthService registration**

Add to DI container (likely in `ConfigureServices` or similar):

```csharp
services.AddSingleton<ProfileHealthService>();
```

- [ ] **Step 3: Verify no compilation errors**

Run `dotnet build` to confirm registration is correct.

- [ ] **Step 4: Commit**

```bash
git add src/RouterPlus.App/App.xaml.cs
git commit -m "feat(health): register ProfileHealthService in DI container"
```

---

## Task 2: MainViewModel Health Commands

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs` (add commands)
- Create: `tests/RouterPlus.App.Tests/ViewModels/MainViewModelHealthTests.cs`

**Interfaces:**
- Consumes: 
  - `ProfileHealthService.GetHealthStatusAsync(profile, forceRefresh)`
  - `ProfileRowViewModel.HealthStatus` setter
- Produces:
  - `MainViewModel.CheckAllProfilesHealthCommand: AsyncRelayCommand`
  - `MainViewModel.CheckProfileHealthCommand: AsyncRelayCommand<ProfileRowViewModel>`

**Steps:**

- [ ] **Step 1: Write test for CheckAllProfilesHealthCommand**

```csharp
[Fact]
public async Task CheckAllProfilesHealthCommand_UpdatesAllProfileHealthStatus()
{
    // Arrange: MainViewModel with 3 profiles, mocked ProfileHealthService
    // Act: Execute CheckAllProfilesHealthCommand
    // Assert: All 3 ProfileRowViewModel.HealthStatus are non-null
}
```

- [ ] **Step 2: Run test to verify failure**

`dotnet test --filter "FullyQualifiedName~MainViewModelHealthTests"`

- [ ] **Step 3: Add ProfileHealthService field to MainViewModel**

Constructor injection:

```csharp
private readonly ProfileHealthService _profileHealthService;

public MainViewModel(..., ProfileHealthService profileHealthService)
{
    // existing initialization
    _profileHealthService = profileHealthService;
}
```

- [ ] **Step 4: Implement CheckAllProfilesHealthCommand**

```csharp
public AsyncRelayCommand CheckAllProfilesHealthCommand { get; }

// In constructor:
CheckAllProfilesHealthCommand = new AsyncRelayCommand(
    CheckAllProfilesHealthAsync,
    () => ProfileRows.Any());

private async Task CheckAllProfilesHealthAsync()
{
    foreach (var row in ProfileRows)
    {
        var status = await _profileHealthService.GetHealthStatusAsync(
            row.Profile,
            forceRefresh: true);
        row.HealthStatus = status;
        
        // Small delay to avoid overwhelming UI
        await Task.Delay(50);
    }
}
```

- [ ] **Step 5: Implement CheckProfileHealthCommand (single profile)**

```csharp
public AsyncRelayCommand<ProfileRowViewModel> CheckProfileHealthCommand { get; }

// In constructor:
CheckProfileHealthCommand = new AsyncRelayCommand<ProfileRowViewModel>(
    CheckProfileHealthAsync,
    row => row != null);

private async Task CheckProfileHealthAsync(ProfileRowViewModel? row)
{
    if (row == null) return;
    
    var status = await _profileHealthService.GetHealthStatusAsync(
        row.Profile,
        forceRefresh: true);
    row.HealthStatus = status;
}
```

- [ ] **Step 6: Run test to verify pass**

- [ ] **Step 7: Write test for CheckProfileHealthCommand**

```csharp
[Fact]
public async Task CheckProfileHealthCommand_UpdatesSingleProfileHealthStatus()
{
    // Arrange: MainViewModel with 1 profile
    // Act: Execute CheckProfileHealthCommand with that profile
    // Assert: HealthStatus updated
}
```

- [ ] **Step 8: Run test to verify pass**

- [ ] **Step 9: Commit**

```bash
git add src/RouterPlus.App/ViewModels/MainViewModel.cs
git add tests/RouterPlus.App.Tests/ViewModels/MainViewModelHealthTests.cs
git commit -m "feat(health): add health check commands to MainViewModel

- Add CheckAllProfilesHealthCommand for batch checking
- Add CheckProfileHealthCommand for single profile
- Inject ProfileHealthService via constructor
- 2 unit tests covering both commands"
```

---

## Task 3: XAML Health Status Column

**Files:**
- Modify: `src/RouterPlus.App/Views/MainWindow.xaml` (add Health column to DataGrid)

**Interfaces:**
- Consumes: 
  - `ProfileRowViewModel.HealthStatusIcon`
  - `ProfileRowViewModel.HealthStatusText`
- Produces: Visual health status column in profile list

**Steps:**

- [ ] **Step 1: Read MainWindow.xaml DataGrid structure**

Locate the profiles DataGrid to understand column structure.

- [ ] **Step 2: Add Health status column**

Insert before or after Name column (check spec for placement):

```xml
<DataGridTemplateColumn Header="Health" Width="100">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal" 
                        ToolTip="{Binding HealthStatusText}">
                <TextBlock Text="{Binding HealthStatusIcon}" 
                           FontSize="16"
                           Margin="4,0"
                           VerticalAlignment="Center" />
                <TextBlock Text="{Binding HealthStatusText}" 
                           FontSize="11"
                           Foreground="Gray"
                           VerticalAlignment="Center"
                           TextTrimming="CharacterEllipsis"
                           MaxWidth="60" />
            </StackPanel>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

- [ ] **Step 3: Verify XAML compiles**

Run `dotnet build` to confirm no XAML errors.

- [ ] **Step 4: Commit**

```bash
git add src/RouterPlus.App/Views/MainWindow.xaml
git commit -m "feat(health): add health status column to profile list

- Shows icon (✓/⚠/✗/?) and status text
- Tooltip displays full health message
- Column width 100px"
```

---

## Task 4: Context Menu Integration

**Files:**
- Modify: `src/RouterPlus.App/Views/MainWindow.xaml` (add context menu items)

**Interfaces:**
- Consumes:
  - `MainViewModel.CheckProfileHealthCommand`
  - `MainViewModel.CheckAllProfilesHealthCommand`
- Produces: Context menu items for health checking

**Steps:**

- [ ] **Step 1: Locate DataGrid ContextMenu**

Find the existing context menu for profile rows.

- [ ] **Step 2: Add "Check Profile Health" menu item**

```xml
<MenuItem Header="Check Profile Health" 
          Command="{Binding DataContext.CheckProfileHealthCommand, RelativeSource={RelativeSource AncestorType=Window}}"
          CommandParameter="{Binding}" />
```

- [ ] **Step 3: Add separator and "Check All Profiles Health"**

Add to the main window's command bar or as a global menu item:

```xml
<MenuItem Header="Check All Profiles Health"
          Command="{Binding CheckAllProfilesHealthCommand}" />
```

- [ ] **Step 4: Verify XAML compiles**

- [ ] **Step 5: Commit**

```bash
git add src/RouterPlus.App/Views/MainWindow.xaml
git commit -m "feat(health): add health check context menu items

- Right-click profile: Check Profile Health
- Command bar: Check All Profiles Health
- Bound to MainViewModel commands"
```

---

## Task 5: Documentation Update

**Files:**
- Modify: `docs/features/profile-health-check.md` (add Phase 2 UI usage)

**Interfaces:**
- Consumes: Completed UI implementation
- Produces: Updated user-facing documentation

**Steps:**

- [ ] **Step 1: Add "How to Use" section**

```markdown
## How to Use

### Check Single Profile

1. Right-click a profile in the list
2. Select "Check Profile Health"
3. Health status appears in the Health column

### Check All Profiles

1. Click "Check All Profiles Health" in the toolbar
2. Wait for batch check to complete (~50ms per profile)
3. All health statuses update in the Health column

### Understanding the Health Column

- **✓ (Green)**: Profile healthy, no issues
- **⚠ (Yellow)**: Minor warnings, profile may still work
- **✗ (Red)**: Critical errors, profile likely unusable
- **?**: Health status unknown (not checked yet)

Hover over the health column to see the full status message.
```

- [ ] **Step 2: Update "What It Checks" section**

Change from "Phase 1" to "Current Version" and note Phase 2 (vault/credentials) still deferred.

- [ ] **Step 3: Commit**

```bash
git add docs/features/profile-health-check.md
git commit -m "docs(health): add Phase 2 usage instructions

- How to check single profile (context menu)
- How to check all profiles (toolbar command)
- Health column icon meanings
- Updated scope to reflect Phase 2 completion"
```

---

## Self-Review Checklist

Before marking complete, verify:

**1. Integration Complete**
- [x] ProfileHealthService registered in DI
- [x] MainViewModel has both health commands
- [x] Health column visible in profile list
- [x] Context menu has health check item
- [x] Documentation updated

**2. Commands Work**
- [ ] CheckProfileHealthCommand updates single profile
- [ ] CheckAllProfilesHealthCommand updates all profiles
- [ ] Commands disabled when no profiles present
- [ ] UI responsive during batch check (50ms delays)

**3. Visual Polish**
- [ ] Icons display correctly (✓/⚠/✗/?)
- [ ] Tooltip shows full status message
- [ ] Column width appropriate (not too wide/narrow)
- [ ] Text doesn't overflow

**4. Testing**
- [x] Unit tests for both commands
- [ ] Manual test: check single profile
- [ ] Manual test: check all profiles
- [ ] Manual test: verify icons for each health level

---

## Execution Handoff

**Phase 2 Scope**: UI integration only - MainViewModel commands, XAML health column, context menu, updated documentation.

**Still Deferred to Phase 3**: Vault/credentials/provider health checks, health issues popover, automatic health checking.

Execute using superpowers:subagent-driven-development.
