#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Read the latest diagnostics log and extract Error/Warning entries.

.DESCRIPTION
    Reads the most recent diagnostics.log file produced by ObservabilityHub,
    extracts lines with level "Error" or "Warning", and outputs a concise summary
    that can be posted to a GitHub issue.

.NOTES
    Expected log location: $env:LOCALAPPDATA\RouterPlus\Observability\Sessions\*\events.jsonl
    Each line is a JSON object with fields: timestamp, level, category, event, message, context.
#>

$logRoot = Join-Path $env:LOCALAPPDATA "RouterPlus\Observability\Sessions"
if (-not (Test-Path $logRoot)) {
    Write-Host "No diagnostics log directory found at $logRoot"
    exit 0
}

# Find the most recent session directory
$latestSession = Get-ChildItem $logRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $latestSession) {
    Write-Host "No session directories found"
    exit 0
}

$logFile = Join-Path $latestSession.FullName "events.jsonl"
if (-not (Test-Path $logFile)) {
    Write-Host "No events.jsonl found in $($latestSession.FullName)"
    exit 0
}

Write-Host "Reading diagnostics log: $logFile"

$errors = @()
$warnings = @()

Get-Content $logFile | ForEach-Object {
    try {
        $entry = $_ | ConvertFrom-Json
        if ($entry.level -eq 'Error') {
            $errors += "$($entry.timestamp) [$($entry.category)] $($entry.message)"
        } elseif ($entry.level -eq 'Warning') {
            $warnings += "$($entry.timestamp) [$($entry.category)] $($entry.message)"
        }
    } catch {
        # Skip malformed lines
    }
}

$summary = @()
$summary += "## Runtime Health Summary ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))"
$summary += ""
$summary += "**Session:** $($latestSession.Name)"
$summary += "**Log file:** $logFile"
$summary += ""

if ($errors.Count -gt 0) {
    $summary += "### Errors ($($errors.Count))"
    $summary += $errors | Select-Object -First 10 | ForEach-Object { "- $_" }
    if ($errors.Count -gt 10) {
        $summary += "- ... and $($errors.Count - 10) more errors"
    }
    $summary += ""
}

if ($warnings.Count -gt 0) {
    $summary += "### Warnings ($($warnings.Count))"
    $summary += $warnings | Select-Object -First 10 | ForEach-Object { "- $_" }
    if ($warnings.Count -gt 10) {
        $summary += "- ... and $($warnings.Count - 10) more warnings"
    }
    $summary += ""
}

if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    $summary += "✅ No errors or warnings found in the latest session."
}

$summary += ""
$summary += "---"
$summary += "*Automated report from diagnostics-log monitor*"

$summary -join "`n"