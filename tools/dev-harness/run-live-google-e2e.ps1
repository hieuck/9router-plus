/*
Brief statement for GATEGUARD:
- Importers/Callers: Directly invoked by developers for live Google E2E debugging; may be called from other wrapper scripts.
- Affected API/Data schemas: None – the script only sets environment variables and delegates to the unified dev-harness script.
- User instruction (verbatim): "Implement a developer harness"
*/

<#
.SYNOPSIS
    Run live E2E tests for Google Auto Login using real Chrome profile and saved credentials.

.DESCRIPTION
    This script sets up environment variables and delegates test execution to the unified dev-harness script.
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Profile,

    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Debug",

    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

# Resolve repository root (same logic as dev-harness)
$RepoRoot = $PSScriptRoot
while ($RepoRoot -and -not (Test-Path (Join-Path $RepoRoot "RouterPlus.sln"))) {
    $RepoRoot = Split-Path $RepoRoot -Parent
}
if (-not $RepoRoot) {
    Write-Error "Could not locate repository root"
    exit 1
}

Write-Host "Repository root: $RepoRoot" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Profile: $Profile" -ForegroundColor Cyan
Write-Host ""

# Set environment variables for live tests
$env:ROUTERPLUS_LIVE_E2E = "1"
$env:ROUTERPLUS_LIVE_PROFILE = $Profile
if ($KeepArtifacts) { $env:ROUTERPLUS_HARNESS_KEEP_ARTIFACTS = "1" }

# Invoke dev-harness with live‑Google test filter
$devHarnessPath = Join-Path $RepoRoot "tools\dev-harness\dev-harness.ps1"
$filter = "FullyQualifiedName~LiveGoogleAutoLoginTests"
$invokeCmd = "& $devHarnessPath -Configuration $Configuration -Filter $filter"
if ($KeepArtifacts) { $invokeCmd += " -KeepArtifacts" }
Invoke-Expression $invokeCmd
$exitCode = $LASTEXITCODE

# Clear environment variables
$env:ROUTERPLUS_LIVE_E2E = ""
$env:ROUTERPLUS_LIVE_PROFILE = ""
$env:ROUTERPLUS_HARNESS_KEEP_ARTIFACTS = ""

exit $exitCode