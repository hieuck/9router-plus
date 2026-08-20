# 9Router Profile Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. This session follows the same checkpoints directly because no separate worker session is available.

**Goal:** Build a Windows WPF tool that launches Chrome profiles and guides provider connection setup for 9Router.

**Architecture:** Keep deterministic provider logic and secret handling in a testable core/infrastructure layer. Use a thin WPF shell for profile selection, quick links, provider buttons, and workflow status. Prefer the local 9Router API for mutations and use browser navigation only for user-led authentication.

**Tech Stack:** .NET 8 WPF, `HttpClient`, `System.Text.Json`, Windows DPAPI, Playwright-compatible browser launch abstraction, xUnit.

---

### Task 1: Create the solution and test projects

**Files:**
- Create: `src/RouterPlus.Core/RouterPlus.Core.csproj`
- Create: `src/RouterPlus.Infrastructure/RouterPlus.Infrastructure.csproj`
- Create: `src/RouterPlus.App/RouterPlus.App.csproj`
- Create: `tests/RouterPlus.Core.Tests/RouterPlus.Core.Tests.csproj`
- Create: `RouterPlus.sln`
- Create: `.gitignore`

- [ ] Create the solution, project references, WPF target, and test package references.
- [ ] Add the local SDK bootstrap instructions so builds use `.dotnet/dotnet.exe` when the machine has no global SDK.
- [ ] Run `dotnet test` with the local SDK and confirm the empty test project discovers successfully.

### Task 2: Add failing core tests

**Files:**
- Create: `tests/RouterPlus.Core.Tests/ChromeProfileParserTests.cs`
- Create: `tests/RouterPlus.Core.Tests/ProviderCatalogTests.cs`
- Create: `tests/RouterPlus.Core.Tests/PriorityCalculatorTests.cs`
- Create: `tests/RouterPlus.Core.Tests/ProfileSecretKeyTests.cs`

- [ ] Test parsing Chrome `profile.info_cache` into stable profile records without exposing unrelated JSON values.
- [ ] Test provider buttons map to dashboard paths and quick-link URLs for OpenRouter, Ollama, and Kimchi.
- [ ] Test the next priority is one greater than the largest existing priority, including an empty list.
- [ ] Test the secret key is stable for the same Chrome profile/provider and differs for another provider.
- [ ] Run the focused tests and confirm they fail because the production types do not exist yet.

### Task 3: Implement the core models and decisions

**Files:**
- Create: `src/RouterPlus.Core/Models/ChromeProfile.cs`
- Create: `src/RouterPlus.Core/Models/ProviderDefinition.cs`
- Create: `src/RouterPlus.Core/Models/ProviderConnection.cs`
- Create: `src/RouterPlus.Core/Models/WorkflowState.cs`
- Create: `src/RouterPlus.Core/ProviderCatalog.cs`
- Create: `src/RouterPlus.Core/PriorityCalculator.cs`
- Create: `src/RouterPlus.Core/ProfileSecretKey.cs`

- [ ] Implement immutable records and provider definitions for Codex, Kiro, OpenRouter, Ollama Cloud, and Kimchi.
- [ ] Implement URL construction, quick-link metadata, and provider workflow kind decisions.
- [ ] Implement priority calculation and stable profile/provider secret keys.
- [ ] Run the focused tests and confirm they pass.

### Task 4: Implement infrastructure services

**Files:**
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeLocator.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeProfileReader.cs`
- Create: `src/RouterPlus.Infrastructure/Chrome/ChromeLauncher.cs`
- Create: `src/RouterPlus.Infrastructure/Router/RouterApiClient.cs`
- Create: `src/RouterPlus.Infrastructure/Security/DpapiSecretVault.cs`
- Create: `src/RouterPlus.Infrastructure/Storage/SettingsStore.cs`

- [ ] Discover Chrome from common install paths and the registry without hard-coding one user directory.
- [ ] Parse `Local State` profile metadata and launch a selected profile with the configured dashboard URL.
- [ ] Implement local API calls for listing, creating, updating, and polling provider connections with redacted errors.
- [ ] Protect API keys with `ProtectedData` and persist only encrypted values.
- [ ] Add cancellation support to polling and external-page launch methods.

### Task 5: Build the WPF shell

**Files:**
- Create: `src/RouterPlus.App/App.xaml`
- Create: `src/RouterPlus.App/App.xaml.cs`
- Create: `src/RouterPlus.App/MainWindow.xaml`
- Create: `src/RouterPlus.App/MainWindow.xaml.cs`
- Create: `src/RouterPlus.App/ViewModels/MainViewModel.cs`
- Create: `src/RouterPlus.App/Styles/Theme.xaml`

- [ ] Render profile list, refresh/add path actions, double-click launch, dashboard URL settings, and workflow status.
- [ ] Render provider action buttons and quick links with clear labels.
- [ ] Add secure API-key input, provider/profile association, last-priority behavior, and explicit user-led auth prompts.
- [ ] Keep secrets out of labels, exceptions, logs, and status text.

### Task 6: Add packaging and documentation

**Files:**
- Create: `README.md`
- Create: `scripts/bootstrap-dotnet.ps1`
- Create: `scripts/build.ps1`
- Modify: `docs/superpowers/specs/2026-08-20-9router-profile-tool-design.md`

- [ ] Document local SDK setup, first launch, Chrome profile requirements, OAuth handoff, and DPAPI scope.
- [ ] Add a build script that restores, tests, builds, and publishes a Windows executable.
- [ ] Run the full test/build/publish verification and report any environment-only limitations.
