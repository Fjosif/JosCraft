param([int]$TimeoutSeconds = 240)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$godot = (Get-Command 'Godot_v4.7.2-stable_mono_win64_console.exe' -ErrorAction Stop).Source
$artifacts = Join-Path $root 'artifacts'
$log = Join-Path $artifacts 'godot-qa.log'
$stderr = Join-Path $artifacts 'godot-qa.stderr.log'
Push-Location -LiteralPath $root
try {
    & 'C:\Program Files\dotnet\dotnet.exe' build .\Joscraft.csproj *> (Join-Path $artifacts 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed; see artifacts/build.log' }
    & $godot --headless --editor --path $root --import --quit *> (Join-Path $artifacts 'import.log')
    if ($LASTEXITCODE -ne 0) { throw 'Godot import failed; see artifacts/import.log' }
    $process = Start-Process -FilePath $godot -ArgumentList @('--path', ('"'+$root+'"'), 'res://main.tscn', '--position', '-16000,-16000', '--resolution', '1280x800', '--', '--qa') -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardError $stderr -RedirectStandardOutput $log -PassThru
    $qaHandle = $process.Handle # Keep the Windows handle until ExitCode is read (Windows PowerShell 5).
    # Bound only the newly created QA child; never touch other Godot/editor/device processes.
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); throw "JOSCRAFT QA timed out after $TimeoutSeconds seconds" }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "JOSCRAFT QA failed (exit $($process.ExitCode)); see $log and $stderr" }
    $result = Get-Content -LiteralPath (Join-Path $artifacts 'godot-qa.json') -Raw | ConvertFrom-Json
    if (!$result.pass) { throw 'QA report is not PASS' }
    if ((Get-Content -LiteralPath $stderr -Raw) -match '(?m)^(ERROR|SCRIPT ERROR):') { throw 'Godot reported runtime errors' }
    'JOSCRAFT import / animations / rendered-floor / collision / movement / screenshots PASS'
} finally { Pop-Location }

