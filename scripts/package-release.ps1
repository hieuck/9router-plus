[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishPath = (Resolve-Path $PublishDirectory).Path

if (-not (Test-Path -LiteralPath (Join-Path $publishPath 'RouterPlus.exe') -PathType Leaf)) {
    throw "Publish directory does not contain RouterPlus.exe: $publishPath"
}

function Copy-ReleaseFile([string]$relativePath, [string]$destinationRelativePath = $relativePath) {
    $source = Join-Path $repositoryRoot $relativePath
    $destination = Join-Path $publishPath $destinationRelativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Release documentation source is missing: $relativePath"
    }

    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

Copy-ReleaseFile 'README.md'
Copy-ReleaseFile 'CHANGELOG.md'
Copy-ReleaseFile 'SECURITY.md'
Copy-ReleaseFile 'docs\user-guide.md'
Copy-ReleaseFile 'docs\privacy.md'
Copy-ReleaseFile 'docs\troubleshooting.md'
Copy-ReleaseFile 'docs\release-checklist.md'
Copy-ReleaseFile 'docs\assets\9router-profile-workspace.png'

if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -PathType Leaf) {
    Copy-ReleaseFile 'LICENSE'
}

Write-Output "Release documentation copied into $publishPath"
