param([switch]$World,[switch]$Atelier)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$log = Join-Path $root $(if ($World) { 'artifacts\world-launcher.log' } else { 'artifacts\launcher.log' })
try {
    $godot = (Get-Command 'Godot_v4.7.2-stable_mono_win64.exe' -ErrorAction Stop).Source
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
    if (!(Test-Path -LiteralPath $dotnet)) { throw '.NET SDK není dostupné.' }
    # Only this project is built/imported. No process enumeration or termination.
    Push-Location -LiteralPath $root
    try {
        & $dotnet build (Join-Path $root 'Joscraft.csproj') *> $log
        if ($LASTEXITCODE -ne 0) { throw "C# build selhal. Podrobnosti: $log" }
        & $godot --headless --editor --path $root --import --quit *>> $log
        if ($LASTEXITCODE -ne 0) { throw "Import selhal. Podrobnosti: $log" }
        # The user deliberately launches this game window. The non-console Mono executable is used.
        $gameArgs = @('--path', ('"' + $root + '"'))
        # The game always opens its main menu. -World remains a compatible alias.
        if ($Atelier) { $gameArgs += 'res://main.tscn' }
        Start-Process -FilePath $godot -ArgumentList $gameArgs -WorkingDirectory $root
    } finally { Pop-Location }
} catch {
    $_ | Out-File -LiteralPath $log -Append
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("JOSCRAFT se nepodařilo spustit.`n$($_.Exception.Message)", 'JOSCRAFT') | Out-Null
}
