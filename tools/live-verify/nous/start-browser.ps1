<#
.SYNOPSIS
    Launch Chrome with the real user data directory/profile, log into 9Router, request Nous device code,
    and optionally enable CDP remote debugging.

.DESCRIPTION
    This script is part of the Nous live verification toolkit.
    It logs into the local 9Router dashboard, requests a Nous device code via /api/oauth/nous/device-code,
    saves the device code JSON to $env:TEMP\routerplus-nous-live\nous-device.json, prints the user code and URL,
    and launches Chrome with the specified user data directory and profile.
    For Mode=Cdp it also adds remote debugging arguments and waits for /json/version to be ready.

.PARAMETER BaseUrl
    Base URL of the 9Router dashboard (default: http://127.0.0.1:20128).

.PARAMETER Password
    Dashboard password (mandatory).

.PARAMETER ChromePath
    Full path to chrome.exe (mandatory).

.PARAMETER UserDataDir
    Chrome User Data Directory (mandatory).

.PARAMETER ProfileDirectory
    Chrome profile directory name (mandatory). Example: "Profile 1" or "Profile user@example.com".

.PARAMETER Port
    Remote debugging port (default: 9333). Only used when Mode=Cdp.

.PARAMETER Mode
    Launch mode: Cdp (default, with remote debugging) or Normal (no remote debugging, Kasada-friendly).

.EXAMPLE
    .\start-browser.ps1 -Password "<DASHBOARD_PASSWORD>" -ChromePath "C:\Program Files\Google\Chrome\Application\chrome.exe" -UserDataDir "C:\Users\me\AppData\Local\Google\Chrome\User Data" -ProfileDirectory "Profile 1" -Mode Cdp

.EXAMPLE
    .\start-browser.ps1 -BaseUrl "http://localhost:20128" -Password "<DASHBOARD_PASSWORD>" -ChromePath "C:\...\chrome.exe" -UserDataDir "C:\...\User Data" -ProfileDirectory "Profile 2" -Mode Normal
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$BaseUrl = 'http://127.0.0.1:20128',

    [Parameter(Mandatory=$true)]
    [string]$Password,

    [Parameter(Mandatory=$true)]
    [string]$ChromePath,

    [Parameter(Mandatory=$true)]
    [string]$UserDataDir,

    [Parameter(Mandatory=$true)]
    [string]$ProfileDirectory,

    [Parameter()]
    [int]$Port = 9333,

    [Parameter()]
    [ValidateSet('Cdp', 'Normal')]
    [string]$Mode = 'Cdp'
)

$ErrorActionPreference = 'Stop'

$outDir = Join-Path $env:TEMP 'routerplus-nous-live'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$deviceFile = Join-Path $outDir 'nous-device.json'

# Login and request device code
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
try {
    $null = Invoke-WebRequest -Uri "$BaseUrl/api/auth/login" -Method POST `
        -Body (@{ password = $Password } | ConvertTo-Json) `
        -ContentType 'application/json' -WebSession $session -UseBasicParsing -TimeoutSec 15
}
catch {
    Write-Error "LOGIN_FAILED $($_.Exception.Message)"
    exit 1
}

$r = Invoke-WebRequest -Uri "$BaseUrl/api/oauth/nous/device-code" -WebSession $session -UseBasicParsing -TimeoutSec 30
$r.Content | Set-Content -Path $deviceFile -Encoding utf8
$j = $r.Content | ConvertFrom-Json

$url = $j.verification_uri_complete
Write-Output "USER_CODE=$($j.user_code)"
Write-Output "URL=$url"
Write-Output "EXPIRES=$($j.expires_in)"
Write-Output "DEVICE_FILE=$deviceFile"

# Launch Chrome
$chromeArgs = @(
    "--user-data-dir=$UserDataDir",
    "--profile-directory=$ProfileDirectory",
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-session-crashed-bubble',
    '--hide-crash-restore-bubble',
    '--new-window',
    $url
)

if ($Mode -eq 'Cdp') {
    $chromeArgs += "--remote-debugging-address=127.0.0.1"
    $chromeArgs += "--remote-debugging-port=$Port"
}

Write-Output "LAUNCHING_CHROME: $ChromePath $chromeArgs"
Start-Process -FilePath $ChromePath -ArgumentList $chromeArgs

if ($Mode -eq 'Cdp') {
    # Wait for CDP endpoint
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        try {
            $v = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/json/version" -UseBasicParsing -TimeoutSec 2
            if ($v.StatusCode -eq 200) { Write-Output 'CDP_READY'; break }
        } catch {}
        Start-Sleep -Milliseconds 500
    }
    if ((Get-Date) -ge $deadline) {
        Write-Error 'CDP_NOT_READY: Chrome did not start remote debugging in time.'
        exit 2
    }
}

Write-Output 'DONE'