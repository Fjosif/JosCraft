param([string]$Version='0.4.3')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $root
try {
    # This script is an explicit publishing action. Merely launching/playing never publishes.
    foreach($script in @('verify_building.ps1','verify_survival.ps1','verify_net.ps1','package_release.ps1','verify_updater.ps1')) {
        if($script -eq 'package_release.ps1'){& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $script) -Version $Version}
        else{& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot $script)}
        if($LASTEXITCODE -ne 0){throw "Check failed: $script"}
    }
    $manifest=Get-Content build\update.json -Raw|ConvertFrom-Json
    if($manifest.Version -ne $Version){throw 'Package version does not match requested release'}
    $tag='v'+$Version
    & gh release create $tag build\JosCraft-windows-x64.zip build\update.json --repo Fjosif/JosCraft --verify-tag --title ('JOSCRAFT '+$Version) --notes-file docs\release-notes.md
    if($LASTEXITCODE -ne 0){throw 'GitHub release publishing failed'}
} finally {Pop-Location}
