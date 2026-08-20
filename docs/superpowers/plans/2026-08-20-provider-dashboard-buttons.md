# Provider Dashboard Buttons Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Open each provider's 9Router dashboard in the selected Chrome profile and normalize provider-card action buttons.

**Architecture:** Reuse ProviderDefinition.BuildDashboardUrl and MainViewModel.LaunchUrlAsync. Add a typed OpenProviderDashboardCommand that validates the selected profile, constructs the provider route from DashboardBaseUrl, and launches it through the existing configured Chrome path. Use a left-aligned WrapPanel for provider actions with Dashboard first and natural 3+1 wrapping for four API-key actions.

**Tech Stack:** .NET 8, WPF/XAML, xUnit, existing AsyncRelayCommand and Chrome launcher abstractions.

---

### Task 1: Lock dashboard URL behavior with tests

**Files:**
- Modify: tests/RouterPlus.Core.Tests/ProviderCatalogTests.cs

- [ ] Add assertions that every catalog provider builds the expected route with a trailing-slash and non-trailing-slash base URL.
- [ ] Run the Core test project and confirm the new expectation fails only if URL construction is wrong.

### Task 2: Add the selected-profile dashboard command

**Files:**
- Modify: src/RouterPlus.App/ViewModels/MainViewModel.cs

- [ ] Expose OpenProviderDashboardCommand as AsyncRelayCommand<ProviderKind> beside the existing provider commands.
- [ ] Implement OpenProviderDashboardAsync(ProviderKind) to require SelectedProfile, call ProviderCatalog.Get(provider).BuildDashboardUrl(DashboardBaseUrl), launch with LaunchUrlAsync, and set a success/error status message.
- [ ] Raise the command's can-execute state when SelectedProfile changes and after initialization.

### Task 3: Add the dashboard action and normalize card buttons

**Files:**
- Modify: src/RouterPlus.App/MainWindow.xaml

- [x] Add a shared provider action-row style with equal button widths, equal heights, left-aligned wrapping, and consistent spacing.
- [x] Bind a Dashboard button to OpenProviderDashboardCommand with the provider kind as its parameter.
- [ ] Keep OAuth/API-key conditional buttons in their existing workflow-specific rows.

### Task 4: Verify behavior and rendered layout

**Files:**
- No additional files.

- [ ] Run the full solution test suite.
- [ ] Run the Release build and git diff --check.
- [ ] Publish/restart the app and verify all five cards show equal action-button dimensions.
- [ ] Verify the dashboard button opens /dashboard/providers/<provider> using the selected Chrome profile.
