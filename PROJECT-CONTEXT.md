# RouterPlus Project Context

**Project name and purpose**: RouterPlus (9Router Profile Tool) is a Windows desktop
companion for 9Router. It detects Chrome and the Chrome User Data directory, lists
Chrome profiles from `Local State`, opens the 9Router dashboard with the chosen
profile, and adds provider connections (Codex/Kiro/Kimchi via OAuth/device-code,
OpenRouter/Ollama via API key) named after the profile.

**Tech stack**:
- C# (.NET 8.0, SDK pinned in `global.json`) for all logic
- WPF (Windows Presentation Foundation) for the desktop UI (`RouterPlus.App`)
- Class libraries: `RouterPlus.Core` (models, provider catalog, priority),
  `RouterPlus.Infrastructure` (Chrome launcher, 9Router API client, settings, DPAPI vault),
  `RouterPlus.Updater` (self-update)
- Tests: xUnit + Moq across 5 test projects under `tests/`
- Build: `scripts/build.ps1` (restore, test, build, publish) with local SDK bootstrap
  via `scripts/bootstrap-dotnet.ps1`; CI in `.github/workflows/ci.yml`

**Current phase**: Stabilization after the test-suite refactor (shared `TestHelpers`,
domain-folder layout, behavior-style assertions). Recent work: coverage collection in
CI and shared helpers for `RouterPlus.App.Tests`.

**Key constraints**:
- Windows-only desktop app (`net8.0-windows`), self-contained `win-x64` artifact
- Secrets encrypted with Windows DPAPI `CurrentUser`; API keys never logged
- `TreatWarningsAsErrors` is on; `Nullable` + `ImplicitUsings` enabled
- Unsigned builds; stable releases via `vX.Y.Z` tags with ZIP + `.sha256`, self-update
  only installs checksum-verified stable packages
- Tool never handles Google passwords, CAPTCHAs, or third-party ToS acceptance

**What "done" looks like**: `dotnet build RouterPlus.sln` green with 0 warnings,
all 5 test projects green, publish artifact runs on Windows x64 without a .NET
runtime install, and docs (`README.md`, `docs/user-guide.md`) match the shipped UI.
