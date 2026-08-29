[CmdletBinding()]
param(
    [switch]$RequireLicense,
    [switch]$RequirePublicRepository,
    [switch]$RequirePrivateSecurityChannel
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
    'scripts\package-release.ps1',
    'src\RouterPlus.Updater\RouterPlus.Updater.csproj'
)

foreach ($requiredFile in $requiredFiles) {
    Require-File $requiredFile
}

if ($RequireLicense) {
    Require-File 'LICENSE'
}

$allowedImagePaths = @(
    (Get-RepositoryPath 'docs\assets\9router-profile-workspace.png'),
    (Get-RepositoryPath 'src\RouterPlus.App\Assets\RouterPlus.ico')
) | ForEach-Object { [IO.Path]::GetFullPath($_) }
$releaseImages = Get-ChildItem -Path $repositoryRoot -Recurse -File -Include '*.png', '*.jpg', '*.jpeg', '*.gif', '*.webp', '*.bmp', '*.ico' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|artifacts|work|\.worktrees|\.bootstrap|\.dotnet)\\' }
$unexpectedImages = $releaseImages |
    Where-Object { $allowedImagePaths -notcontains [IO.Path]::GetFullPath($_.FullName) }
if ($unexpectedImages) {
    $paths = $unexpectedImages.FullName -join [Environment]::NewLine
    throw "Unexpected image assets are present in the release tree. Review them for personal data:$([Environment]::NewLine)$paths"
}

$binaryExtensions = @(
    '.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.ico',
    '.dll', '.exe', '.zip', '.7z', '.pdb', '.dmp', '.mdmp',
    '.pdf', '.cer', '.pfx', '.pem', '.key', '.woff', '.woff2', '.ttf'
)
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
$publicDataPatterns = @(
    'arpachy',
    'hieuck\.browser',
    'gmail\.com',
    'yahoo\.com',
    'CentBrowser',
    'G:\\Program Files'
)
$sensitivePatterns = @(
    '\bgh[pousr]_[A-Za-z0-9_]{20,}\b',
    '\bgithub_pat_[A-Za-z0-9_]{20,}\b',
    '\bAKIA[0-9A-Z]{16}\b',
    '\bASIA[0-9A-Z]{16}\b',
    '\bAIza[0-9A-Za-z_-]{20,}\b',
    'xox[baprs]-[0-9A-Za-z-]{20,}',
    '\bsk-[A-Za-z0-9]{20,}\b',
    '-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----'
)

function Test-TextReleasePath([string]$relativePath) {
    $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
    return $binaryExtensions -notcontains $extension
}

$trackedFiles = @(git -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files for release preflight.'
}
$untrackedFiles = @(git -C $repositoryRoot ls-files --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate non-ignored files for release preflight.'
}
$scanFiles = @($trackedFiles + $untrackedFiles) |
    Where-Object { Test-TextReleasePath $_ } |
    Sort-Object -Unique

foreach ($relativePath in $scanFiles) {
    $path = Get-RepositoryPath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $text = Get-Content -LiteralPath $path -Raw
    foreach ($pattern in $sensitivePatterns) {
        if ($text -match $pattern) {
            throw "Sensitive-value pattern '$pattern' found in release tree file: $relativePath"
        }
    }
}

foreach ($relativePath in $publicTextFiles) {
    $path = Get-RepositoryPath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $text = Get-Content -LiteralPath $path -Raw
    foreach ($pattern in $publicDataPatterns) {
        if ($text -match $pattern) {
            throw "Sensitive-value pattern '$pattern' found in public release file: $relativePath"
        }
    }
}

$readme = Get-Content -LiteralPath (Get-RepositoryPath 'README.md') -Raw
if ($readme -notmatch 'https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/releases(?:/latest)?') {
    throw 'README.md does not link to GitHub Releases.'
}

$changelog = Get-Content -LiteralPath (Get-RepositoryPath 'CHANGELOG.md') -Raw
if ($changelog -notmatch '(?m)^## Unreleased\r?$') {
    throw 'CHANGELOG.md must contain an Unreleased section.'
}

if ($RequirePrivateSecurityChannel) {
    $configuredChannel = $env:SECURITY_REPORTING_CHANNEL
    if ([string]::IsNullOrWhiteSpace($configuredChannel)) {
        throw 'SECURITY_REPORTING_CHANNEL must be configured before a public release.'
    }

    $security = Get-Content -LiteralPath (Get-RepositoryPath 'SECURITY.md') -Raw
    if ($security -notmatch [Regex]::Escape($configuredChannel)) {
        throw 'SECURITY.md does not publish the configured private security reporting channel.'
    }
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
