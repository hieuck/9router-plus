/*
Brief statement for GATEGUARD:
- Importers/Callers: This script will be invoked directly by developers via PowerShell and may be called from the existing wrapper scripts (`run-debug-loop.ps1`, `run-live-google-e2e.ps1`). No runtime API imports.
- Affected API/Data schemas: None. The script only orchestrates dotnet CLI commands and filesystem cleanup; it does not modify application data structures.
- User instruction (verbatim): "Implement a developer harness"
*/

<#
.SYNOPSIS
    Unified developer harness script for building the RouterPlus app and running E2E tests.

.DESCRIPTION
    Provides a single entry point for developers to build the application, run the E2E test suite (optionally filtered), and manage temporary artifacts.

.PARAMETER Configuration
    Build configuration: Debug or Release. Default is Debug.

.PARAMETER Filter
    Optional XUnit test filter string (e.g., "FullyQualifiedName~ProfileContextMenuTests"). When omitted the full E2E suite runs.

.PARAMETER KeepArtifacts
    Switch to preserve temporary harness artifacts after execution; otherwise they are cleaned up.
#>
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [string]$Filter,
    [switch]$KeepArtifacts
)

$ErrorActionPreference = 'Stop'

# Resolve repository root (script resides in tools/dev-harness)
$RepoRoot = $PSScriptRoot
while ($RepoRoot -and -not (Test-Path (Join-Path $RepoRoot 'RouterPlus.sln'))) {
    $RepoRoot = Split-Path $RepoRoot -Parent
}
if (-not $RepoRoot) {
    Write-Error "Could not locate repository root containing RouterPlus.sln"
    exit 1
}

Write-Host "Repository root: $RepoRoot" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
if ($Filter) { Write-Host "Test filter: $Filter" -ForegroundColor Cyan }
Write-Host ""

$AppProject = Join-Path $RepoRoot 'src\RouterPlus.App\RouterPlus.App.csproj'
$E2EProject = Join-Path $RepoRoot 'tests\RouterPlus.App.E2E\RouterPlus.App.E2E.csproj'

# Build the main application
Write-Host "Building RouterPlus.App ($Configuration)..." -ForegroundColor Yellow
& dotnet build $AppProject --configuration $Configuration --verbosity quiet
if ($LASTEXITCODE -ne 0) { Write-Error "App build failed"; exit $LASTEXITCODE }

# Run the E2E tests (skip rebuild since we already built above)
Write-Host "Running RouterPlus.App.E2E tests ($Configuration)..." -ForegroundColor Yellow
$testCmd = "dotnet test $E2EProject --configuration $Configuration --no-build"
if ($Filter) { $testCmd += " --filter `"$Filter`"" }
& $testCmd
$TestExitCode = $LASTEXITCODE

# Cleanup temporary harness artifacts unless KeepArtifacts is set
if (-not $KeepArtifacts) {
    $tempDir = Join-Path $env:TEMP 'RouterPlusHarness'
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

exit $TestExitCode
