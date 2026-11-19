$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$qa=Join-Path $root ('artifacts\update-qa-data\'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $qa -Force | Out-Null
$zip=Join-Path $root 'build\JosCraft-windows-x64.zip'
$manifest=Get-Content -LiteralPath (Join-Path $root 'build\update.json') -Raw | ConvertFrom-Json
$helper=Join-Path $root 'distribution\apply-update.ps1'
$results=@()
function Check($pass,$name){$script:results+=@{name=$name;pass=[bool]$pass};if(!$pass){throw $name}}
function Invoke-Helper($arguments,$log){
    $ErrorActionPreference='Continue'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $helper @arguments *> $log
    return $LASTEXITCODE
}
$install=Join-Path $qa 'install'
New-Item -ItemType Directory -Path $install -Force | Out-Null
$before=Get-FileHash -LiteralPath $zip -Algorithm SHA256
$code=Invoke-Helper @('-Root',$install,'-Zip',$zip,'-Version',$manifest.Version,'-Sha256',$manifest.Sha256,'-ValidateOnly') (Join-Path $qa 'valid.log')
Check ($code -eq 0) 'Real release archive passes SHA and path validation'
$code=Invoke-Helper @('-Root',$install,'-Zip',$zip,'-Version',$manifest.Version,'-Sha256',('0'*64),'-ValidateOnly') (Join-Path $qa 'bad-sha.log')
Check ($code -ne 0) 'Corrupted download rejected before install'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$attack=Join-Path $qa 'path-attack.zip'
$a=[IO.Compression.ZipFile]::Open($attack,'Create')
$entry=$a.CreateEntry('../escape.txt');$writer=New-Object IO.StreamWriter($entry.Open());$writer.Write('must never escape');$writer.Dispose();$a.Dispose()
$sha=(Get-FileHash -LiteralPath $attack -Algorithm SHA256).Hash
$code=Invoke-Helper @('-Root',$install,'-Zip',$attack,'-Version','0.4.1','-Sha256',$sha,'-ValidateOnly') (Join-Path $qa 'bad-path.log')
Check ($code -ne 0 -and !(Test-Path -LiteralPath (Join-Path $qa 'escape.txt'))) 'ZIP traversal rejected without extraction'
Copy-Item -LiteralPath (Join-Path $root 'distribution\launch.ps1'),(Join-Path $root 'distribution\JosCraft.vbs') -Destination $install
@{Version='0.3.9'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $install 'current.json') -Encoding utf8
New-Item -ItemType Directory -Force (Join-Path $install 'saves') | Out-Null
'saved-world-sentinel'|Set-Content -LiteralPath (Join-Path $install 'saves\world.txt') -Encoding utf8
$sentinel=(Get-FileHash -LiteralPath (Join-Path $install 'saves\world.txt')).Hash
$code=Invoke-Helper @('-Root',$install,'-Zip',$zip,'-Version',$manifest.Version,'-Sha256',$manifest.Sha256,'-Qa','-HealthTimeoutSeconds','20') (Join-Path $qa 'apply.log')
Check ($code -eq 0 -and (Get-Content -LiteralPath (Join-Path $install 'current.json') -Raw|ConvertFrom-Json).Version -eq $manifest.Version -and (Test-Path -LiteralPath (Join-Path $install ('healthy-'+$manifest.Version+'.txt')))) 'Actual exported game starts after atomic update and reports healthy'
Check ((Get-FileHash -LiteralPath (Join-Path $install 'saves\world.txt')).Hash -eq $sentinel) 'Update preserves saved-world sentinel'
# Start a valid headless payload under a different expected version. Its missing
# matching health acknowledgement simulates failed startup without native error dialogs.
$broken=Join-Path $qa 'broken';$brokenVersion='0.4.99'
New-Item -ItemType Directory -Force (Join-Path $broken ('versions\'+$brokenVersion))|Out-Null
Copy-Item -Path (Join-Path $install ('versions\'+$manifest.Version+'\*')) -Destination (Join-Path $broken ('versions\'+$brokenVersion)) -Recurse -Force
$brokenZip=Join-Path $qa 'broken.zip';Compress-Archive -Path (Join-Path $broken '*') -DestinationPath $brokenZip
$sha=(Get-FileHash -LiteralPath $brokenZip -Algorithm SHA256).Hash
$code=Invoke-Helper @('-Root',$install,'-Zip',$brokenZip,'-Version',$brokenVersion,'-Sha256',$sha,'-Qa','-HealthTimeoutSeconds','4') (Join-Path $qa 'rollback.log')
Check ($code -ne 0 -and (Get-Content -LiteralPath (Join-Path $install 'current.json') -Raw|ConvertFrom-Json).Version -eq $manifest.Version) 'Failed startup rolls pointer back to previous intact version'
Check ((Get-FileHash -LiteralPath (Join-Path $install 'saves\world.txt')).Hash -eq $sentinel) 'Rollback preserves saved-world sentinel'
@{pass=$true;utc=(Get-Date).ToUniversalTime().ToString('o');checks=$results;fixture=$qa;scope='Real Windows exported game startup and external update helper; isolated install, hash rejection, traversal rejection, automatic startup rollback. GitHub polling requires a published newer release.'}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $root 'artifacts\updater-qa.json') -Encoding utf8
'JOSCRAFT updater QA PASS'
