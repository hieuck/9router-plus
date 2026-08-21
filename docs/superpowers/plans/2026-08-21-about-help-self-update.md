# About, Help và Secure Self-Update Implementation Plan

> For agentic workers: REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

Goal: Add standard About/Help entry points and a fail-closed Windows self-update flow that verifies release metadata, checksum, signature, staging and rollback before replacing the app.

Architecture: Keep UI orchestration in RouterPlus.App, deterministic version/metadata/path rules in RouterPlus.Core, network and package verification in RouterPlus.Infrastructure, and process replacement in a separate RouterPlus.Updater helper. The app never updates itself in-process and never sends profile or secret data to GitHub.

Tech Stack: .NET 8, WPF, existing AsyncRelayCommand pattern, HttpClient, System.IO.Compression, SemVer-compatible version value objects, Windows Authenticode verification, GitHub Releases REST API, PowerShell GitHub Actions.

---

## File map

Create:

- src/RouterPlus.Core/Updates/ReleaseVersion.cs — strict stable/prerelease version parsing and comparison.
- src/RouterPlus.Core/Updates/ReleaseAsset.cs — immutable release asset metadata.
- src/RouterPlus.Core/Updates/ReleaseManifest.cs — signed update manifest model.
- src/RouterPlus.Core/Updates/UpdateState.cs — update lifecycle states and user-safe messages.
- src/RouterPlus.Infrastructure/Updates/GitHubReleaseClient.cs — GitHub latest-release metadata client with host and schema validation.
- src/RouterPlus.Infrastructure/Updates/UpdatePackageVerifier.cs — checksum, signature, archive-entry and package-layout validation.
- src/RouterPlus.Infrastructure/Updates/UpdatePaths.cs — canonical staging, backup and state paths under LocalAppData.
- src/RouterPlus.Updater/RouterPlus.Updater.csproj — Windows helper executable project.
- src/RouterPlus.Updater/Program.cs — updater process entry point and argument validation.
- src/RouterPlus.Updater/UpdateTransaction.cs — wait, backup, swap, health-check and rollback transaction.
- src/RouterPlus.App/ViewModels/AboutViewModel.cs — presentation-only About data.
- src/RouterPlus.App/AboutWindow.xaml — About dialog layout.
- src/RouterPlus.App/AboutWindow.xaml.cs — dialog close and safe external-link handlers.
- tests/RouterPlus.Core.Tests/ReleaseVersionTests.cs — version parsing/comparison tests.
- tests/RouterPlus.Core.Tests/UpdatePackageValidationTests.cs — checksum, URL, archive and path tests.
- tests/RouterPlus.Core.Tests/GitHubReleaseClientTests.cs — fake HTTP release responses.
- tests/RouterPlus.Core.Tests/MainViewModelUpdateTests.cs — command state and privacy behavior.
- tests/RouterPlus.Updater.Tests/RouterPlus.Updater.Tests.csproj — isolated updater transaction test project.
- tests/RouterPlus.Updater.Tests/UpdateTransactionTests.cs — swap and rollback tests using temporary directories.

Modify:

- RouterPlus.sln — include RouterPlus.Updater and updater tests.
- src/RouterPlus.App/MainWindow.xaml — add Help menu/button beside Sync and Settings.
- src/RouterPlus.App/MainWindow.xaml.cs — create/show AboutWindow without exposing profile data.
- src/RouterPlus.App/ViewModels/MainViewModel.cs — expose Help/About/update commands and coordinate startup checks.
- src/RouterPlus.App/RouterPlus.App.csproj — reference update infrastructure and updater payload metadata.
- src/RouterPlus.Infrastructure/RouterPlus.Infrastructure.csproj — add only required framework references; no unreviewed third-party updater package.
- .github/workflows/release.yml — publish helper, generate manifest/checksum and sign release executables.
- scripts/package-release.ps1 — include updater helper and signed update metadata in the release layout.
- scripts/release-preflight.ps1 — require manifest/signature assets when self-update is enabled.
- README.md — document About/Help/update behavior and signing requirement.
- docs/user-guide.md — add update and rollback instructions.
- docs/troubleshooting.md — add update failure recovery instructions.
- docs/release-checklist.md — add signing, manifest, rollback and clean-machine checks.
- SECURITY.md — explain signed update verification and how to report updater vulnerabilities.

Do not modify profile storage, DPAPI secret formats, provider API contracts or the personal-data cleanup already completed in artifacts.

---

### Task 1: Define update domain types and version policy

Files:
- Create: src/RouterPlus.Core/Updates/ReleaseVersion.cs
- Create: src/RouterPlus.Core/Updates/ReleaseAsset.cs
- Create: src/RouterPlus.Core/Updates/ReleaseManifest.cs
- Create: src/RouterPlus.Core/Updates/UpdateState.cs
- Test: tests/RouterPlus.Core.Tests/ReleaseVersionTests.cs

- [ ] Step 1: Write failing tests for stable and prerelease ordering.

Test cases must cover:

~~~csharp
[Theory]
[InlineData("1.2.3", "1.2.4", -1)]
[InlineData("1.2.3", "1.2.3", 0)]
[InlineData("1.2.3", "1.2.3-rc.1", 1)]
[InlineData("1.2.3-rc.1", "1.2.3-rc.2", -1)]
public void Compare_uses_semver_order(string current, string candidate, int expectedSign)
{
    var result = ReleaseVersion.Parse(current).CompareTo(ReleaseVersion.Parse(candidate));

    Assert.Equal(expectedSign, Math.Sign(result));
}

[Theory]
[InlineData("v1.2.3")]
[InlineData("1.2")]
[InlineData("1.2.3+build")]
[InlineData("1.2.3-rc 1")]
public void Parse_rejects_non_release_versions(string value)
{
    Assert.Throws<FormatException>(() => ReleaseVersion.Parse(value));
}
~~~

- [ ] Step 2: Run the focused test and confirm it fails because the types do not exist.

Run: .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~ReleaseVersionTests

Expected: compile failure naming the missing ReleaseVersion type.

- [ ] Step 3: Implement the minimal immutable domain types.

ReleaseVersion must store major/minor/patch, optional prerelease identifiers and expose IsPrerelease, Parse and CompareTo. Reject leading v, missing patch, whitespace, build metadata and malformed identifiers. ReleaseAsset must contain Name, DownloadUri, Length, Sha256 and IsRequired. ReleaseManifest must contain Version, Channel, AssetName, Sha256, Publisher and Signature. UpdateState must distinguish Idle, Checking, Available, Downloading, Verifying, ReadyToInstall, Installing, Completed, Failed and Disabled.

- [ ] Step 4: Run ReleaseVersionTests and the existing core suite.

Expected: focused tests pass and the existing suite has zero failures.

---

### Task 2: Implement release metadata fetching with privacy and host boundaries

Files:
- Create: src/RouterPlus.Infrastructure/Updates/GitHubReleaseClient.cs
- Create: tests/RouterPlus.Core.Tests/GitHubReleaseClientTests.cs
- Modify: src/RouterPlus.Infrastructure/RouterPlus.Infrastructure.csproj only if a framework reference is required.

- [ ] Step 1: Write failing fake-HTTP tests.

Cover latest stable release, no newer release, malformed JSON, prerelease filtering, missing required asset, non-GitHub URL, redirect to an unapproved host and response containing profile-like fields. The client must not add a GitHub token and must not serialize application settings.

- [ ] Step 2: Run the focused tests and confirm the expected missing-client failure.

Run: .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~GitHubReleaseClientTests

Expected: compile failure naming the missing client.

- [ ] Step 3: Implement GitHubReleaseClient with injectable HttpClient.

Use a fixed repository owner/name, a fixed API endpoint, an explicit User-Agent, a timeout, and case-insensitive JSON parsing. Accept only HTTPS GitHub API/release asset hosts required by the final release flow. Reject non-200 responses, malformed metadata, prerelease candidates for stable channel, downgrades and asset names that do not match the win-x64 convention. Return a domain object, never raw response text.

- [ ] Step 4: Add a privacy assertion to the test handler.

Inspect every outgoing request method, URI and body; assert there is no API key, profile name, email, Chrome path, dashboard URL, OAuth value or machine identifier. The request may contain only standard headers and the fixed release endpoint.

- [ ] Step 5: Run the focused tests and the complete core suite.

Expected: all metadata tests pass with no warnings.

---

### Task 3: Build package verification and safe staging paths

Files:
- Create: src/RouterPlus.Infrastructure/Updates/UpdatePackageVerifier.cs
- Create: src/RouterPlus.Infrastructure/Updates/UpdatePaths.cs
- Create: tests/RouterPlus.Core.Tests/UpdatePackageValidationTests.cs

- [ ] Step 1: Write failing validation tests.

Cover matching checksum, mismatched checksum, malformed checksum file, missing signature, invalid signature, ZIP path traversal such as ..\\evil.exe, absolute archive entries, symlink-like entries, oversized archive entries and package missing RouterPlus.exe or the updater helper.

- [ ] Step 2: Run the focused tests and confirm they fail before implementation.

Run: .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~UpdatePackageValidationTests

Expected: compile failure naming the missing verifier/path types.

- [ ] Step 3: Implement canonical path handling.

UpdatePaths must resolve the installation, staging, backup and state paths with absolute canonical paths. Every path operation must verify the final path remains inside the expected root before creating, extracting, moving or deleting it. Never place update files under the secrets directory.

- [ ] Step 4: Implement checksum and archive verification.

Compute SHA-256 from the downloaded ZIP and compare in constant-time to the normalized manifest value. Extract only after checksum and signature checks. Enumerate every ZIP entry before extraction and reject traversal, rooted paths, unexpected file types and duplicate normalized paths. Require RouterPlus.exe, RouterPlus.Updater.exe and the signed manifest in the staged layout.

- [ ] Step 5: Implement Authenticode verification behind a narrow interface.

Create an injectable verifier so unit tests can use valid/invalid test doubles. The Windows implementation must verify the embedded publisher certificate against the configured publisher identity and reject unsigned files, invalid chains and unexpected publishers. Do not make an unsigned production artifact eligible for automatic installation.

- [ ] Step 6: Run focused validation tests and the complete core suite.

Expected: all validation tests pass; no test writes outside its temporary directory.

---

### Task 4: Add the updater helper and rollback transaction

Files:
- Create: src/RouterPlus.Updater/RouterPlus.Updater.csproj
- Create: src/RouterPlus.Updater/Program.cs
- Create: src/RouterPlus.Updater/UpdateTransaction.cs
- Modify: RouterPlus.sln
- Create: tests/RouterPlus.Updater.Tests/RouterPlus.Updater.Tests.csproj
- Create: tests/RouterPlus.Updater.Tests/UpdateTransactionTests.cs

- [ ] Step 1: Add the updater project and test project to the solution.

Target net8.0-windows, use an executable output for the helper and reference only the minimal infrastructure/domain projects. Keep the updater independent of profile UI and provider code.

- [ ] Step 2: Write failing transaction tests.

Test waiting for the parent PID, successful backup/staging/live swap, failed health check rollback, locked target failure, missing argument failure, duplicate updater prevention and cleanup of staging while retaining backup on failure.

- [ ] Step 3: Implement strict command-line arguments.

Accept only explicit installation root, staging root, backup root, parent PID, expected version and health-check executable path. Reject missing, relative or outside-root paths. Refuse to run if the parent process is still running beyond the configured timeout or if another updater mutex exists.

- [ ] Step 4: Implement backup, swap, health check and rollback.

Keep the existing app untouched until the parent exits. Rename live to backup, move verified staging to live, launch RouterPlus.exe, wait for a bounded health signal, and restore backup on failure. Return distinct exit codes for validation, lock, swap, health-check and rollback failures. Never delete the only known good version.

- [ ] Step 5: Run updater tests and inspect temporary directories.

Run: .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Updater.Tests\\RouterPlus.Updater.Tests.csproj --configuration Release --no-restore

Expected: all transaction tests pass and temporary roots are empty after each test.

---

### Task 5: Add About, Help and update commands to the WPF app

Files:
- Create: src/RouterPlus.App/ViewModels/AboutViewModel.cs
- Create: src/RouterPlus.App/AboutWindow.xaml
- Create: src/RouterPlus.App/AboutWindow.xaml.cs
- Create: tests/RouterPlus.Core.Tests/MainViewModelUpdateTests.cs
- Modify: src/RouterPlus.App/MainWindow.xaml
- Modify: src/RouterPlus.App/MainWindow.xaml.cs
- Modify: src/RouterPlus.App/ViewModels/MainViewModel.cs
- Modify: src/RouterPlus.App/RouterPlus.App.csproj

- [ ] Step 1: Write failing ViewModel tests for command privacy and state.

Cover About data containing only app metadata, Help opening fixed public links, update check transitions for no-update/available/failed, disabled state when signature support is unavailable, and refusal to install while a provider workflow is running.

- [ ] Step 2: Run the focused tests and confirm the missing command/service failure.

Run: .\\.dotnet\\dotnet.exe test tests\\RouterPlus.Core.Tests\\RouterPlus.Core.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~MainViewModelUpdateTests

Expected: compile failure naming the missing commands or update service.

- [ ] Step 3: Add AboutViewModel and AboutWindow.

Read version and repository metadata only from assembly/application constants. Use safe fixed links. Render the existing app icon and license name. The dialog must not bind to MainViewModel profile collections, logs, settings or secret state.

- [ ] Step 4: Add a compact Help menu beside Sync and Settings.

Bind menu actions to commands. Use the existing external URL launcher pattern, but allow only fixed HTTPS URLs. Add keyboard-accessible labels and tooltips. Keep menu visible in light and dark themes and at the existing minimum window size.

- [ ] Step 5: Add update lifecycle properties and commands.

Expose IsUpdateChecking, IsUpdateAvailable, AvailableVersion, UpdateStatusText, CheckForUpdatesCommand, InstallUpdateCommand and OpenReleasePageCommand. On startup, schedule a cooldown-based check after InitializeAsync completes; do not block profile loading. Disable install during active workflows, settings writes or when signature verification is unavailable.

- [ ] Step 6: Connect download/stage/invoke-helper flow.

Download to the UpdatePaths staging root, report progress without logging URLs or response bodies, validate package, then launch RouterPlus.Updater.exe with canonical arguments and request app shutdown only after user confirmation. Preserve settings and DPAPI secrets.

- [ ] Step 7: Run focused tests and manually inspect XAML bindings.

Expected: all update command tests pass; no binding references a missing property/command; About and Help contain no personal-data binding.

---

### Task 6: Wire release packaging, signing and preflight gates

Files:
- Modify: .github/workflows/release.yml
- Modify: scripts/package-release.ps1
- Modify: scripts/release-preflight.ps1
- Modify: RouterPlus.sln if publish project metadata requires it
- Test: release preflight and package inspection commands

- [ ] Step 1: Add updater publish output to the release workflow.

Publish RouterPlus.App and RouterPlus.Updater as self-contained win-x64 outputs into a controlled publish directory. Keep the updater helper adjacent to the app and exclude secrets, debug screenshots and local artifacts.

- [ ] Step 2: Generate manifest and checksum from the exact archive.

Create the manifest only after packaging, record version/channel/asset/length/SHA-256/publisher, and sign the manifest and executable files with the configured certificate. Upload the archive, checksum and signed manifest to the GitHub Release.

- [ ] Step 3: Add fail-closed preflight checks.

Require public repository, LICENSE, signed RouterPlus.exe, signed RouterPlus.Updater.exe, manifest, checksum, exact asset names, no raw UI screenshots, no known secret patterns and no personal-data documentation. Make the script report the exact missing gate and exit nonzero.

- [ ] Step 4: Update release documentation.

Document automatic checking, user-confirmed restart, failure recovery, signed artifact requirements, unsigned-build behavior and the fact that update requests contain no profile data. Update README, user guide, troubleshooting, SECURITY.md and release checklist together.

- [ ] Step 5: Run package inspection on a clean temporary directory.

Verify archive contents, signature status, manifest/hash agreement, relative links, absence of artifacts/debug screenshots and absence of secrets/PII. Do not inspect or include personal debug images from the ignored artifacts directory.

---

### Task 7: End-to-end verification and release sign-off

Files:
- Modify: docs/release-checklist.md only for final evidence if required.
- Test: complete solution and clean Windows install.

- [ ] Step 1: Run focused tests for domain, metadata, package, updater and ViewModel behavior.

Expected: zero failures and no warnings introduced by the feature.

- [ ] Step 2: Run the complete Release test suite and build.

Run:

~~~powershell
& .\\.dotnet\\dotnet.exe test RouterPlus.sln --configuration Release --no-restore --disable-build-servers
& .\\.dotnet\\dotnet.exe build RouterPlus.sln --configuration Release --no-restore --disable-build-servers
~~~

Expected: all tests pass, build has zero warnings and zero errors.

- [ ] Step 3: Run vulnerability and release preflight checks.

Run:

~~~powershell
& .\\.dotnet\\dotnet.exe list RouterPlus.sln package --vulnerable --include-transitive
& .\\scripts\\release-preflight.ps1 -RequireLicense -RequirePublicRepository
~~~

Expected: no known vulnerable packages and every public-release gate passes. If code signing or security channel is absent, the release must remain blocked.

- [ ] Step 4: Test from a clean Windows user profile.

Install the release ZIP, open About/Help, run a no-update check, stage a signed update, confirm restart, verify settings/secrets remain, and force a failed health check to verify rollback. Capture only sanitized evidence.

- [ ] Step 5: Perform final privacy audit.

Check source, Git history, docs, release archive, manifest, logs and screenshots for email, Chrome paths, profile names, API keys, OAuth values, cookies and machine identifiers. Delete any generated personal screenshot before sign-off.

- [ ] Step 6: Stop if any gate is incomplete.

Do not mark the release ready and do not change repository visibility until signing, private security reporting, clean-machine smoke tests, license ownership and all package/privacy checks are confirmed.

---

## Self-review checklist

- Spec coverage: About, Help, self-update, checksum, signature, staging, rollback, privacy, release workflow, tests and manual acceptance each have an explicit task.
- Scope: UI, update service and helper are separated by project/file responsibility; no provider or secret-storage refactor is included.
- Security: unsigned artifacts fail closed; URLs/paths are bounded; profile and secret data are excluded from requests/logs.
- Verification: every implementation task has a focused failing test before production code and a complete-suite checkpoint after.
- Repository safety: no commit, push, branch creation or repository-visibility change is part of this plan unless the user explicitly requests it.
