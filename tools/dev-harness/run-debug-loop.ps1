param(
    [string]$Configuration = "Debug",
    [string]$Filter = "FullyQualifiedName~ProfileContextMenuTests",
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$appProject = Join-Path $repoRoot "src\RouterPlus.App\RouterPlus.App.csproj"
$e2eProject = Join-Path $repoRoot "tests\RouterPlus.App.E2E\RouterPlus.App.E2E.csproj"

& dotnet build $appProject -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& dotnet test $e2eProject -c $Configuration --no-restore --filter $Filter
$exitCode = $LASTEXITCODE

if (-not $KeepArtifacts) {
    Remove-Item (Join-Path $env:TEMP "RouterPlusHarness") -Recurse -Force -ErrorAction SilentlyContinue
}

exit $exitCode
