$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
$solution = Join-Path $repoRoot "RouterPlus.sln"
$publishDirectory = Join-Path $repoRoot "artifacts\publish"

if (-not (Test-Path -LiteralPath $dotnet)) {
    & (Join-Path $PSScriptRoot "bootstrap-dotnet.ps1")
}

& $dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet test $solution --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $dotnet build $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force $publishDirectory | Out-Null
& $dotnet publish (Join-Path $repoRoot "src\RouterPlus.App\RouterPlus.App.csproj") --configuration Release --runtime win-x64 --self-contained false --output $publishDirectory
exit $LASTEXITCODE
