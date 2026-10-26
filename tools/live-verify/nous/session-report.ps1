<#
.SYNOPSIS
    Compact report of selected events from a RouterPlus observability session.

.PARAMETER SessionDir
    Session directory (default: newest under %LOCALAPPDATA%\RouterPlus\Observability\sessions).

.PARAMETER Pattern
    Event-name substrings to include.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$SessionDir,

    [Parameter()]
    [string[]]$Pattern = @('ProviderWorkflowStarted','ProviderWorkflowFinished','FlowCompleted','AccountPicker','AccountClicked','ConsentButtonClicked','Totp','FlowFailed')
)

$ErrorActionPreference = 'Stop'

$base = Join-Path $env:LOCALAPPDATA 'RouterPlus\Observability\sessions'
if (-not $SessionDir) {
    if (-not (Test-Path $base)) { Write-Error "SESSIONS_DIR_NOT_FOUND $base"; exit 1 }
    $SessionDir = (Get-ChildItem -Path $base -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
}

$eventsFile = Join-Path $SessionDir 'events.jsonl'
if (-not (Test-Path $eventsFile)) { Write-Error "EVENTS_FILE_NOT_FOUND $eventsFile"; exit 1 }

Write-Output "SESSION_PATH=$SessionDir"

Get-Content -Path $eventsFile | ForEach-Object {
    $line = $_.Trim()
    if (-not $line) { return }
    try { $o = $line | ConvertFrom-Json } catch { return }
    $eventName = [string]$o.event
    $match = $false
    foreach ($p in $Pattern) {
        if ($eventName -like "*$p*") { $match = $true; break }
    }
    if (-not $match) { return }

    $ts = [string]$o.timestamp
    $time = if ($ts.Length -ge 15) { $ts.Substring(11, 12) } else { $ts }

    $ctx = ''
    if ($null -ne $o.context) {
        try { $ctx = ($o.context | ConvertTo-Json -Compress -Depth 5) } catch { $ctx = [string]$o.context }
    }
    if ($ctx.Length -gt 400) { $ctx = $ctx.Substring(0, 400) + '...' }

    $msg = [string]$o.message
    Write-Output ("{0} [{1}/{2}] {3} :: {4}" -f $time, $o.category, $o.event, $msg, $ctx)
}