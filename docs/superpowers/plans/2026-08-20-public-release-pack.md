# Public Release Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the RouterPlus zip release into a safe, user-facing release pack with sanitized visuals, complete usage guides, privacy/security information, and repeatable support/release templates.

**Architecture:** Keep the application and existing CI/release workflows unchanged. Remove personal screenshots, render one safe screenshot from the existing HTML mockup using demo data, and organize user-facing documentation under `docs/`. Add GitHub support templates and link everything from the README.

**Tech Stack:** Markdown, GitHub issue/PR templates, existing HTML mockup, Playwright screenshot, PowerShell validation, existing .NET test/build/publish commands.

---

## File Map

- Delete from workspace: `ui-collapsed-check.png`, `ui-collapsed-current.png`, `ui-dark-check.png`, `ui-dark-settings-final.png`, `ui-light-settings-check.png`.
- Create: `docs/assets/9router-profile-workspace.png` — sanitized demo screenshot only.
- Create: `docs/user-guide.md` — end-user setup and provider workflows.
- Create: `docs/privacy.md` — local data, secret storage, and network behavior.
- Create: `docs/troubleshooting.md` — actionable problem-solving flows.
- Create: `CHANGELOG.md` — release history format starting with `Unreleased`.
- Create: `SECURITY.md` — safe issue reporting and vulnerability guidance.
- Create: `docs/release-checklist.md` — pre-tag, release, and post-release checks.
- Create: `.github/ISSUE_TEMPLATE/bug_report.md` — sanitized bug report form.
- Create: `.github/ISSUE_TEMPLATE/feature_request.md` — feature request form.
- Create: `.github/PULL_REQUEST_TEMPLATE.md` — test/docs/security checklist.
- Modify: `README.md` — user-first download, quick-start, docs links, and safe screenshot.
- Do not modify: application source, tests, or existing CI/release workflow behavior.

### Task 1: Remove personal screenshots and create a safe demo asset

**Files:**
- Delete: `ui-collapsed-check.png`
- Delete: `ui-collapsed-current.png`
- Delete: `ui-dark-check.png`
- Delete: `ui-dark-settings-final.png`
- Delete: `ui-light-settings-check.png`
- Create: `docs/assets/9router-profile-workspace.png`
- Read-only source: `docs/mockups/9router-profile-tool-ui.html`

- [ ] **Step 1: Verify every raw screenshot path is inside the workspace**

Run:

```powershell
$workspace = (Resolve-Path '.').Path
$rawImages = @(
  'ui-collapsed-check.png',
  'ui-collapsed-current.png',
  'ui-dark-check.png',
  'ui-dark-settings-final.png',
  'ui-light-settings-check.png'
) | ForEach-Object { (Resolve-Path $_).Path }
$rawImages | ForEach-Object {
  if (-not $_.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Raw image is outside the workspace: $_"
  }
}
```

Expected: all five paths resolve under `E:\GitHub\9router-plus`.

- [ ] **Step 2: Render a sanitized screenshot from the mockup copy**

Copy the mockup to a temporary file, replace personal-looking demo labels with neutral data, and render it at `1440x900` using Playwright. Do not edit the original mockup. The rendered page must use values such as `Demo Work Profile`, `demo.user@example.com`, `C:\Program Files\Google\Chrome\Application\chrome.exe`, `C:\Users\demo\AppData\Local\Google\Chrome\User Data`, and masked API keys.

Save the final screenshot as:

```text
docs/assets/9router-profile-workspace.png
```

- [ ] **Step 3: Inspect the generated image visually**

Open `docs/assets/9router-profile-workspace.png` and confirm it contains no real email, local drive letter, username, access token, API key, or OAuth code. The screenshot should show the app layout and workflow controls, not personal data.

- [ ] **Step 4: Delete the raw screenshots with literal paths**

After the sanitized image passes visual inspection, delete only the five named files with `Remove-Item -LiteralPath`. Do not recursively delete any directory.

- [ ] **Step 5: Verify raw screenshot cleanup**

Run:

```powershell
$remaining = Get-ChildItem -LiteralPath . -Filter 'ui-*.png' -File -Force -ErrorAction SilentlyContinue
if ($remaining) { $remaining | Select-Object FullName; throw 'Raw UI screenshots still exist.' }
```

Expected: no output and exit code `0`.

### Task 2: Write the user guide

**Files:**
- Create: `docs/user-guide.md`

- [ ] **Step 1: Add requirements and install flow**

Document Windows x64, Chrome/Chromium, a running 9Router dashboard, the self-contained zip behavior, extracting the archive, and launching `RouterPlus.exe`. Link to the latest GitHub Release through the README rather than hard-coding a version.

- [ ] **Step 2: Add first-run setup flow**

Document the exact sequence: open `⚙ Cài đặt`, choose `chrome.exe`, choose Chrome User Data, set dashboard URL if needed, press `Lưu cài đặt`, select a profile, and confirm the profile card is populated.

- [ ] **Step 3: Add profile and dashboard flows**

Document search, adding a new managed profile, double-clicking to open the dashboard with the selected Chrome profile, refreshing/syncing, and the context-menu actions. State that profile deletion requires confirmation and only the selected profile directory is removed.

- [ ] **Step 4: Add provider flows**

Document separate steps for Codex OAuth, Kiro device code, Kimchi OAuth, OpenRouter API key, and Ollama API key. Explain that API keys remain masked, are associated with the selected profile, are stored with Windows DPAPI, and must never be pasted into issues or screenshots.

- [ ] **Step 5: Add settings, backup, and uninstall flows**

Document theme/font settings, the local files `%LOCALAPPDATA%\9RouterPlus\settings.json` and `%LOCALAPPDATA%\9RouterPlus\secrets.json`, how to preserve them before moving to another Windows account, and how to remove them when the user wants a full local-data wipe.

- [ ] **Step 6: Add the safe screenshot and links**

Embed `docs/assets/9router-profile-workspace.png` with alt text that identifies it as a demo screenshot and link to privacy/troubleshooting/security documents.

### Task 3: Add privacy and troubleshooting documentation

**Files:**
- Create: `docs/privacy.md`
- Create: `docs/troubleshooting.md`

- [ ] **Step 1: Document local storage and DPAPI**

In `docs/privacy.md`, state the exact LocalAppData paths, the difference between settings and secrets, `DataProtectionScope.CurrentUser`, the consequence of changing Windows user accounts, and that API keys are not written to logs or status text.

- [ ] **Step 2: Document network and browser behavior**

Explain that the app reads Chrome `Local State`/profile metadata locally, calls the configured local 9Router dashboard, opens provider/OAuth pages through the selected Chrome executable, and does not automate passwords, CAPTCHA, or third-party consent.

- [ ] **Step 3: Add troubleshooting decision trees**

Cover Chrome detection, incorrect User Data directory, missing profiles, 9Router unavailable, OAuth/device-code timeout, invalid API key, DPAPI access after account changes, and connection/profile name mismatches. Each issue must include a symptom, checks, and a safe next action.

### Task 4: Add release and support metadata

**Files:**
- Create: `CHANGELOG.md`
- Create: `SECURITY.md`
- Create: `docs/release-checklist.md`
- Create: `.github/ISSUE_TEMPLATE/bug_report.md`
- Create: `.github/ISSUE_TEMPLATE/feature_request.md`
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

- [ ] **Step 1: Create the changelog**

Start `CHANGELOG.md` with an `Unreleased` section and categories for Added, Changed, Fixed, Security, and Known limitations. Include a release-entry example without inventing a version number.

- [ ] **Step 2: Create the security policy**

Tell users not to include API keys, OAuth codes, cookies, emails, Chrome paths, or screenshots with personal data in public issues. State that maintainers must enable GitHub Security Advisories or publish a real private security contact before public distribution; until then, do not promise a private reporting channel and do not invent an email address.

- [ ] **Step 3: Create the release checklist**

Include: remove raw screenshots, inspect sanitized assets, run restore/test/build/publish, verify `RouterPlus.exe`, verify zip/checksum, inspect generated release notes, test extraction on a clean Windows account, confirm SmartScreen/code-signing status, and verify the published assets.

- [ ] **Step 4: Create sanitized GitHub templates**

Bug reports must request app version/tag, Windows version, reproduction steps, expected/actual behavior, and sanitized logs. Feature requests must request user problem and desired outcome. Both templates must explicitly say not to paste secrets or personal screenshots. The PR template must include tests, docs, screenshot hygiene, and secret hygiene checkboxes.

### Task 5: Make README user-first

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add download and release guidance**

Add a `Tải bản phát hành` section linking to the repository’s latest Releases page and explaining that the release asset is a self-contained Windows x64 zip. Include extraction and launch instructions.

- [ ] **Step 2: Add quick start and documentation links**

Add a compact five-step quick start and links to `docs/user-guide.md`, `docs/privacy.md`, `docs/troubleshooting.md`, `SECURITY.md`, and `CHANGELOG.md`.

- [ ] **Step 3: Add the safe screenshot**

Embed only `docs/assets/9router-profile-workspace.png`; do not reference any `ui-*.png` file.

- [ ] **Step 4: Clarify release limitations**

State that the current package is unsigned and has no installer or auto-update, and that a project license must be selected before broad redistribution.

### Task 6: Validate the public release pack

**Files:**
- Verify all files from Tasks 1–5.

- [ ] **Step 1: Scan new text for personal/secrets patterns**

Run a targeted scan over new docs/templates:

```powershell
rg -n -i '(arpachy|hieuck\.browser|gmail\.com|yahoo\.com|CentBrowser|G:\\Program Files|ghp_|github_pat_|sk-[A-Za-z0-9]|-----BEGIN .*PRIVATE KEY-----)' README.md CHANGELOG.md SECURITY.md docs/user-guide.md docs/privacy.md docs/troubleshooting.md docs/release-checklist.md .github/ISSUE_TEMPLATE .github/PULL_REQUEST_TEMPLATE.md
```

Expected: no matches except intentional generic words in documentation; no real account, path, or token appears.

- [ ] **Step 2: Inspect image dimensions and visually review the asset**

Run:

```powershell
Get-Item docs/assets/9router-profile-workspace.png | Select-Object FullName,Length
```

Then open the image and confirm it contains only demo values.

- [ ] **Step 3: Run repository hygiene checks**

Run:

```powershell
git diff --check
$raw = Get-ChildItem -LiteralPath . -Filter 'ui-*.png' -File -Force -ErrorAction SilentlyContinue
if ($raw) { throw 'Raw screenshot cleanup failed.' }
```

Expected: no whitespace errors and no raw screenshots.

- [ ] **Step 4: Run the existing .NET verification**

Run:

```powershell
& .\.dotnet\dotnet.exe restore RouterPlus.sln --runtime win-x64
& .\.dotnet\dotnet.exe test RouterPlus.sln --configuration Release --no-restore
& .\.dotnet\dotnet.exe build RouterPlus.sln --configuration Release --no-restore
```

Expected: restore succeeds, all tests pass, and Release build reports zero warnings/errors.

- [ ] **Step 5: Smoke-test self-contained publish**

Publish with `--runtime win-x64 --self-contained true`, verify `RouterPlus.exe` exists, create the zip/checksum using the existing release commands, and record the exact output paths and hash in the handoff.

- [ ] **Step 6: Review final scope**

Run:

```powershell
git status --short
git diff --stat
```

Expected: only the approved release pack, documentation, sanitized asset, existing workflow/spec/plan changes, and README are present; no application source or tests changed.
