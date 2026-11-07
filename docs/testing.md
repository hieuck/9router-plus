# Testing Guide

## Test structure

```
tests/
  RouterPlus.Core.Tests/         # Unit tests for core domain models, TOTP, state machines
  RouterPlus.Infrastructure.Tests/ # Unit tests for Chrome automation, vault, OAuth, API client
  RouterPlus.App.Tests/          # Unit tests for ViewModels and WPF command logic
  RouterPlus.Updater.Tests/      # Unit tests for self-update package verification
  RouterPlus.App.E2E/            # End-to-end tests (launch real app, GUI automation via FlaUI)
  TestHelpers/                   # Shared test utilities (TestDirectory, fake browsers, etc.)
```

## Running tests

```powershell
# Full suite (excludes E2E by default)
.\.dotnet\dotnet.exe test RouterPlus.sln --configuration Release

# Single project
.\.dotnet\dotnet.exe test tests/RouterPlus.Infrastructure.Tests/RouterPlus.Infrastructure.Tests.csproj --configuration Release

# Specific test class
.\.dotnet\dotnet.exe test tests/RouterPlus.Infrastructure.Tests --configuration Release --filter "FullyQualifiedName~GoogleLoginStateMachine"

# With coverage
.\.dotnet\dotnet.exe test RouterPlus.sln --configuration Release --collect:"XPlat Code Coverage"
```

## E2E tests

E2E tests launch the real application using FlaUI (UI Automation). They are **skipped by default**.

```powershell
# Enable E2E tests
$env:ROUTERPLUS_RUN_E2E = "1"
.\.dotnet\dotnet.exe test tests/RouterPlus.App.E2E --configuration Release
```

E2E tests require a GUI session (no headless). Do not run in CI without a desktop.

## Test categories

Tests are in the default xUnit collection with `parallelizeTestCollections=false` (see `xunit.runner.json`). This is intentional — Chrome automation tests are not re-entrant.

| Filter | What it runs |
|---|---|
| `FullyQualifiedName~GoogleLoginStateMachine` | TOTP state machine + 8 audit scenarios |
| `FullyQualifiedName~AgnesKeyFlow` | Agnes API key orchestration (28 tests) |
| `FullyQualifiedName~Security` | Vault encryption/decryption, PBKDF2, AES-GCM |
| `FullyQualifiedName~GoogleOAuthPageDetector` | Page state detection |
| `FullyQualifiedName~RouterApiClient` | HTTP client + retry/quota |
| `Category!=E2E` | Everything except E2E (default CI filter) |

## Conventions

- **xUnit** with `[Fact]` / `[Theory]`
- **Moq** for external dependencies — never reach real filesystem, registry, or network in unit tests
- **Behavior-style names**: `WhenXxx_ThenYyy` or `Xxx_ReturnsYyy_WhenZzz`
- Test + implementation in the **same commit** — never commit a red test alone
- 80%+ coverage target across all test projects

## Mock/Fixture patterns

### Fake browser with state sequence

```csharp
var browser = new FakeAgnesBrowserWithStateSequence
{
    State = InitialState(),
    StateSequence = new Queue<AgnesApiKeyPageState>(new[] { State1(), State2() }),
    MaxReadCount = 50   // safety net — throws TaskCanceledException after 50 reads
};
```

`MaxReadCount` prevents infinite loops when the queue empties and the orchestrator keeps polling.

### Isolated temp directories

```csharp
var dir = TestDirectory.Create();
try { /* test */ }
finally { TestDirectory.Delete(dir); }
```

### Vault tests

```csharp
var paths = new GoogleAccountVaultPaths(tempDir);
var store = new GoogleAccountVaultStore(paths);
await using var session = await store.CreateAsync(paths.VaultPath, "password");
```

## Debug environment variables

| Variable | Value | Effect |
|---|---|---|
| `ROUTERPLUS_RUN_E2E` | `1` | Enable E2E tests |
| `ROUTERPLUS_DEBUG_AUTOLOGIN` | `1` | Enable auto-login debug flow |

## Known slow tests

- `AgnesKeyFlowOrchestratorTests` — 28 tests, ~2 minutes total (orchestrator retries)
- `RouterPlus.App.Tests` — 587 tests, ~80 seconds (WPF ViewModel init overhead)

Run the full Infrastructure suite with a 5-minute timeout in CI (`--blame-hang --blame-hang-timeout 5m`).
