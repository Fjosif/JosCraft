param()
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$delivery=Join-Path $root 'collaboration\incoming\antigravity-blocks-v1'
$fixture=Join-Path $root ('build\blocks-import-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $delivery 'exports') -Destination $fixture -Recurse
Copy-Item -LiteralPath (Join-Path $delivery 'block-models.json') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'block_import_qa.gd.txt') -Destination (Join-Path $fixture 'qa.gd')
@'
config_version=5
[application]
config/name="JOSCRAFT block import QA"
[rendering]
renderer/rendering_method="gl_compatibility"
'@ | Set-Content -LiteralPath (Join-Path $fixture 'project.godot') -Encoding utf8
$godot=(Get-Command Godot_v4.7.2-stable_mono_win64_console.exe).Source
& $godot --headless --editor --path $fixture --import --quit *> (Join-Path $root 'artifacts\blocks-import.log')
if($LASTEXITCODE -ne 0){throw 'Godot block import failed'}
& $godot --headless --path $fixture --script res://qa.gd -- (Join-Path $root 'artifacts\blocks-godot-review.json') *> (Join-Path $root 'artifacts\blocks-godot-qa.log')
if($LASTEXITCODE -ne 0){Get-Content (Join-Path $root 'artifacts\blocks-godot-qa.log');throw 'Godot block QA failed'}
$report=Get-Content (Join-Path $root 'artifacts\blocks-godot-review.json') -Raw | ConvertFrom-Json
if(!$report.pass){throw 'Godot review did not pass'}
'Godot block import QA PASS: '+$report.checks.Count+' checks'
