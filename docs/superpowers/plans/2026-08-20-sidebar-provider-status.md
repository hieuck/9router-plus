# Sidebar and Provider Status Implementation Plan
> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
**Goal:** Center the collapsed profile selection border and show Online/Disable/Error/Not added status on each provider card for the selected profile.
**Architecture:** Reuse `ProfileProviderStatusViewModel` as the source of truth for connection health. `MainViewModel` copies the selected row's provider status into each `ProviderCardViewModel`, while XAML renders a compact color-coded badge. The collapsed sidebar uses a dedicated centered item container so the scrollbar does not affect border alignment.
**Tech Stack:** .NET 8, WPF/XAML, xUnit, existing `ProviderHealthState` resolver.
---
### Task 1: Add display-state tests
**Files:**
- Create: `src/RouterPlus.Core/Providers/ProviderDisplayStatus.cs`
- Test: `tests/RouterPlus.Core.Tests/ProviderDisplayStatusTests.cs`
- [ ] Write tests for `Healthy`, `Disabled`, `Error`, `Missing`, and `Unknown` display labels and colors.
- [ ] Run `& .dotnet\dotnet.exe test tests\RouterPlus.Core.Tests\RouterPlus.Core.Tests.csproj --no-restore --configuration Release`; confirm the new tests fail because the display-state type does not exist.
### Task 2: Wire selected-profile status into provider cards
**Files:**
- Modify: `src/RouterPlus.App/ViewModels/ProviderCardViewModel.cs`
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs`
- Modify: `src/RouterPlus.App/ViewModels/ProfileRowViewModel.cs` only if an accessor is needed.
- [ ] Add a card status property initialized to `Unknown`.
- [ ] Add an update method that accepts the matching `ProfileProviderStatusViewModel` and raises property notifications.
- [ ] Refresh card statuses whenever the selected profile changes, connections are synchronized, or synchronization fails.
- [ ] Run the focused tests and build the solution.
### Task 3: Render provider status badges
**Files:**
- Modify: `src/RouterPlus.App/MainWindow.xaml`
- [ ] Add the status badge beside the existing workflow tag in each provider card header.
- [ ] Bind label, color, and tooltip to the card display-state properties.
- [ ] Keep the badge compact so API-key controls and OAuth buttons retain their current layout.
### Task 4: Center collapsed sidebar borders
**Files:**
- Modify: `src/RouterPlus.App/MainWindow.xaml`
- [ ] Give collapsed list items a fixed centered content width matching the avatar region.
- [ ] Remove asymmetric left/right slack caused by the list scrollbar and hidden columns.
- [ ] Preserve the expanded sidebar layout and selected/hover colors.
### Task 5: Verify runtime behavior
**Files:**
- No additional files.
- [ ] Run the full Release test suite and solution build.
- [ ] Publish and launch `artifacts\publish\RouterPlus.exe`.
- [ ] Verify collapsed selected border is centered around the avatar.
- [ ] Verify provider cards show the selected profile's status with green/yellow/red/gray states.
- [ ] Run `git diff --check`.
