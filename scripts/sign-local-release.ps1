[CmdletBinding()]
param(
    [Parameter()]
    [string]$Version = '0.0.0-local',

    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [string]$Publisher = '9Router Project',

    [Parameter()]
    [string]$ManifestPrivateKeyPath
)

$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'sign-local-release.ps1 requires Windows PowerShell with signtool.exe.'
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repositoryRoot 'RouterPlus.sln'
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "RouterPlus.sln is missing in $repositoryRoot."
}

$versionPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'
if ($Version -notmatch $versionPattern) {
    throw "Version '$Version' is invalid. Use MAJOR.MINOR.PATCH or a prerelease suffix."
}


if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\local'
}

$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ($outputPath -eq [IO.Path]::GetFullPath($repositoryRoot)) {
    throw 'OutputDirectory cannot be the repository root.'
}

if (Test-Path -LiteralPath $outputPath) {
    $existingEntries = @(Get-ChildItem -LiteralPath $outputPath -Force)
    if ($existingEntries.Count -gt 0) {
        throw ('OutputDirectory must be new or empty: ' + $outputPath)
    }
} elseif (-not (Test-Path -LiteralPath $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

$stagingPath = Join-Path $outputPath 'staging'
$testResultsPath = Join-Path ([IO.Path]::GetTempPath()) ('routerplus-local-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
New-Item -ItemType Directory -Path $testResultsPath -Force | Out-Null

function Invoke-NativeCommand([string]$FilePath, [string[]]$Arguments, [string]$FailureMessage) {
    & $FilePath @Arguments 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
    $localDotnet
} else {
    (Get-Command dotnet.exe -ErrorAction Stop).Source
}
$runtime = 'win-x64'
$configuration = 'Release'
$appProject = Join-Path $repositoryRoot 'src\RouterPlus.App\RouterPlus.App.csproj'
$updaterProject = Join-Path $repositoryRoot 'src\RouterPlus.Updater\RouterPlus.Updater.csproj'

$certificate = $null
$pfxPath = Join-Path ([IO.Path]::GetTempPath()) ('routerplus-local-signing-' + [Guid]::NewGuid().ToString('N') + '.pfx')
$cerPath = Join-Path $outputPath 'RouterPlus-Dev-Test.cer'
$pfxPassword = $null

try {
    Write-Output '[sign-local-release] Restoring solution...'
    Invoke-NativeCommand $dotnet @('restore', $solutionPath, '--runtime', $runtime) 'dotnet restore failed.'

    Write-Output '[sign-local-release] Running tests...'
    Invoke-NativeCommand $dotnet @('test', $solutionPath, '--configuration', $configuration, '--no-restore', '--logger', 'trx;LogFileName=RouterPlus.LocalTests.trx', '--results-directory', $testResultsPath) 'dotnet test failed.'

    Write-Output '[sign-local-release] Building solution...'
    Invoke-NativeCommand $dotnet @('build', $solutionPath, '--configuration', $configuration, '--no-restore') 'dotnet build failed.'

    Write-Output '[sign-local-release] Publishing App...'
    Invoke-NativeCommand $dotnet @('publish', $appProject, '--configuration', $configuration, '--runtime', $runtime, '--self-contained', 'true', '--no-restore', '--output', $stagingPath, "-p:Version=$Version", "-p:InformationalVersion=$Version") 'App publish failed.'

    Write-Output '[sign-local-release] Publishing Updater...'
    Invoke-NativeCommand $dotnet @('publish', $updaterProject, '--configuration', $configuration, '--runtime', $runtime, '--self-contained', 'true', '--no-restore', '--output', $stagingPath, "-p:Version=$Version", "-p:InformationalVersion=$Version") 'Updater publish failed.'

    Write-Output '[sign-local-release] Adding release documentation...'
    Invoke-NativeCommand (Join-Path $PSScriptRoot 'package-release.ps1') @('-PublishDirectory', $stagingPath) 'Release documentation packaging failed.'

    Write-Output '[sign-local-release] Creating a temporary self-signed certificate...'
    $pfxPassword = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([Guid]::NewGuid().ToString('N'))).Substring(0, 24) + '!Aa1'
    $securePassword = ConvertTo-SecureString $pfxPassword -AsPlainText -Force
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject "CN=$Publisher" `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy Exportable `
        -NotAfter (Get-Date).AddDays(30)

    Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
    Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null

    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        $signtool = Get-ChildItem -Path 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
            Select-Object -First 1
    }
    if (-not $signtool) {
        throw 'signtool.exe is not available. Install the Windows SDK first.'
    }

    foreach ($binary in @('RouterPlus.exe', 'RouterPlus.Updater.exe')) {
        $binaryPath = Join-Path $stagingPath $binary
        $signtoolPath = if ($signtool.Source) { $signtool.Source } else { $signtool.FullName }
        & $signtoolPath sign /fd SHA256 /f $pfxPath /p $pfxPassword $binaryPath 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Could not sign $binary."
        }

        $signature = Get-AuthenticodeSignature -FilePath $binaryPath
        if ($null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notlike "*$Publisher*") {
            throw "The self-signed publisher could not be read from $binary."
        }
    }

    & (Join-Path $PSScriptRoot 'release-preflight.ps1') -RequireLicense -PublishDirectory $stagingPath 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Release preflight failed.'
    }

    $archiveBaseName = "RouterPlus-v$Version-win-x64"
    $archivePath = Join-Path $outputPath "$archiveBaseName.zip"
    $checksumPath = Join-Path $outputPath "$archiveBaseName.zip.sha256"
    Compress-Archive -Path (Join-Path $stagingPath '*') -DestinationPath $archivePath -CompressionLevel Optimal

    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $archiveBaseName.zip" | Set-Content -LiteralPath $checksumPath -Encoding ascii

    $manifestPath = $null
    if (-not [string]::IsNullOrWhiteSpace($ManifestPrivateKeyPath)) {
        if (-not (Test-Path -LiteralPath $ManifestPrivateKeyPath -PathType Leaf)) {
            throw "Manifest private key not found: $ManifestPrivateKeyPath"
        }

        $manifestPath = Join-Path $outputPath "$archiveBaseName-manifest.json"
        $manifest = [ordered]@{
            version   = $Version
            channel   = if ($Version.Contains('-')) { 'prerelease' } else { 'stable' }
            assetName = "$archiveBaseName.zip"
            sha256    = $hash
            publisher = $Publisher
            signature = ''
        }
        $manifest | ConvertTo-Json -Depth 5 -Compress | Set-Content -LiteralPath $manifestPath -Encoding utf8
        Invoke-NativeCommand (Join-Path $PSScriptRoot 'sign-release-manifest.ps1') @('-ManifestPath', $manifestPath, '-PrivateKeyPath', $ManifestPrivateKeyPath) 'Manifest signing failed.'
    }

    Remove-Item -LiteralPath $stagingPath -Recurse -Force

    Write-Output ''
    Write-Output 'LOCAL DEV ARTIFACTS'
    Write-Output ("Archive   : {0}" -f $archivePath)
    Write-Output ("Checksum  : {0}" -f $checksumPath)
    Write-Output ("Dev cert  : {0}" -f $cerPath)
    if ($manifestPath) {
        Write-Output ("Manifest  : {0}" -f $manifestPath)
    } else {
        Write-Warning 'No manifest was created. Pass -ManifestPrivateKeyPath to test the signed self-update manifest.'
    }
    Write-Output 'The self-signed certificate is for local development only.'
}
finally {
    if ($certificate) {
        $certificatePath = Join-Path 'Cert:\CurrentUser\My' $certificate.Thumbprint
        if (Test-Path -LiteralPath $certificatePath) {
            Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
        }
    }
    if (Test-Path -LiteralPath $pfxPath) {
        Remove-Item -LiteralPath $pfxPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $testResultsPath) {
        Remove-Item -LiteralPath $testResultsPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}


