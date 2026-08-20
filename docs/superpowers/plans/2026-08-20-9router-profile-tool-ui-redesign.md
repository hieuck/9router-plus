# 9Router Profile Tool UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Translate the approved browser mockup into the WPF shell without changing provider workflows or secret-handling behavior.

**Architecture:** Keep `MainViewModel` as the source of profile/provider state and add one presentation property for collapsing the profile sidebar. Rebuild the WPF window as a dashboard shell: profile sidebar, selected-profile hero, provider workspace, compact snapshot/activity rail, and an overlay settings drawer. Consolidate visual tokens and control styles in `Theme.xaml`.

**Tech Stack:** .NET 8 WPF, XAML styles/templates, existing `MainViewModel`, existing provider card view models, existing commands.

---

### Task 1: Add sidebar presentation state

**Files:**
- Modify: `src/RouterPlus.App/ViewModels/MainViewModel.cs`

- [ ] Add `IsProfileSidebarCollapsed` with notification and a default value of `false`.
- [ ] Keep the state presentation-only; do not alter profile filtering, selection, launch, or provider commands.
- [ ] Build the app project to verify the new binding surface compiles.

### Task 2: Rebuild the WPF shell around the approved layout

**Files:**
- Modify: `src/RouterPlus.App/MainWindow.xaml`

- [ ] Replace the two-column GroupBox layout with a sidebar/main dashboard grid and bind the sidebar column width to `IsProfileSidebarCollapsed` through XAML triggers.
- [ ] Preserve profile search, numbering, provider status badges, selection, and double-click launch in the sidebar.
- [ ] Add the selected-profile hero with profile name/path, connection count, dashboard/open-profile actions, and summary metrics.
- [ ] Render provider cards in a two-column workspace, preserving API key fields, show/hide behavior, quick links, OAuth/device-code actions, and status text.
- [ ] Add a compact right rail for profile snapshot and recent activity without introducing fake production bindings; use existing status/log bindings where available.
- [ ] Convert settings from the main content flow into a compact overlay drawer controlled by the existing `IsSettingsExpanded` property.
- [ ] Keep the log panel available in the lower content area with the existing copy action and bound `LogText`.

### Task 3: Refresh WPF visual tokens and control styles

**Files:**
- Modify: `src/RouterPlus.App/Styles/Theme.xaml`

- [ ] Replace the flat palette with the approved navy/teal token set, preserving resource keys already referenced by view models and XAML where practical.
- [ ] Add rounded card, button, input, list-row, and status-pill styles for consistent spacing and hover/selected states.
- [ ] Add compact typography, muted labels, accent/connected/pending brushes, and a reusable card border style.
- [ ] Ensure controls remain readable at the existing minimum window size and do not reintroduce horizontal scrolling.

### Task 4: Verify the redesign without changing workflows

**Files:**
- Test: existing `tests/RouterPlus.Core.Tests` suite

- [ ] Run `dotnet test RouterPlus.sln --no-restore --configuration Release` and confirm all tests pass.
- [ ] Run `dotnet build RouterPlus.sln --no-restore --configuration Release` and confirm zero warnings/errors.
- [ ] Publish `src/RouterPlus.App/RouterPlus.App.csproj` to `artifacts/publish` after stopping the old published process.
- [ ] Launch the published app and verify the window responds, the profile list is selectable, settings drawer toggles, and provider-card bindings render.
