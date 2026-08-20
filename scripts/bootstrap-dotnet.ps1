$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$bootstrapDirectory = Join-Path $repoRoot ".bootstrap"
$dotnetDirectory = Join-Path $repoRoot ".dotnet"
$installerPath = Join-Path $bootstrapDirectory "dotnet-install.ps1"

New-Item -ItemType Directory -Force $bootstrapDirectory | Out-Null

if (-not (Test-Path -LiteralPath $installerPath)) {
    Invoke-WebRequest -UseBasicParsing "https://dot.net/v1/dotnet-install.ps1" -OutFile $installerPath
}

if (-not (Test-Path -LiteralPath (Join-Path $dotnetDirectory "dotnet.exe"))) {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath -Channel 8.0 -InstallDir $dotnetDirectory -NoPath
}

& (Join-Path $dotnetDirectory "dotnet.exe") --info
