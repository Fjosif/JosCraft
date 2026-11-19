$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest=Get-Content (Join-Path $root 'build\update.json') -Raw | ConvertFrom-Json
$zip=Join-Path $root 'build\JosCraft-windows-x64.zip'
if((Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant() -ne $manifest.Sha256){throw 'Package SHA mismatch'}
$fixture=Join-Path $root ('build\building-export-qa-'+[Guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $zip -DestinationPath $fixture
$payload=Join-Path $fixture ('versions\'+$manifest.Version)
$p=Start-Process -FilePath (Join-Path $payload 'JosCraft.exe') -WorkingDirectory $payload -ArgumentList @('--position','-16000,-16000','--resolution','1280x800','--','--building-qa') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $root 'artifacts\building-export.log') -RedirectStandardError (Join-Path $root 'artifacts\building-export.err.log')
$handle=$p.Handle
if(!$p.WaitForExit(60000)){$p.Kill();throw 'Owned exported building QA timed out'}
$report=Join-Path $payload 'artifacts\building-qa.json'
if($p.ExitCode -ne 0 -or !(Test-Path -LiteralPath $report)){throw 'Exported building QA failed; see artifacts/building-export logs'}
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if(!$result.pass){throw 'Exported report failed'}
$errors=Get-Content (Join-Path $root 'artifacts\building-export.err.log') -Raw
if($errors -match '(?m)^(ERROR|SCRIPT ERROR):'){throw 'Godot errors in exported building test'}
Copy-Item -LiteralPath $report -Destination (Join-Path $root 'artifacts\building-export-qa.json') -Force
@{pass=$true;version=$manifest.Version;sha256=$manifest.Sha256;checks=$result.checks.Count;fixture=$fixture;scope='Actual extracted Windows executable, real renderer and physics, no SDK/editor at runtime; all fixture writes inside copied build directory'} | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts\building-export-review.json') -Encoding utf8
'Exported JOSCRAFT building QA PASS: '+$result.checks.Count+' checks'
