param([int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$godot = (Get-Command 'Godot_v4.7.2-stable_mono_win64_console.exe' -ErrorAction Stop).Source
$artifacts = Join-Path $root 'artifacts'
$log = Join-Path $artifacts 'grass-qa.log'
$stderr = Join-Path $artifacts 'grass-qa.stderr.log'
Push-Location -LiteralPath $root
try {
    $liveBefore = @{}
    foreach ($relative in @('saves\slot-1.json','saves\settings.json')) {
        $livePath = Join-Path $root $relative
        $liveBefore[$relative] = if (Test-Path -LiteralPath $livePath) { (Get-FileHash -LiteralPath $livePath -Algorithm SHA256).Hash } else { 'absent' }
    }
    & 'C:\Program Files\dotnet\dotnet.exe' build .\Joscraft.csproj *> (Join-Path $artifacts 'grass-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'C# game build failed; see artifacts/grass-build.log' }
    & $godot --headless --editor --path $root --import --quit *> (Join-Path $artifacts 'grass-import.log')
    if ($LASTEXITCODE -ne 0) { throw 'Godot game import failed; see artifacts/grass-import.log' }
    $process = Start-Process -FilePath $godot -ArgumentList @('--path', ('"'+$root+'"'), 'res://world.tscn', '--position', '-16000,-16000', '--resolution', '1280x800', '--', '--grass-qa') -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $stderr -PassThru
    $qaHandle = $process.Handle
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw "JOSCRAFT game QA timed out after $TimeoutSeconds seconds" }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "JOSCRAFT game QA failed (exit $($process.ExitCode)); see $log and $stderr" }
    $result = Get-Content -LiteralPath (Join-Path $artifacts 'grass-qa.json') -Raw | ConvertFrom-Json
    if (!$result.pass) { throw 'Game QA report is not PASS' }
    if ((Get-Content -LiteralPath $stderr -Raw) -match '(?m)^(ERROR|SCRIPT ERROR):') { throw 'Godot reported runtime game errors' }
    $liveAfter = @{}
    foreach ($relative in $liveBefore.Keys) {
        $livePath = Join-Path $root $relative
        $liveAfter[$relative] = if (Test-Path -LiteralPath $livePath) { (Get-FileHash -LiteralPath $livePath -Algorithm SHA256).Hash } else { 'absent' }
        if ($liveAfter[$relative] -ne $liveBefore[$relative]) { throw "QA modified live project save/settings: $relative" }
    }
    @{ pass=$true; before=$liveBefore; after=$liveAfter; scope='QA writes only artifacts/grass-qa-data; live project save/settings preserved' } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $artifacts 'grass-live-isolation.json')
    'JOSCRAFT grass drops / growth / cover / pause / streaming / save-load / actual skin-mesh-physics PASS'
} finally { Pop-Location }

