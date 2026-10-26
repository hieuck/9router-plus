# RouterPlus.App.E2E — Interactive E2E Tests

## Default Behavior: **Skipped**

These tests launch the real 9Router application and control the **mouse, keyboard, and focus**. They do **not** run by default.

```
dotnet test                    # → 0 tests run (all skipped)
dotnet test .../RouterPlus.App.E2E.csproj  # → all tests skipped
```

## Opt-In: Run Interactive E2E Tests

```powershell
$env:ROUTERPLUS_RUN_E2E = "1"
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Release
```

```cmd
set ROUTERPLUS_RUN_E2E=1
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Release
```

## Live Chrome / Real Profile Tests (Additional Gate)

Some tests interact with **real Chrome profiles and saved credentials**. They require **both** environment variables:

```powershell
$env:ROUTERPLUS_RUN_E2E = "1"
$env:ROUTERPLUS_LIVE_E2E = "1"
$env:ROUTERPLUS_LIVE_PROFILE = "Default"   # or your Chrome profile name
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Release
```

These tests are in:
- `RealChromeHealthCheckTests.cs`
- `RealProfileHealthCheckTests.cs`

## Summary

| Test Type | `ROUTERPLUS_RUN_E2E=1` | `ROUTERPLUS_LIVE_E2E=1` |
|-----------|------------------------|-------------------------|
| Synthetic E2E (AppLifecycle, ProfileManagement, etc.) | ✅ Required | ❌ Not needed |
| Real Chrome / Real Profile | ✅ Required | ✅ Required |

All test methods use `[E2EFact]` (or `[E2ETheory]`) which automatically skips when `ROUTERPLUS_RUN_E2E` is not `1`.