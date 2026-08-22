Describe 'release-preflight secret scan' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('routerplus-preflight-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:temporaryRoot -Force | Out-Null

        $trackedFiles = @(git -C $repoRoot ls-files)
        foreach ($relativePath in $trackedFiles) {
            $sourcePath = Join-Path $repoRoot $relativePath
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                continue
            }

            $destinationPath = Join-Path $script:temporaryRoot ($relativePath -replace '/', '\')
            New-Item -ItemType Directory -Path (Split-Path -Parent $destinationPath) -Force | Out-Null
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        }

        git -C $script:temporaryRoot init --quiet
        git -C $script:temporaryRoot config user.email 'test@example.com'
        git -C $script:temporaryRoot config user.name 'Preflight Test'
        git -C $script:temporaryRoot add --all
        $script:preflightPath = Join-Path $script:temporaryRoot 'scripts/release-preflight.ps1'
    }

    AfterAll {
        if (Test-Path -LiteralPath $script:temporaryRoot) {
            Remove-Item -LiteralPath $script:temporaryRoot -Recurse -Force
        }
    }

    It 'passes a clean tracked tree' {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script:preflightPath *> $null
        $LASTEXITCODE | Should Be 0
    }

    It 'rejects a secret pattern in a tracked source file' {
        $markerPath = Join-Path $script:temporaryRoot 'src/RouterPlus.Core/PreflightSecretMarker.cs'
        $secretMarker = 'gh' + 'p_' + '123456789012345678901234567890'
        Set-Content -LiteralPath $markerPath -Value ("const string Marker = '$secretMarker';") -Encoding utf8
        git -C $script:temporaryRoot add --all

        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script:preflightPath *> $null
        $LASTEXITCODE | Should Not Be 0
    }
}
