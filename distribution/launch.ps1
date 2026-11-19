param([switch]$Resume,[switch]$Qa)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
try {
    $current=Get-Content -LiteralPath (Join-Path $root 'current.json') -Raw | ConvertFrom-Json
    if ($current.Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Neplatná verze hry.' }
    $exe=Join-Path $root ('versions\'+$current.Version+'\JosCraft.exe')
    if (!(Test-Path -LiteralPath $exe)) { throw 'Chybí soubory hry. Obnov původní ZIP.' }
    if (!(Test-Path -LiteralPath ([IO.Path]::ChangeExtension($exe,'.pck'))) -or !(Test-Path -LiteralPath (Join-Path (Split-Path $exe) 'data_Joscraft_windows_x86_64'))) {throw 'Balíček není celý rozbalený. Rozbal celou složku ze ZIPu a spusť JosCraft.vbs.'}
    $arguments=@()
    if($Qa){$arguments+=@('--headless','--max-fps','60','--quit-after','720')}
    $arguments+=@('--','--install-root',('"'+$root+'"'))
    if($Qa){$arguments+='--update-qa'}
    if ($Resume) { $arguments+='--resume' }
    Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory (Split-Path $exe) -WindowStyle Normal
} catch {
    if($Qa){$_ | Out-File -LiteralPath (Join-Path $root 'qa-launch-error.log') -Append;exit 1}
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'JOSCRAFT') | Out-Null
}
