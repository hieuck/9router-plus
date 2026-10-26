$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$bootstrapDirectory = Join-Path $repoRoot ".bootstrap"
$dotnetDirectory = Join-Path $repoRoot ".dotnet"
$installerPath = Join-Path $bootstrapDirectory "dotnet-install.ps1"

# Pin: dotnet/install-scripts@4a37a9f9d1a061fc389d6515100336db4e51710e (2026-07-07)
# SHA256: 3bb07bc8025211836c1e4f9d3f6a044e55b1fb6eec518a6c78851d04e210442b
# Update: check https://github.com/dotnet/install-scripts/commits/main/src/dotnet-install.ps1 for new commits,
# download the raw file at the new commit, compute SHA256, and update both the URL and hash below.
$pinnedInstallerUrl = "https://raw.githubusercontent.com/dotnet/install-scripts/4a37a9f9d1a061fc389d6515100336db4e51710e/src/dotnet-install.ps1"
$expectedSha256 = "3bb07bc8025211836c1e4f9d3f6a044e55b1fb6eec518a6c78851d04e210442b"

New-Item -ItemType Directory -Force $bootstrapDirectory | Out-Null

if (-not (Test-Path -LiteralPath $installerPath)) {
    Write-Host "Downloading pinned dotnet-install.ps1..."
    Invoke-WebRequest -UseBasicParsing $pinnedInstallerUrl -OutFile $installerPath
}

# Verify SHA256 before execution (fail closed)
$actualSha256 = (Get-FileHash -Algorithm SHA256 -Path $installerPath).Hash.ToLowerInvariant()
if ($actualSha256 -ne $expectedSha256.ToLowerInvariant()) {
    Write-Error "SHA256 mismatch for dotnet-install.ps1!"
    Write-Error "Expected: $expectedSha256"
    Write-Error "Actual:   $actualSha256"
    Write-Error "Refusing to execute unverified installer. Update the pin in bootstrap-dotnet.ps1."
    exit 1
}

if (-not (Test-Path -LiteralPath (Join-Path $dotnetDirectory "dotnet.exe"))) {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerPath -Channel 8.0 -InstallDir $dotnetDirectory -NoPath
}

& (Join-Path $dotnetDirectory "dotnet.exe") --info
