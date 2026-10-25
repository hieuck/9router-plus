<#
.SYNOPSIS
    Watch for new Nous connections and detect when a connection is renamed to the expected profile name.

.DESCRIPTION
    This script is part of the Nous live verification toolkit.
    It logs into 9Router, captures a baseline of existing Nous connection IDs,
    then polls /api/providers every 3 seconds for the specified duration.
    When a new Nous connection appears, it logs it.
    If the new connection's name matches the provided ProfileName, it logs RENAMED_TO_PROFILE and exits 0.

.PARAMETER BaseUrl
    Base URL of the 9Router dashboard (default: http://127.0.0.1:20128).

.PARAMETER Password
    Dashboard password (mandatory).

.PARAMETER ProfileName
    Expected profile name to match against new connection names (optional). If provided and a new connection's name matches, logs RENAMED_TO_PROFILE and exits success.

.PARAMETER DurationMinutes
    How long to watch in minutes (default: 12).

.PARAMETER LogFile
    Path to log file (default: $env:TEMP\routerplus-nous-live\watch-connections.log).

.EXAMPLE
    .\watch-connections.ps1 -Password "123456" -ProfileName "hieuck.banned@gmail.com"

.EXAMPLE
    .\watch-connections.ps1 -BaseUrl "http://localhost:20128" -Password "mypass" -ProfileName "My Profile" -DurationMinutes 10 -LogFile "C:\temp\watch.log"
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$BaseUrl = 'http://127.0.0.1:20128',

    [Parameter(Mandatory=$true)]
    [string]$Password,

    [Parameter()]
    [string]$ProfileName,

    [Parameter()]
    [int]$DurationMinutes = 12,

    [Parameter()]
    [string]$LogFile = (Join-Path $env:TEMP 'routerplus-nous-live\watch-connections.log')
)

$ErrorActionPreference = 'Continue'

function Log([string]$m) {
    Add-Content -Path $LogFile -Value ('{0} {1}' -f (Get-Date -Format o), $m)
}

$s = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$null = Invoke-WebRequest -Uri "$BaseUrl/api/auth/login" -Method POST -Body (@{ password = $Password } | ConvertTo-Json) -ContentType 'application/json' -WebSession $s -UseBasicParsing -TimeoutSec 15
$p = Invoke-WebRequest -Uri "$BaseUrl/api/providers" -WebSession $s -UseBasicParsing -TimeoutSec 20
$baseline = @{}
@(($p.Content | ConvertFrom-Json).connections | Where-Object { $_.provider -eq 'nous' }) | ForEach-Object { $baseline[$_.id] = $true }
Log ("BASELINE_NOUS={0}" -f $baseline.Count)

$deadline = (Get-Date).AddMinutes($DurationMinutes)
while ((Get-Date) -lt $deadline) {
    try {
        $p = Invoke-WebRequest -Uri "$BaseUrl/api/providers" -WebSession $s -UseBasicParsing -TimeoutSec 20
        $nous = @(($p.Content | ConvertFrom-Json).connections | Where-Object { $_.provider -eq 'nous' })
        $new = @($nous | Where-Object { -not $baseline.ContainsKey($_.id) })
        if ($new.Count -gt 0) {
            foreach ($c in $new) {
                Log ("NEW_NOUS id={0} name={1} createdAt={2} active={3}" -f $c.id, $c.name, $c.createdAt, $c.isActive)
                if ($ProfileName -and $c.name -eq $ProfileName) {
                    Log 'RENAMED_TO_PROFILE'
                    exit 0
                }
            }
        }
    } catch {
        Log "WATCH_ERR $($_.Exception.Message)"
    }
    Start-Sleep -Seconds 3
}
Log 'WATCH_TIMEOUT'
exit 3