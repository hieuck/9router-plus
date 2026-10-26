# RouterPlus.App.E2E — Interactive E2E Tests

## Default Behavior: **Excluded**

These tests launch the real 9Router application and control the **mouse, keyboard, and focus**. They are **excluded by default** via the project-level filter `Category!=E2E` in `Directory.Build.props`.

```
dotnet test                    # → 0 tests run from this project (filtered)
dotnet test .../RouterPlus.App.E2E.csproj  # → 0 tests run (filtered)
```

## Opt-In: Run Interactive E2E Tests

**Both** are required:
1. Set `$env:ROUTERPLUS_RUN_E2E=1` (enables `[E2EFact]`/`[E2ETheory]` attributes)
2. Pass `--filter Category=E2E` (overrides the default exclude filter)

```powershell
$env:ROUTERPLUS_RUN_E2E = "1"
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Release --filter Category=E2E
```

```cmd
set ROUTERPLUS_RUN_E2E=1
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Release --filter Category=E2E
```

## Live Chrome / Real Profile Tests (Additional Gate)

Some tests interact with **real Chrome profiles and saved credentials**. They require **both** environment variables above, **plus**:

```powershell
$env:ROUTERPLUS_RUN_E2E = "1"
$env:ROUTERPLUS_LIVE_E2E = "1"
$env:ROUTERPLUS_LIVE_PROFILE = "Default"   # or your Chrome profile name
dotnet test tests/RouterPlus.App.E2E/RouterPlus.App.E2E.csproj -c Release --filter Category=E2E
```

These tests are in:
- `RealChromeHealthCheckTests.cs`
- `RealProfileHealthCheckTests.cs`

## Summary

| Test Type | `ROUTERPLUS_RUN_E2E=1` | `--filter Category=E2E` | `ROUTERPLUS_LIVE_E2E=1` |
|-----------|------------------------|-------------------------|-------------------------|
| Synthetic E2E (AppLifecycle, ProfileManagement, etc.) | ✅ Required | ✅ Required | ❌ Not needed |
| Real Chrome / Real Profile | ✅ Required | ✅ Required | ✅ Required |

All test methods use `[E2EFact]` (or `[E2ETheory]`) which automatically skips when `ROUTERPLUS_RUN_E2E` is not `1`.

**Note:** The default filter `Category!=E2E` is **unconditional** — even a leftover `ROUTERPLUS_RUN_E2E=1` will not make a plain `dotnet test` run E2E tests. You must explicitly pass `--filter Category=E2E`.