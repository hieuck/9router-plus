<#
.SYNOPSIS
    Poll the Nous device code endpoint until authorized, then list Nous connections.

.DESCRIPTION
    This script is part of the Nous live verification toolkit.
    It logs into 9Router, reads the device code JSON from a file,
    polls /api/oauth/nous/poll at the specified interval,
    logs each poll result, and on success lists the Nous connections (id/name/createdAt).

.PARAMETER BaseUrl
    Base URL of the 9Router dashboard (default: http://127.0.0.1:20128).

.PARAMETER Password
    Dashboard password (mandatory).

.PARAMETER DeviceFile
    Path to the device code JSON file (default: $env:TEMP\routerplus-nous-live\nous-device.json).

.PARAMETER LogFile
    Path to log file (default: $env:TEMP\routerplus-nous-live\nous-poll.log).

.EXAMPLE
    .\poll-device-code.ps1 -Password "<DASHBOARD_PASSWORD>"

.EXAMPLE
    .\poll-device-code.ps1 -BaseUrl "http://localhost:20128" -Password "<DASHBOARD_PASSWORD>" -DeviceFile "C:\temp\nous-device.json" -LogFile "C:\temp\poll.log"
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$BaseUrl = 'http://127.0.0.1:20128',

    [Parameter(Mandatory=$true)]
    [string]$Password,

    [Parameter()]
    [string]$DeviceFile = (Join-Path $env:TEMP 'routerplus-nous-live\nous-device.json'),

    [Parameter()]
    [string]$LogFile = (Join-Path $env:TEMP 'routerplus-nous-live\nous-poll.log')
)

$ErrorActionPreference = 'Stop'

function Log([string]$msg) {
    $line = '{0} {1}' -f (Get-Date -Format o), $msg
    Add-Content -Path $LogFile -Value $line
    Write-Output $line
}

if (-not (Test-Path $DeviceFile)) {
    Log "DEVICE_FILE_NOT_FOUND $DeviceFile"
    exit 1
}

$device = Get-Content $DeviceFile -Raw | ConvertFrom-Json

$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$login = Invoke-WebRequest -Uri "$BaseUrl/api/auth/login" -Method POST `
    -Body (@{ password = $Password } | ConvertTo-Json) `
    -ContentType 'application/json' -WebSession $session -UseBasicParsing -TimeoutSec 15
if ($login.StatusCode -ne 200) { Log "LOGIN_FAILED $($login.StatusCode)"; exit 1 }
Log "LOGIN_OK device=$($device.user_code) expires_in=$($device.expires_in)"

$deadline = (Get-Date).AddSeconds([int]$device.expires_in)
$interval = [Math]::Max(1, [int]$device.interval)
$extraData = @{
    _clientId     = $device._clientId
    _clientSecret = $device._clientSecret
    _region       = $device._region
    _authMethod   = $device._authMethod
    _startUrl     = $device._startUrl
}
$body = @{
    deviceCode  = $device.device_code
    codeVerifier = $device.codeVerifier
    extraData   = $extraData
} | ConvertTo-Json -Depth 6

while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl/api/oauth/nous/poll" -Method POST `
            -Body $body -ContentType 'application/json' -WebSession $session `
            -UseBasicParsing -TimeoutSec 20
        $res = $r.Content | ConvertFrom-Json
        Log ("POLL status={0} success={1} error={2}" -f $r.StatusCode, $res.success, $res.error)
        if ($res.success -eq $true) {
            Log 'AUTHORIZED'
            $providers = Invoke-WebRequest -Uri "$BaseUrl/api/providers" -WebSession $session -UseBasicParsing -TimeoutSec 20
            $connections = ($providers.Content | ConvertFrom-Json).connections
            $nous = @($connections | Where-Object { $_.provider -eq 'nous' })
            Log ("NOUS_COUNT={0}" -f $nous.Count)
            $nous | Sort-Object createdAt -Descending | Select-Object -First 2 |
                ForEach-Object { Log ("NOUS_CONN id={0} name={1} createdAt={2} active={3}" -f $_.id, $_.name, $_.createdAt, $_.isActive) }
            exit 0
        }
        if ($res.error -and $res.error -notin @('authorization_pending', 'slow_down')) {
            Log "TERMINAL_ERROR $($res.error) $($res.errorDescription)"
            exit 2
        }
    }
    catch {
        Log "POLL_EXCEPTION $($_.Exception.Message)"
    }
    Start-Sleep -Seconds $interval
}
Log 'EXPIRED'
exit 3