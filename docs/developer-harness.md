# Developer Harness

The repository includes a native WPF E2E harness built with FlaUI. It runs RouterPlus in a child process with synthetic profile data and exercises the real profile-list context menu without reading user settings, Chrome profiles, vault data, credentials, or external services.

## Run the loop

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\dev-harness\run-debug-loop.ps1 -KeepArtifacts
```

The default filter runs `ProfileContextMenuTests`. To run the startup smoke test:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\dev-harness\run-debug-loop.ps1 `
  -Filter "FullyQualifiedName~StartupSmokeTests"
```

Use `-Configuration Release` only for build compatibility checks; native UI tests are intended for Debug.

## What it does

1. Builds `RouterPlus.App`.
2. Builds and runs `tests/RouterPlus.App.E2E`.
3. Creates a unique temporary root under `%TEMP%\RouterPlusHarness`.
4. Sets `ROUTERPLUS_HARNESS=1` and `ROUTERPLUS_HARNESS_ROOT` only on the child RouterPlus process.
5. Starts the WPF app through FlaUI UIA3.
6. Finds the synthetic profiles `Harness Alpha` and `Harness Beta`.
7. Performs a real right-click at the profile item's screen bounds.
8. Finds the WPF popup menu by UI Automation `Menu` control type.
9. Verifies menu headers, repeated right-click behavior, and menu-open timing.
10. Captures a screenshot/UI tree under the temporary artifact directory on a menu timeout.
11. Closes the child process and cleans up the temporary root.

Native UI tests are serialized because they control the desktop mouse and keyboard. Do not run multiple FlaUI tests in parallel on the same interactive desktop.

## Isolation guarantees

Harness mode is selected only when the child process receives both:

```text
ROUTERPLUS_HARNESS=1
ROUTERPLUS_HARNESS_ROOT=<unique temporary directory>
```

In harness mode, the app:

- uses synthetic `Harness Alpha` and `Harness Beta` profiles;
- uses an isolated settings path;
- skips the setup wizard;
- skips provider network synchronization;
- skips saved credential loading;
- does not launch Chrome;
- does not read the user's normal LocalApplicationData settings or Chrome User Data.

Normal app startup is unchanged when `ROUTERPLUS_HARNESS` is absent.

## Live read-only checks

The live suite is opt-in and uses a real Chrome profile only when both variables are set:

```powershell
$env:ROUTERPLUS_LIVE_E2E = "1"
$env:ROUTERPLUS_LIVE_PROFILE = "<profile name>"
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj `
  --filter "FullyQualifiedName~LiveProfileReadOnlyTests"
```

Live read-only tests verify profile discovery, selection stability, and context-menu inspection. The dialog cancellation test opens the Google Auto Login dialog (which may load configuration from the user's saved Chrome profile) and immediately cancels it. The suite does not click `Xóa profile…`, does not submit credentials, and does not start Auto Login. Keep the normal synthetic filter (`FullyQualifiedName!~Live`) as the default for safe local and CI runs.

## Failure artifacts

Pass `-KeepArtifacts` to preserve temporary run directories. On context-menu timeout, the driver writes:

```text
%TEMP%\RouterPlusHarness\<run-id>\artifacts\context-menu-timeout.png
%TEMP%\RouterPlusHarness\<run-id>\artifacts\context-menu-timeout.tree.txt
```

These artifacts are intended for diagnosing UI Automation and rendering regressions.
