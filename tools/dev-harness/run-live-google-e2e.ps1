#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Run live E2E tests for Google Auto Login using real Chrome profile and saved credentials.

.DESCRIPTION
    This script runs live E2E tests that interact with:
    - Real Chrome installation and profiles
    - Saved Google login credentials in the vault
    - Actual Google accounts.google.com pages

    IMPORTANT: These tests use real credentials and should NOT run in CI.
    Run manually only when debugging Auto Login issues.

.PARAMETER Profile
    Chrome profile name to test (e.g., "Default"). Required.

.PARAMETER Configuration
    Build configuration: Debug or Release. Default: Debug

.PARAMETER KeepArtifacts
    Keep test artifacts (screenshots, logs) after test completion.

.EXAMPLE
    .\run-live-google-e2e.ps1 -Profile "Default"

.EXAMPLE
    .\run-live-google-e2e.ps1 -Profile "Default" -Configuration Release -KeepArtifacts
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Profile,

    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [Parameter(Mandatory=$false)]
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"

# Find repository root
$RepoRoot = $PSScriptRoot
while ($RepoRoot -and -not (Test-Path (Join-Path $RepoRoot "RouterPlus.sln"))) {
    $RepoRoot = Split-Path $RepoRoot -Parent
}

if (-not $RepoRoot) {
    Write-Error "Could not find repository root (RouterPlus.sln)"
    exit 1
}

Write-Host "Repository root: $RepoRoot" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Profile: $Profile" -ForegroundColor Cyan
Write-Host ""

# Build the app and E2E tests
Write-Host "Building RouterPlus.App ($Configuration)..." -ForegroundColor Yellow
& dotnet build "$RepoRoot\src\RouterPlus.App\RouterPlus.App.csproj" `
    --configuration $Configuration `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Error "App build failed"
    exit $LASTEXITCODE
}

Write-Host "Building RouterPlus.App.E2E ($Configuration)..." -ForegroundColor Yellow
& dotnet build "$RepoRoot\tests\RouterPlus.App.E2E\RouterPlus.App.E2E.csproj" `
    --configuration $Configuration `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Error "E2E build failed"
    exit $LASTEXITCODE
}

Write-Host "Build completed" -ForegroundColor Green
Write-Host ""

# Set environment variables for live tests
$env:ROUTERPLUS_LIVE_E2E = "1"
$env:ROUTERPLUS_LIVE_PROFILE = $Profile

if ($KeepArtifacts) {
    $env:ROUTERPLUS_HARNESS_KEEP_ARTIFACTS = "1"
}

Write-Host "Running live Google Auto Login E2E tests..." -ForegroundColor Yellow
Write-Host "IMPORTANT: These tests will:" -ForegroundColor Red
Write-Host "  - Launch the real RouterPlus app" -ForegroundColor Red
Write-Host "  - Use real Chrome profile: $Profile" -ForegroundColor Red
Write-Host "  - Use saved credentials from the vault" -ForegroundColor Red
Write-Host "  - Interact with real Google login pages" -ForegroundColor Red
Write-Host ""

# Run live E2E tests
& dotnet test "$RepoRoot\tests\RouterPlus.App.E2E\RouterPlus.App.E2E.csproj" `
    --configuration $Configuration `
    --filter "FullyQualifiedName~LiveGoogleAutoLoginTests" `
    --logger "console;verbosity=detailed" `
    --no-build

$TestExitCode = $LASTEXITCODE

# Clear environment variables
$env:ROUTERPLUS_LIVE_E2E = ""
$env:ROUTERPLUS_LIVE_PROFILE = ""
$env:ROUTERPLUS_HARNESS_KEEP_ARTIFACTS = ""

Write-Host ""
if ($TestExitCode -eq 0) {
    Write-Host "Live E2E tests completed successfully" -ForegroundColor Green
} else {
    Write-Host "Live E2E tests failed" -ForegroundColor Red
}

exit $TestExitCode
