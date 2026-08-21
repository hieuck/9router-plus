[CmdletBinding()]
param(
    [switch]$RequireLicense,
    [switch]$RequirePublicRepository
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

function Get-RepositoryPath([string]$relativePath) {
    return Join-Path $repositoryRoot $relativePath
}

function Require-File([string]$relativePath) {
    $path = Get-RepositoryPath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release file is missing: $relativePath"
    }
}

$requiredFiles = @(
    'README.md',
    'CHANGELOG.md',
    'SECURITY.md',
    'docs\user-guide.md',
    'docs\privacy.md',
    'docs\troubleshooting.md',
    'docs\release-checklist.md',
    'docs\assets\9router-profile-workspace.png',
    '.github\workflows\ci.yml',
    '.github\workflows\release.yml',
    'scripts\package-release.ps1'
)

foreach ($requiredFile in $requiredFiles) {
    Require-File $requiredFile
}

if ($RequireLicense) {
    Require-File 'LICENSE'
}

$rawScreenshots = Get-ChildItem -Path $repositoryRoot -Recurse -Filter 'ui-*.png' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|artifacts|work|\.worktrees)\\' }
if ($rawScreenshots) {
    $paths = $rawScreenshots.FullName -join [Environment]::NewLine
    throw "Raw UI screenshots are present in the release tree:$([Environment]::NewLine)$paths"
}

$publicTextFiles = @(
    'README.md',
    'CHANGELOG.md',
    'SECURITY.md',
    'docs\user-guide.md',
    'docs\privacy.md',
    'docs\troubleshooting.md',
    'docs\release-checklist.md',
    '.github\ISSUE_TEMPLATE\bug_report.md',
    '.github\ISSUE_TEMPLATE\feature_request.md',
    '.github\PULL_REQUEST_TEMPLATE.md'
)
$sensitivePatterns = @(
    'arpachy',
    'hieuck\.browser',
    'gmail\.com',
    'yahoo\.com',
    'CentBrowser',
    'G:\\Program Files',
    'ghp_[A-Za-z0-9]+',
    'github_pat_[A-Za-z0-9_]+',
    'sk-[A-Za-z0-9]+',
    '-----BEGIN .*PRIVATE KEY-----'
)
foreach ($relativePath in $publicTextFiles) {
    $path = Get-RepositoryPath $relativePath
    $text = Get-Content -LiteralPath $path -Raw
    foreach ($pattern in $sensitivePatterns) {
        if ($text -match $pattern) {
            throw "Sensitive-value pattern '$pattern' found in public release file: $relativePath"
        }
    }
}

$readme = Get-Content -LiteralPath (Get-RepositoryPath 'README.md') -Raw
if ($readme -notmatch 'releases/latest') {
    throw 'README.md does not link to the latest release.'
}

$changelog = Get-Content -LiteralPath (Get-RepositoryPath 'CHANGELOG.md') -Raw
if ($changelog -notmatch '(?m)^## Unreleased$') {
    throw 'CHANGELOG.md must contain an Unreleased section.'
}

if ($RequirePublicRepository) {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) {
        throw 'GITHUB_REPOSITORY is required for the public repository check.'
    }

    if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
        throw 'GH_TOKEN is required for the public repository check.'
    }

    $visibility = (& gh api "repos/$env:GITHUB_REPOSITORY" --jq '.visibility').Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to query GitHub repository visibility.'
    }

    if ($visibility -ne 'public') {
        throw "Repository visibility is '$visibility'. Public release requires visibility 'public'."
    }
}

$licenseState = if (Test-Path -LiteralPath (Get-RepositoryPath 'LICENSE') -PathType Leaf) { 'present' } else { 'missing (release gate disabled)' }
Write-Output "Release preflight passed. License: $licenseState"
