[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishPath = (Resolve-Path $PublishDirectory).Path

foreach ($requiredBinary in @('RouterPlus.exe', 'RouterPlus.Updater.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $requiredBinary) -PathType Leaf)) {
        throw "Publish directory does not contain ${requiredBinary}: $publishPath"
    }
}

$forbiddenArtifacts = Get-ChildItem -LiteralPath $publishPath -Recurse -File |
    Where-Object { $_.Extension -in @('.pdb', '.dmp', '.mdmp') }
if ($forbiddenArtifacts) {
    $paths = $forbiddenArtifacts.FullName -join [Environment]::NewLine
    throw "Publish directory contains debug artifacts that must not ship:$([Environment]::NewLine)$paths"
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
