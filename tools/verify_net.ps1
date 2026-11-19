# JOSCRAFT multiplayer – two-or-more real process QA (návrh; spustit AŽ PO integraci Codexem).
# Spouští jen vlastní QA instance. Nic neukončuje, nemění firewall ani router, nepíše do saves/.
# Host save jde díky GameSession.IsQa do artifacts/game-qa-data/session.json.
param([int]$Port = 24601, [int]$HostSeconds = 100)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))   # collaboration\incoming\claude-multiplayer -> projekt
$out  = Join-Path $root 'artifacts\net-qa'
$live=Join-Path $root 'saves\slot-1.json'
$liveBefore=if(Test-Path -LiteralPath $live){(Get-FileHash -LiteralPath $live).Hash}else{'absent'}
New-Item -ItemType Directory -Force -Path $out | Out-Null

$godot = (Get-Command 'Godot_v4.7.2-stable_mono_win64_console.exe' -ErrorAction Stop).Source

function Start-Role([string]$name, [string[]]$userArgs) {
    $log = Join-Path $out "$name.log"; $err = Join-Path $out "$name.err.log"
    $args = @('--path', ('"' + $root + '"'), '--position', '-16000,-16000', '--resolution', '960x600', '--') + $userArgs
    $process = Start-Process -FilePath $godot -ArgumentList $args -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError $err -PassThru
    $handle=$process.Handle
    return $process
}
function Wait-Role($p, [int]$seconds) { if (-not $p.WaitForExit($seconds * 1000)) { Write-Warning "$($p.Id) neskončil včas (nechávám běžet, nic neukončuji)"; return $null }; return $p.ExitCode }

& 'C:\Program Files\dotnet\dotnet.exe' build (Join-Path $root 'Joscraft.csproj') *> (Join-Path $out 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Net build failed' }
& $godot --headless --editor --path $root --import --quit *> (Join-Path $out 'import.log')

$hostP = Start-Role 'host' @("--net-qa=host", "--net-qa-port=$Port", "--net-qa-seconds=$HostSeconds")
Start-Sleep -Seconds 8
$a = Start-Role 'joinA' @("--net-qa=join", "--net-qa-port=$Port", "--net-profile=A")
$b = Start-Role 'joinB' @("--net-qa=join", "--net-qa-port=$Port", "--net-profile=B")
$codeA = Wait-Role $a 40; $codeB = Wait-Role $b 40
$a2 = Start-Role 'joinA2' @("--net-qa=join", "--net-qa-port=$Port", "--net-profile=A", "--net-qa-tag=again")   # reconnect se stejnou identitou
$codeA2 = Wait-Role $a2 40
$bad = Start-Role 'badversion' @("--net-qa=badversion", "--net-qa-port=$Port", "--net-qa-version=qa-old", "--net-profile=C")
$codeBad = Wait-Role $bad 20
$gar = Start-Role 'garbage' @("--net-qa=garbage", "--net-qa-port=$Port", "--net-profile=D")
$codeGar = Wait-Role $gar 20
$codeHost = Wait-Role $hostP ($HostSeconds + 30)

function J([string]$f) { $p = Join-Path $out $f; if (Test-Path $p) { Get-Content $p -Raw | ConvertFrom-Json } else { $null } }
$h = J 'host.json'; $hf = J 'host-final.json'; $ja = J 'join-A.json'; $jb = J 'join-B.json'; $ja2 = J 'join-A-again.json'
$bv = J 'badversion-C.json'; $gb = J 'garbage-D.json'
$checks = [ordered]@{
  'shaped snapshot includes rotations' = ($ja -ne $null -and $ja.shapeSnapshot -and $jb.shapeSnapshot)
  'host save does not mutate remote cursor' = ($ja -ne $null -and $ja.autosavePreservesCarried)
  'door toggles replicate from client' = ($ja -ne $null -and $ja.doorToggleReplicates)
  'remote chest opens and stores authoritative items' = ($ja -ne $null -and $ja.chestOpensRemotely -and $ja.chestStoresOnHost -and $ja.chestReopenRetainsContents -and $ja.chestWithdrawPreservesItems)
  'host běžel a uložil do artifacts'           = ($hf -ne $null -and $hf.saved -and $hf.savePath -like '*artifacts*')
  'A: snímek již upraveného světa'               = ($ja -ne $null -and $ja.editsAfterJoin -ge 24 -and $ja.pickupsAfterJoin -ge 3)
  'A: těžba odebrala blok a dala 1 kus'          = ($ja -ne $null -and $ja.mineRemovedBlock -and $ja.mineGaveOne -ge 1)
  'A: vzdálená těžba odmítnuta'                  = ($ja -ne $null -and $ja.farMineRefused)
  'A: okamžitá těžba bez startu odmítnuta'       = ($ja -ne $null -and $ja.instantMineRefused)
  'A: stavění ze špatného slotu odmítnuto'       = ($ja -ne $null -and $ja.wrongSlotPlaceRefused)
  'B: připojen současně, vlastní slot'           = ($jb -ne $null -and $jb.slot -ne $ja.slot)
  'A znovu: stejný batoh po reconnectu'          = ($ja2 -ne $null -and $ja2.itemsAfterJoin -eq $ja.itemsEnd)
  'jiná verze: čitelné odmítnutí'                = ($bv -ne $null -and $bv.rejected -and $bv.message -match 'verzi')
  'neplatná data před ověřením: odmítnuto'       = ($gb -ne $null -and $gb.rejected)
  'host save obsahuje vzdálené hráče'            = ($hf -ne $null -and $hf.remotePlayersInSave -ge 2)
  'authoritative placement consumes once' = ($ja -ne $null -and $ja.legitPlaceConsumesOnce)
  'remote avatars render on actual physics floor' = ($ja -ne $null -and $ja.renderedRemotePlayers -ge 1 -and $ja.remoteFeetOnPhysics)
}
$liveAfter=if(Test-Path -LiteralPath $live){(Get-FileHash -LiteralPath $live).Hash}else{'absent'}
$checks['live singleplayer save unchanged']=($liveBefore -eq $liveAfter)
$errors=@(Get-ChildItem -LiteralPath $out -Filter *.err.log | Where-Object {(Get-Content -LiteralPath $_.FullName -Raw) -match '(?m)^ERROR:'})
$checks['no Godot runtime errors']=($errors.Count -eq 0)
$checks.GetEnumerator() | ForEach-Object { '{0,-6} {1}' -f ($(if ($_.Value) { 'OK' } else { 'CHYBA' }), $_.Key) }
'Pozn.: digest klienta po připojení porovnej ručně s host.json/timeline (digest se mění s každou živou změnou).'
@{pass=!($checks.Values -contains $false);checks=$checks;liveBefore=$liveBefore;liveAfter=$liveAfter;utc=(Get-Date).ToUniversalTime().ToString('o')} | ConvertTo-Json -Depth 5 | Out-File -LiteralPath (Join-Path $out 'summary.json') -Encoding utf8
if ($checks.Values -contains $false) { exit 1 }
