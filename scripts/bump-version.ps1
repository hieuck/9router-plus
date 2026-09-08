#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bump patch version in CHANGELOG.md, commit, tag, and push.

.DESCRIPTION
    Reads the current version from CHANGELOG.md (expects a line like "## [vX.Y.Z] - YYYY-MM-DD"),
    increments the patch number, updates CHANGELOG.md, commits the change, creates an annotated
    Git tag, and pushes both commit and tag to origin.

.NOTES
    This script should be run only after a successful CI run and when the harness audit score >= 70.
#>

param(
    [switch]$DryRun
)

$changelogPath = Join-Path $PSScriptRoot "..\CHANGELOG.md"
if (-not (Test-Path $changelogPath)) {
    Write-Error "CHANGELOG.md not found at $changelogPath"
    exit 1
}

$content = Get-Content $changelogPath -Raw

# Find the first version header like "## [v0.2.0] - 2026-08-29"
$pattern = '^##\s*\[v(\d+)\.(\d+)\.(\d+)\]'
if ($content -match $pattern) {
    $major = [int]$matches[1]
    $minor = [int]$matches[2]
    $patch = [int]$matches[3] + 1
    $newVersion = "v$major.$minor.$patch"
    $newHeader = "## [$newVersion] - $(Get-Date -Format 'yyyy-MM-dd')"

    Write-Host "Current version: v$major.$minor.$($patch-1)"
    Write-Host "New version:     $newVersion"

    if (-not $DryRun) {
        # Replace the first occurrence of the version header
        $lines = $content -split "`r?`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $pattern) {
                $lines[$i] = $newHeader
                break
            }
        }
        $newContent = $lines -join "`n"

        # Write back
        Set-Content -Path $changelogPath -Value $newContent -Encoding UTF8
        Write-Host "Updated CHANGELOG.md with $newHeader"

        # Git operations
        & git add $changelogPath
        & git commit -m "chore: bump version to $newVersion"
        & git tag -a "$newVersion" -m "Release $newVersion"
        & git push origin HEAD
        & git push origin "$newVersion"
        Write-Host "Pushed commit and tag $newVersion"
    } else {
        Write-Host "[DryRun] Would update CHANGELOG.md and push tag $newVersion"
    }
} else {
    Write-Error "Could not find version header in CHANGELOG.md"
    exit 1
}