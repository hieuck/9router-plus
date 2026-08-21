# GitHub Actions CI and Release-Ready Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Windows GitHub Actions CI for RouterPlus and automatic self-contained `win-x64` GitHub Releases from SemVer tags.

**Architecture:** Keep CI and release concerns in two workflows. CI validates `master` pushes and pull requests, packages a short-lived downloadable artifact, and preserves TRX test results. Release validates the tagged commit again, packages a versioned self-contained archive, writes a SHA-256 sidecar, and creates the GitHub Release with the default `GITHUB_TOKEN`.

**Tech Stack:** GitHub Actions, `windows-latest`, .NET SDK selected by `global.json`, PowerShell 7, `dotnet test/build/publish`, GitHub CLI `gh`, GitHub Actions artifact storage.

---

## File Map

- Create: `.github/workflows/ci.yml` — read-only CI for pushes and pull requests.
- Create: `.github/workflows/release.yml` — tag-triggered package and GitHub Release creation.
- Modify: `README.md` — CI badge and tag-based release instructions.
- Reuse unchanged: `global.json`, `RouterPlus.sln`, `src/RouterPlus.App/RouterPlus.App.csproj`, and existing local build scripts.
- Do not modify: application source, tests, or project behavior.

The workflow files are configuration, so no production-code test-first cycle is needed. Validation will exercise the existing solution and the exact publish settings used by the workflows.

### Task 1: Add the CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Create the workflow triggers and permissions**

Use this complete workflow structure so CI runs only for the intended branch and cannot write repository contents:

```yaml
name: CI

on:
  push:
    branches:
      - master
  pull_request:
    branches:
      - master

permissions:
  contents: read

concurrency:
  group: ci-${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: true

jobs:
  validate:
    runs-on: windows-latest
    timeout-minutes: 20
    env:
      DOTNET_NOLOGO: true
      DOTNET_CLI_TELEMETRY_OPTOUT: true
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
```

- [ ] **Step 2: Add restore, test, and build steps**

Append these steps to the same `validate` job. The test command writes predictable TRX files under the runner temporary directory:

```yaml
      - name: Restore
        shell: pwsh
        run: dotnet restore RouterPlus.sln --runtime win-x64

      - name: Test
        shell: pwsh
        run: >-
          dotnet test RouterPlus.sln
          --configuration Release
          --no-restore
          --logger "trx;LogFileName=RouterPlus.Tests.trx"
          --results-directory "$env:RUNNER_TEMP\test-results"

      - name: Build
        shell: pwsh
        run: dotnet build RouterPlus.sln --configuration Release --no-restore
```

- [ ] **Step 3: Add self-contained CI publishing**

Publish the WPF app for `win-x64` and stamp the CI artifact with a non-release version:

```yaml
      - name: Publish
        shell: pwsh
        env:
          CI_VERSION: 0.0.0-ci.${{ github.run_number }}
          CI_COMMIT: ${{ github.sha }}
        run: |
          $publishDirectory = Join-Path $env:RUNNER_TEMP 'routerplus-ci-publish'
          if (Test-Path -LiteralPath $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
          }

          dotnet publish src/RouterPlus.App/RouterPlus.App.csproj `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --no-restore `
            --output $publishDirectory `
            -p:Version=$env:CI_VERSION `
            -p:InformationalVersion=$env:CI_COMMIT
```

- [ ] **Step 4: Zip and upload the CI package**

Create a deterministic archive name containing the commit SHA and fail if the archive is missing:

```yaml
      - name: Package
        shell: pwsh
        run: |
          $publishDirectory = Join-Path $env:RUNNER_TEMP 'routerplus-ci-publish'
          $archiveName = "RouterPlus-$env:GITHUB_SHA-win-x64.zip"
          $archivePath = Join-Path $env:RUNNER_TEMP $archiveName

          if (Test-Path -LiteralPath $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
          }

          Compress-Archive `
            -Path (Join-Path $publishDirectory '*') `
            -DestinationPath $archivePath `
            -CompressionLevel Optimal

      - name: Upload packaged app
        uses: actions/upload-artifact@v7
        with:
          name: RouterPlus-${{ github.sha }}-win-x64
          path: ${{ runner.temp }}/RouterPlus-${{ github.sha }}-win-x64.zip
          if-no-files-found: error
          retention-days: 14

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: test-results-${{ github.run_id }}
          path: ${{ runner.temp }}/test-results/*.trx
          if-no-files-found: ignore
          retention-days: 14
```

- [ ] **Step 5: Check the CI file locally**

Run:

```powershell
Get-Content -Raw .github/workflows/ci.yml
```

Expected: the file contains the `push`/`pull_request` triggers, `contents: read`, the four .NET phases, the self-contained publish, and both artifact uploads. Do not claim GitHub-side validation until a local workflow linter or GitHub run confirms it.

### Task 2: Add the tag-triggered release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create the release trigger and write permission**

Use a tag filter broad enough to let the validation step reject malformed `v` tags before packaging:

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    timeout-minutes: 30
    env:
      DOTNET_NOLOGO: true
      DOTNET_CLI_TELEMETRY_OPTOUT: true
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Validate release tag
        id: metadata
        shell: pwsh
        run: |
          $tag = $env:GITHUB_REF_NAME
          $pattern = '^v(?<version>(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?)$'

          if ($tag -notmatch $pattern) {
            throw "Release tag '$tag' is invalid. Use vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-IDENTIFIER."
          }

          "tag=$tag" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
          "version=$($matches.version)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
```

- [ ] **Step 2: Add release build validation**

Append the SDK setup and the same restore/test/build sequence used by CI:

```yaml
      - name: Setup .NET
        uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json

      - name: Restore
        shell: pwsh
        run: dotnet restore RouterPlus.sln --runtime win-x64

      - name: Test
        shell: pwsh
        run: >-
          dotnet test RouterPlus.sln
          --configuration Release
          --no-restore
          --logger "trx;LogFileName=RouterPlus.Release.Tests.trx"
          --results-directory "$env:RUNNER_TEMP\release-test-results"

      - name: Build
        shell: pwsh
        run: dotnet build RouterPlus.sln --configuration Release --no-restore
```

- [ ] **Step 3: Publish with tag-derived version metadata**

Use the validated output values so invalid or untrusted tag text never reaches MSBuild:

```yaml
      - name: Publish
        shell: pwsh
        env:
          RELEASE_TAG: ${{ steps.metadata.outputs.tag }}
          RELEASE_VERSION: ${{ steps.metadata.outputs.version }}
        run: |
          $publishDirectory = Join-Path $env:RUNNER_TEMP 'routerplus-release-publish'
          if (Test-Path -LiteralPath $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
          }

          dotnet publish src/RouterPlus.App/RouterPlus.App.csproj `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --no-restore `
            --output $publishDirectory `
            -p:Version=$env:RELEASE_VERSION `
            -p:InformationalVersion=$env:RELEASE_TAG
```

- [ ] **Step 4: Create the versioned zip and SHA-256 sidecar**

Write the checksum in the conventional `hash  filename` format so Windows and Unix checksum tools can consume it:

```yaml
      - name: Package release
        id: package
        shell: pwsh
        env:
          RELEASE_TAG: ${{ steps.metadata.outputs.tag }}
        run: |
          $publishDirectory = Join-Path $env:RUNNER_TEMP 'routerplus-release-publish'
          $archiveName = "RouterPlus-$env:RELEASE_TAG-win-x64.zip"
          $archivePath = Join-Path $env:RUNNER_TEMP $archiveName
          $checksumPath = "$archivePath.sha256"

          Compress-Archive `
            -Path (Join-Path $publishDirectory '*') `
            -DestinationPath $archivePath `
            -CompressionLevel Optimal

          $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
          "$hash  $archiveName" | Set-Content -LiteralPath $checksumPath -Encoding ascii

          "archive=$archivePath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
          "checksum=$checksumPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
```

- [ ] **Step 5: Create the GitHub Release using the runner's GitHub CLI**

Use only the default token and mark prerelease tags as prereleases:

```yaml
      - name: Create GitHub Release
        shell: pwsh
        env:
          GH_TOKEN: ${{ github.token }}
          RELEASE_TAG: ${{ steps.metadata.outputs.tag }}
          RELEASE_VERSION: ${{ steps.metadata.outputs.version }}
          ARCHIVE_PATH: ${{ steps.package.outputs.archive }}
          CHECKSUM_PATH: ${{ steps.package.outputs.checksum }}
        run: |
          $arguments = @(
            'release', 'create', $env:RELEASE_TAG,
            '--repo', $env:GITHUB_REPOSITORY,
            '--title', "RouterPlus $env:RELEASE_TAG",
            '--generate-notes',
            '--verify-tag'
          )

          if ($env:RELEASE_VERSION.Contains('-')) {
            $arguments += '--prerelease'
          }

          $arguments += @($env:ARCHIVE_PATH, $env:CHECKSUM_PATH)
          & gh @arguments
          if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
          }
```

- [ ] **Step 6: Check release workflow invariants locally**

Run:

```powershell
Get-Content -Raw .github/workflows/release.yml
```

Verify manually that `contents: write` appears only in the release workflow, that the tag regex is anchored, that both release assets are generated, and that `GH_TOKEN` is set only for the `gh release create` step.

### Task 3: Document CI and release usage

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add the CI badge below the title**

Add this badge immediately after the `# 9Router Profile Tool` heading:

```markdown
[![CI](https://github.com/hieuck/9router-plus/actions/workflows/ci.yml/badge.svg)](https://github.com/hieuck/9router-plus/actions/workflows/ci.yml)
```

- [ ] **Step 2: Add the release instructions**

Add this section after the source-build instructions:

```markdown
## CI và phát hành

- Pull request vào `master` và push lên `master` tự động chạy restore, test, build và publish.
- CI tạo artifact self-contained `win-x64` để tải từ trang Actions; artifact CI được giữ trong 14 ngày.
- Tạo release bằng tag SemVer, ví dụ:

  ```powershell
  git tag v1.0.0
  git push origin v1.0.0
  ```

- Tag `v1.0.0-rc.1` tạo prerelease; tag `v1.0.0` tạo release ổn định.
- Release tự động đính kèm `RouterPlus-v1.0.0-win-x64.zip` và file `.sha256`.
- Bản phát hành self-contained không yêu cầu cài .NET 8 Runtime.
- Executable hiện chưa được code-sign; Windows có thể hiển thị cảnh báo SmartScreen khi tải bản phát hành.
```

- [ ] **Step 3: Check documentation scope**

Confirm the README does not promise an installer, code signing, automatic updates, or a license that the repository does not provide.

### Task 4: Validate the workflows and existing solution

**Files:**
- Verify: `.github/workflows/ci.yml`
- Verify: `.github/workflows/release.yml`
- Verify: `README.md`

- [ ] **Step 1: Check whitespace and repository state**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; only the intended workflow, README, and plan/spec files are changed or untracked.

- [ ] **Step 2: Restore the solution with the repository SDK**

Run:

```powershell
& .\.dotnet\dotnet.exe restore RouterPlus.sln
```

Expected: exit code `0` and no restore errors.

- [ ] **Step 3: Run the full Release test suite**

Run:

```powershell
& .\.dotnet\dotnet.exe test RouterPlus.sln --configuration Release --no-restore
```

Expected: exit code `0` and all tests pass.

- [ ] **Step 4: Build the full Release solution**

Run:

```powershell
& .\.dotnet\dotnet.exe build RouterPlus.sln --configuration Release --no-restore
```

Expected: exit code `0` with zero warnings treated as errors.

- [ ] **Step 5: Smoke-test the self-contained publish settings**

Run:

```powershell
$publishDirectory = Join-Path $env:TEMP 'routerplus-release-validation'
if (Test-Path -LiteralPath $publishDirectory) {
  Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
& .\.dotnet\dotnet.exe publish src\RouterPlus.App\RouterPlus.App.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output $publishDirectory -p:Version=1.0.0 -p:InformationalVersion=v1.0.0
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'RouterPlus.exe'))) { throw 'RouterPlus.exe was not produced.' }
```

Expected: exit code `0` and `$publishDirectory\RouterPlus.exe` exists.

- [ ] **Step 6: Validate the archive and checksum commands**

Run:

```powershell
$archivePath = Join-Path $env:TEMP 'RouterPlus-v1.0.0-win-x64.zip'
$checksumPath = "$archivePath.sha256"
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path -Leaf $archivePath)" | Set-Content -LiteralPath $checksumPath -Encoding ascii
if (-not (Test-Path -LiteralPath $archivePath)) { throw 'Archive was not produced.' }
if (-not (Test-Path -LiteralPath $checksumPath)) { throw 'Checksum was not produced.' }
```

Expected: both files exist, and the checksum line begins with 64 hexadecimal characters followed by two spaces and the zip filename.

- [ ] **Step 7: Run a workflow syntax linter when available**

Run:

```powershell
if (Get-Command actionlint -ErrorAction SilentlyContinue) {
  actionlint .github/workflows/ci.yml
  actionlint .github/workflows/release.yml
} else {
  Write-Output 'actionlint is not installed; rely on the local content review and the first GitHub Actions run for workflow validation.'
}
```

Expected: `actionlint` exits `0` when installed; otherwise the fallback message is recorded without pretending the YAML has been GitHub-validated.

### Task 5: Final requirement review

- [ ] **Step 1: Re-read the approved spec**

Check each requirement in `docs/superpowers/specs/2026-08-20-github-actions-release-ready-design.md` against the created workflows and README. Confirm CI is read-only, release has only `contents: write`, tags are validated, artifacts are self-contained, checksums are attached, and unsigned-code-signing status is documented.

- [ ] **Step 2: Review the final diff**

Run:

```powershell
git diff -- .github/workflows/ci.yml .github/workflows/release.yml README.md
git status --short
```

Expected: no application source or test files changed, no secrets are present, and all changed paths match the approved scope.

- [ ] **Step 3: Report verification evidence**

Report the exact test/build/publish commands run and their exit status. If GitHub Actions has not yet been executed remotely, state that local validation passed but remote workflow execution remains the final confirmation.
