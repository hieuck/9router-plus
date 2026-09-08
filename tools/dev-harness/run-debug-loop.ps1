<#
Brief statement for GATEGUARD:
- Importers/Callers: This script is invoked directly by developers and may be called from other wrapper scripts (e.g., run-live-google-e2e.ps1). It does not import any runtime APIs.
- Affected API/Data schemas: None – the script only orchestrates `dotnet` CLI commands and filesystem cleanup.
- User instruction (verbatim): "Implement a developer harness"
#>

# Updated to delegate to the unified dev-harness script

param(
    [string]$Configuration = "Debug",
    [string]$Filter = "FullyQualifiedName~ProfileContextMenuTests",
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

# Call the unified dev-harness script with the appropriate arguments
$devHarnessPath = Join-Path $RepoRoot "tools\dev-harness\dev-harness.ps1"
$invokeCmd = "& $devHarnessPath -Configuration $Configuration -Filter $Filter"
if ($KeepArtifacts) { $invokeCmd += " -KeepArtifacts" }
Invoke-Expression $invokeCmd
$exitCode = $LASTEXITCODE

exit $exitCode
