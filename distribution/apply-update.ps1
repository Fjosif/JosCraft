param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][string]$Zip,[Parameter(Mandatory=$true)][string]$Version,[Parameter(Mandatory=$true)][string]$Sha256,[int]$WaitPid=0,[switch]$Resume,[switch]$ValidateOnly,[switch]$Qa,[int]$HealthTimeoutSeconds=45)
$ErrorActionPreference='Stop'
$script:pointerChanged=$false
$script:previousVersion=''
trap {
    $problem=$_
    try{if($script:newGame -and !$script:newGame.HasExited){$script:newGame.Kill();$script:newGame.WaitForExit(5000)|Out-Null}}catch{}
    if(!$ValidateOnly -and $rootPath -and (Test-Path -LiteralPath (Join-Path $rootPath 'launch.ps1'))) {
        try {
            if($script:pointerChanged -and $script:previousVersion) {
                $pointer=Join-Path $rootPath 'current.json'
                @{Version=$script:previousVersion}|ConvertTo-Json|Set-Content -LiteralPath ($pointer+'.recover') -Encoding utf8
                [IO.File]::Replace($pointer+'.recover',$pointer,$pointer+'.failed')
            }
            if($Version -match '^\d+\.\d+\.\d+(\.\d+)?$'){$problem|Out-File -LiteralPath (Join-Path $rootPath ('failed-'+$Version+'.txt')) -Append}
            $stillRunning=$WaitPid -gt 0 -and (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue)
            if(!$stillRunning){
                $fallback=@('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',('"'+(Join-Path $rootPath 'launch.ps1')+'"'))
                if($Resume){$fallback+='-Resume'};if($Qa){$fallback+='-Qa'}
                Start-Process powershell.exe -ArgumentList $fallback -WindowStyle Hidden
            }
        } catch {}
    }
    $problem | Write-Error -ErrorAction Continue
    exit 1
}
$rootPath=[IO.Path]::GetFullPath($Root)
$zipPath=[IO.Path]::GetFullPath($Zip)
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Invalid update version' }
if ($Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ne $Sha256) { throw 'Invalid update checksum' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $total=0L;$seen=@{}
    foreach($entry in $archive.Entries) {
        $name=$entry.FullName.Replace('\','/')
        if ($name.StartsWith('/') -or $name.Contains(':') -or $name.Split('/') -contains '..' -or $seen.ContainsKey($name.ToLowerInvariant())) { throw 'Unsafe update path' }
        $seen[$name.ToLowerInvariant()]=$true
        if (($entry.ExternalAttributes -shr 16 -band 0xF000) -eq 0xA000) { throw 'Symlink in update' }
        $total+=$entry.Length
        if ($total -gt 3000000000 -or $archive.Entries.Count -gt 20000) { throw 'Update too large' }
    }
    if (!$seen.ContainsKey(('versions/'+$Version+'/joscraft.exe').ToLowerInvariant())) { throw 'Missing game executable' }
} finally { $archive.Dispose() }
if ($ValidateOnly) { 'Update archive validation PASS'; exit 0 }
if ($WaitPid -gt 0) { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction SilentlyContinue }
if ($WaitPid -gt 0 -and (Get-Process -Id $WaitPid -ErrorAction SilentlyContinue)) { throw 'Game still running; update postponed' }
$staging=Join-Path $rootPath ('downloads\stage-'+[Guid]::NewGuid().ToString('N'))
$stageFull=[IO.Path]::GetFullPath($staging)
if (!$stageFull.StartsWith($rootPath+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging directory' }
New-Item -ItemType Directory -Path $stageFull -Force | Out-Null
$old=Get-Content -LiteralPath (Join-Path $rootPath 'current.json') -Raw | ConvertFrom-Json
$script:previousVersion=$old.Version
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath,$stageFull)
    $payload=Join-Path $stageFull ('versions\'+$Version)
    $target=Join-Path $rootPath ('versions\'+$Version)
    $targetFull=[IO.Path]::GetFullPath($target)
    if (!$targetFull.StartsWith((Join-Path $rootPath 'versions')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe version directory' }
    New-Item -ItemType Directory -Path (Split-Path $targetFull) -Force | Out-Null
    # Never delete/overwrite a previous version. Interrupted staging is harmless.
    if (Test-Path -LiteralPath $targetFull) { throw 'Version already exists; update postponed' }
    Move-Item -LiteralPath $payload -Destination $targetFull
    $pointer=Join-Path $rootPath 'current.json';$temp=$pointer+'.tmp'
    @{Version=$Version} | ConvertTo-Json | Set-Content -LiteralPath $temp -Encoding utf8
    [IO.File]::Replace($temp,$pointer,$pointer+'.bak')
    $script:pointerChanged=$true
    $gameArgs=@()
    if($Qa){$gameArgs+=@('--headless','--max-fps','60','--quit-after','720')}
    $gameArgs+=@('--','--install-root',('"'+$rootPath+'"'))
    if($Qa){$gameArgs+='--update-qa'}
    if($Resume){$gameArgs+='--resume'}
    $script:newGame=Start-Process -FilePath (Join-Path $targetFull 'JosCraft.exe') -ArgumentList $gameArgs -WorkingDirectory $targetFull -WindowStyle $(if($Qa){'Hidden'}else{'Normal'}) -PassThru
    $gameHandle=$script:newGame.Handle
    $healthy=Join-Path $rootPath ('healthy-'+$Version+'.txt')
    $deadline=(Get-Date).AddSeconds([Math]::Max(2,$HealthTimeoutSeconds))
    while((Get-Date) -lt $deadline -and !(Test-Path -LiteralPath $healthy) -and !$script:newGame.HasExited){Start-Sleep -Milliseconds 500}
    if(!(Test-Path -LiteralPath $healthy)) {
        if(!$script:newGame.HasExited){$script:newGame.Kill();$script:newGame.WaitForExit(5000)|Out-Null}
        @{Version=$old.Version} | ConvertTo-Json | Set-Content -LiteralPath ($pointer+'.rollback') -Encoding utf8
        [IO.File]::Replace($pointer+'.rollback',$pointer,$pointer+'.failed')
        $script:pointerChanged=$false
        throw 'New version failed startup; previous version restored'
    }
} catch {
    $_ | Out-File -LiteralPath (Join-Path $rootPath 'update-error.log') -Append
    throw
}
