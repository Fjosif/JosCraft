$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$target=Join-Path $root 'artifacts\delivery-manifest.json'
$reports=@('player-sizing-review.json','building-qa.json','building-export-review.json','blocks-blender-backup.json','material-registry-review.json','survival-qa.json','net-qa/summary.json','updater-qa.json','survival-blender-backup.json')
$tests=@($reports|ForEach-Object{
    $r=Get-Content -LiteralPath (Join-Path $root ('artifacts/'+$_)) -Raw|ConvertFrom-Json
    if($r.pass -ne $true){throw "Report not PASS: $_"}
    @{report='artifacts/'+$_;pass=$true}
})
$files=@(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
    $_.FullName -ne $target -and $_.FullName -notmatch '\\(\.git|\.godot|bin|obj|build|__pycache__|saves|game-qa-data|grass-qa-data|tree-qa-data|survival-qa-data|building-qa-data|update-qa-data)\\' -and $_.FullName -notmatch '\\collaboration\\incoming\\|\\net-qa\\profiles\\' -and $_.Extension -notin '.blend1','.blend2'
}|Sort-Object FullName|ForEach-Object{
    @{path=$_.FullName.Substring($root.Length+1).Replace('\','/');bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$survival=Get-Content -LiteralPath (Join-Path $root 'artifacts\survival-qa.json') -Raw|ConvertFrom-Json
$package=Get-Content -LiteralPath (Join-Path $root 'build\update.json') -Raw|ConvertFrom-Json
[ordered]@{
    project='JOSCRAFT';version=$package.Version;stage='restored-original-character-proportions';utc=(Get-Date).ToUniversalTime().ToString('o')
    tests=$tests
    sourceProjectReadOnly='D:/Projekty/Hradeti/DetiSkola'
    launch='SPUSTIT_JOSCRAFT.vbs';standalone='build/JosCraft-windows-x64.zip';package=$package
    handoff='artifacts/owned-handoff.md';features='docs/player-sizing-v0.4.3.md';updates='docs/automatic-updates.md'
    officialRecipeCatalogue=$survival.officialCatalogue;currentlyCraftableRecipes=$survival.craftableRecipes;recipeSource='docs/recipe-import.json';materialsRoadmap='docs/material-roadmap.csv'
    textureMode='built-in image_gen; original atlases preserved; no CLI fallback';newTexturePrompts='source/textures/prompts-survival-v1.json';textures='assets/textures'
    models='source/creatures-manifest.json';blenderBackup='artifacts/survival-blender-backup.json';catStatus='Model/source prepared; not yet integrated into gameplay'
    externalContributions=@('Antigravity creature models/rigs/animations','Claude initial LAN modules; integrated and fixed against real Godot API','Antigravity five shaped block models; live-game integration and physics checked by Codex','Claude material registry; 102 baseline tests and schema checked, runtime adapter deferred')
    buildingModels='source/blocks-v1/block-models.json';buildingPreview='artifacts/building-placed-blocks.png';chestPreview='artifacts/building-chest-inventory.png'
    preview='artifacts/survival-recipe-catalogue.png';firelightPreview='artifacts/survival-firelight-night.png'
    limitations=@('Finite 96x96 world; no endless generation yet','Lower slabs only; no double chests or corner stairs yet; fence geometry and collision are1m high','Only introduced materials/stations are craftable; full1892catalogue is reference data','Windows auto-update; no Android/iOS export','LAN tested in separate real processes on one PC; no physical second device, WAN or rocketweb.cz service','No water/lava, hunger or tool durability yet','No wall torches; nearest6lights active','Client movement has plausibility checks, not full server physics')
    priorStage='artifacts/pre-survival-owned-handoff.md'
    files=$files
}|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $target -Encoding utf8
"Wrote $($files.Count) verified file records; package SHA256 $($package.Sha256)"
