[CmdletBinding()]
param(
    [switch]$RequireLicense,
    [switch]$RequirePublicRepository,
    [switch]$RequirePrivateSecurityChannel,
    [switch]$RequireUpdateSigning,
    [string]$PublishDirectory
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
    '.github\workflows\release-personal.yml',
    'scripts\package-release.ps1',
    'scripts\sign-local-release.ps1',
    'scripts\sign-release-manifest.ps1',
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

if ($RequireUpdateSigning) {
    if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
        throw 'PublishDirectory is required for the update-signing gate.'
    }

    $publishPath = (Resolve-Path $PublishDirectory).Path
    $signingSubject = $env:UPDATE_SIGNING_SUBJECT
    if ([string]::IsNullOrWhiteSpace($signingSubject)) {
        throw 'UPDATE_SIGNING_SUBJECT must be configured for the update-signing gate.'
    }
    if ([string]::IsNullOrWhiteSpace($env:MANIFEST_SIGNING_KEY_BASE64)) {
        throw 'MANIFEST_SIGNING_KEY_BASE64 must be configured for the manifest-signing gate.'
    }

    foreach ($binary in @('RouterPlus.exe', 'RouterPlus.Updater.exe')) {
        $binaryPath = Join-Path $publishPath $binary
        if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
            throw "Signed release binary is missing: $binary"
        }

        $signature = Get-AuthenticodeSignature -FilePath $binaryPath
        if ($signature.Status -ne 'Valid') {
            throw "Authenticode signature is not valid for $binary"
        }
        if ($signature.SignerCertificate.Subject -notlike "*$signingSubject*") {
            throw "Authenticode signer does not match the configured publisher for $binary"
        }
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
