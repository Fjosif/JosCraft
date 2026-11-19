param([int]$TimeoutSeconds=180)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$live=Join-Path $root 'saves\slot-1.json'
$before=if(Test-Path -LiteralPath $live){(Get-FileHash -LiteralPath $live).Hash}else{'absent'}
$godot=(Get-Command Godot_v4.7.2-stable_mono_win64_console.exe).Source
Push-Location -LiteralPath $root
try {
    & 'C:\Program Files\dotnet\dotnet.exe' build Joscraft.csproj *> artifacts\survival-build.log
    if($LASTEXITCODE -ne 0){throw 'Build failed'}
    & $godot --headless --editor --path $root --import --quit *> artifacts\survival-import.log
    if($LASTEXITCODE -ne 0){throw 'Import failed'}
    $p=Start-Process -FilePath $godot -ArgumentList @('--path',('"'+$root+'"'),'res://world.tscn','--position','-16000,-16000','--resolution','1280x800','--','--survival-qa') -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput artifacts\survival-qa.log -RedirectStandardError artifacts\survival-qa.stderr.log
    $handle=$p.Handle
    if(!$p.WaitForExit($TimeoutSeconds*1000)){$p.Kill();throw 'Survival QA timed out'}
    $p.Refresh()
    if($p.ExitCode -ne 0){throw 'Survival QA failed; see artifacts/survival-qa.json and stderr'}
    $qa=Get-Content artifacts\survival-qa.json -Raw | ConvertFrom-Json
    if(!$qa.pass){throw 'Survival report failed'}
    if((Get-Content artifacts\survival-qa.stderr.log -Raw) -match '(?m)^(ERROR|SCRIPT ERROR):'){throw 'Runtime errors'}
    $after=if(Test-Path -LiteralPath $live){(Get-FileHash -LiteralPath $live).Hash}else{'absent'}
    if($before -ne $after){throw 'Live save changed during QA'}
    @{pass=$true;before=$before;after=$after}|ConvertTo-Json|Set-Content artifacts\survival-live-isolation.json -Encoding utf8
    'JOSCRAFT survival QA PASS'
} finally {Pop-Location}
