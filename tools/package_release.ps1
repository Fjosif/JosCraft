param([string]$Version='0.4.3')
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use numeric major.minor.patch' }
$runtime=Get-Content -LiteralPath (Join-Path $root 'runtime\AutoUpdater.cs') -Raw
if (!$runtime.Contains('CurrentVersion="'+$Version+'"')) { throw 'Version does not match runtime/AutoUpdater.cs' }
$godot=(Get-Command Godot_v4.7.2-stable_mono_win64_console.exe).Source
$bundle=Join-Path $root ('build\release-'+$Version)
$payload=Join-Path $bundle ('versions\'+$Version)
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Push-Location -LiteralPath $root
try {
    $ErrorActionPreference='Continue'
    & $godot --headless --path $root --export-release 'Windows Desktop' (Join-Path $payload 'JosCraft.exe') *> (Join-Path $root 'artifacts\export-windows.log')
    $ErrorActionPreference='Stop'
    if($LASTEXITCODE -ne 0){throw 'Windows export failed; see artifacts/export-windows.log'}
    if((Get-Content -LiteralPath (Join-Path $root 'artifacts\export-windows.log') -Raw) -match 'ERROR:'){throw 'Godot export emitted errors despite exit code; see artifacts/export-windows.log'}
    Copy-Item -LiteralPath (Join-Path $root 'distribution\apply-update.ps1') -Destination $payload -Force
    Copy-Item -LiteralPath (Join-Path $root 'distribution\launch.ps1'),(Join-Path $root 'distribution\JosCraft.vbs') -Destination $bundle -Force
    Copy-Item -LiteralPath (Join-Path $root 'distribution\GODOT-LICENSE.txt'),(Join-Path $root 'distribution\THIRD_PARTY_NOTICES.txt') -Destination $bundle -Force
    Copy-Item -LiteralPath (Join-Path $root 'distribution\DOTNET-LICENSE.txt'),(Join-Path $root 'distribution\DOTNET-THIRD-PARTY-NOTICES.txt') -Destination $bundle -Force
    @{Version=$Version} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'current.json') -Encoding utf8
    'Rozbal celou složku do zapisovatelného umístění a spusť JosCraft.vbs. Další vydané aktualizace se stáhnou automaticky. Světy jsou oddělené v uživatelských datech Godotu/JOSCRAFT/saves; tento ZIP neobsahuje tvoje uložené hry.' | Set-Content -LiteralPath (Join-Path $bundle 'PRECTI_ME.txt') -Encoding utf8
    $zip=Join-Path $root 'build\JosCraft-windows-x64.zip'
    Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath $zip -Force
    @{Format=1;Version=$Version;Package='JosCraft-windows-x64.zip';Sha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant();Bytes=(Get-Item -LiteralPath $zip).Length} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'build\update.json') -Encoding utf8
    "Release package ready: $zip"
} finally { Pop-Location }
