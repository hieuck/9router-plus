[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Manifest file is missing: $ManifestPath"
}
if (-not (Test-Path -LiteralPath $PrivateKeyPath -PathType Leaf)) {
    throw "Manifest signing key is missing."
}

$openssl = Get-Command openssl.exe -ErrorAction SilentlyContinue
if ($null -eq $openssl) {
    throw 'OpenSSL is required to sign the release manifest.'
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$payload = @(
    [string]$manifest.version
    [string]$manifest.channel
    [string]$manifest.assetName
    ([string]$manifest.sha256).ToLowerInvariant()
    [string]$manifest.publisher
) -join "`n"

$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ('routerplus-manifest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
$payloadPath = Join-Path $temporaryDirectory 'payload.txt'
$signaturePath = Join-Path $temporaryDirectory 'signature.bin'
try {
    [IO.File]::WriteAllText($payloadPath, $payload, [Text.UTF8Encoding]::new($false))
    & $openssl.Source dgst -sha256 -sign $PrivateKeyPath -out $signaturePath $payloadPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $signaturePath -PathType Leaf)) {
        throw 'OpenSSL could not sign the release manifest.'
    }

    $manifest.signature = [Convert]::ToBase64String([IO.File]::ReadAllBytes($signaturePath))
    $json = $manifest | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::WriteAllText($ManifestPath, $json, [Text.UTF8Encoding]::new($false))
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

Write-Output "Release manifest signed: $ManifestPath"
