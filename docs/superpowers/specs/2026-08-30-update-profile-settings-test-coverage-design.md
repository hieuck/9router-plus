# Update Checker and Profile/Settings Test Coverage Design

**Date:** 2026-08-30

**Status:** Proposed
**Scope:** Update checker and Profile/Settings only. Credentials Manager is explicitly out of scope because another agent owns that work.

## Goal

Provide reliable coverage at three levels for the update checker and Profile/Settings workflows: deterministic unit tests for domain decisions, integration tests for filesystem/process boundaries, and WPF UI tests for local user journeys plus a small CI smoke suite.

## Existing Context

- The solution targets .NET 8 on Windows.
- Unit and integration tests use xUnit and are split across `RouterPlus.Core.Tests`, `RouterPlus.Infrastructure.Tests`, and `RouterPlus.App.Tests`.
- WPF E2E tests use FlaUI (`FlaUI.Core` and `FlaUI.UIA3`) in `RouterPlus.App.E2E`.
- `TestEnvironment` creates two synthetic profiles, `Harness Alpha` and `Harness Beta`, and an isolated remembered Google vault.
- `AppProcess` launches the built `RouterPlus.exe` with `ROUTERPLUS_HARNESS=1` and `ROUTERPLUS_HARNESS_ROOT`.
- The release workflow intentionally excludes E2E tests because GUI automation is not available in the package job.
- The CI workflow currently has no dedicated E2E smoke job.

## Requirements

### Unit coverage

1. `GitHubReleaseClient` must be covered for:
   - successful stable release selection;
   - ignored draft, prerelease, and personal tags;
   - HTTP 404, 403, and 5xx failures preserving the response status;
   - malformed JSON and missing required assets;
   - rejected asset host, redirect, duplicate asset, and invalid size cases;
   - no application data, secrets, or authorization header in requests.
2. `MainViewModel` update state must be covered for:
   - checking, available, current, failed, cancelled, and unsupported-platform states;
   - safe user-facing error messages without exception details;
   - explicit confirmation before installation.
3. Profile and settings behavior must be covered for:
   - search and add-action state;
   - provider filter cycle and unassigned filter interaction;
   - selecting one/all profiles and toolbar state notifications;
   - dashboard URL, Chrome executable, and Chrome data directory validation;
   - settings persistence including window placement and quota markers.

### Integration coverage

1. `SelfUpdateService` must be tested with a synthetic HTTP handler and ZIP package for:
   - download, checksum verification, extraction, and required files;
   - partial download failure cleanup;
   - cancellation/failure not leaving a usable staging directory.
2. `SettingsStore` must be tested with temporary files for atomic save/load and marker preservation.
3. Provider/vault integration is out of scope for this spec; existing or concurrent Credentials work owns it.
4. No integration test may call the real GitHub API or write to the user's normal settings/update directory without an isolated temporary root.

### Local E2E coverage

1. Keep FlaUI as the WPF automation layer.
2. Add deterministic local journeys for:
   - app startup and synthetic profile rendering;
   - profile search filtering;
   - provider filter cycle through the visible profile list;
   - select-all/deselect-all toolbar behavior;
   - settings drawer validation for invalid paths and recovery to valid values;
   - update-check UI using a test-controlled service or harness response, not the live GitHub API.
3. Each journey must use `TestEnvironment`, clean up its app process, and capture diagnostic artifacts on failure.
4. Avoid assertions on passwords, API keys, email addresses, machine identifiers, or live release content.

### CI smoke E2E coverage

1. Add a separate Windows workflow/job for E2E smoke tests; do not add E2E to the release package test step.
2. Build the Debug WPF app before running E2E so `RouterPlus.exe` exists at the path used by `AppProcess`.
3. Run one deterministic smoke journey sequentially with a single worker to avoid desktop contention.
4. Upload test results and failure diagnostics (logs/screenshots) with `if: always()`.
5. CI smoke must use the harness environment and must not depend on the public GitHub Releases API, real Chrome profiles, or user credentials.
6. Preserve the existing unit/integration CI and release gates.

## Design Decisions

### Test seams

- Continue injecting `IUpdateService` into `MainViewModel` for state-level tests.
- Use an in-memory `HttpMessageHandler` for GitHub and asset responses.
- Use temporary directories for `SettingsStore`, update staging, and package files.
- For E2E update UI, add only the smallest test seam needed to provide a deterministic update result; do not introduce a general runtime plugin system.

### E2E stability

- Use condition-based polling through existing FlaUI `Retry` helpers rather than fixed sleeps.
- Run app lifecycle tests serially in CI.
- Keep local E2E tests opt-in or separately filterable so normal unit/integration feedback remains fast.
- Retain artifacts only on failure where supported, with a clear temporary-root naming convention.

### Out of scope

- Credentials Manager implementation or tests owned by another agent.
- Live GitHub release checks in CI.
- Self-hosted runners or production deployment changes.
- Replacing FlaUI with Playwright; this is a native WPF application.
- Refactoring unrelated existing formatting or asynchronous helper methods.

## Acceptance Criteria

- Unit and integration tests cover all listed behaviors and use only synthetic data.
- Local E2E smoke journeys pass on an interactive Windows desktop after a Debug build.
- CI E2E smoke runs in a dedicated Windows job and uploads diagnostics on failure.
- Release workflow continues to pass without requiring an interactive desktop.
- Full non-E2E solution test run passes with zero failures.
- No Credentials Manager files are modified by this work.
