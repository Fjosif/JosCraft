# Materiálový registr – předání (Claude → Codex)

Datum 13. 11. 2026. Výstupy jsou jen v `collaboration/incoming/claude-material-registry/`. Žádný zdroj projektu, save, Git, release ani běžící hra nebyly změněny. Nové číselné ID nejsou přidělené. C# je jen jako `*.cs.txt`. Moduly LAN jsem nečetl kvůli úpravám, jen `NetProtocol.cs` a `WorldReplica.cs` kvůli šířce wire typů.

## Soubory
| Cesta | Obsah |
|---|---|
| `material-registry.json` | 1286 záznamů a 15 značek; zdroje se SHA/commitem/URL; klasifikační pravidla; `runtime.stableIds` (1–33) a `runtime.tagBindings` |
| `material-audit.json` | počty, pokrytí, audit stabilních ID a vazeb značek, 90 nevyřešených referencí nebo referencí s metadaty, věrnost katalogu vůči zdroji, mezery v receptech, srovnání s `material-roadmap.csv` |
| `implementation-order.csv` | 194 materiálů v 7 sadách s důvodem pořadí |
| `PLAN.md` | stručný plán a odůvodnění |
| `registry-schema.json` | JSON Schema 2020‑12 registru |
| `src/MaterialRegistry.cs.txt` | parser a model (jen System.Text.Json, bez Godotu) se strukturálními kontrolami |
| `src/MaterialValidator.cs.txt` | kontroly: zamrzlá ID 1–33, U8 přetečení, nevázané značky (třída chyby `stone_tool_materials`), členové značek, `data` vstupy |
| `src/RegistryRuntimeAdapter.cs.txt` | read-only most na dnešní `RecipeCatalog.Type/Ingredient/IngredientMatches` + `EnsureWireCompatible` |
| `tests/RegistryTests.cs.txt`, `RegistryTests.csproj.txt`, `run-tests.sh` | skutečné testy nad skutečnými daty |
| `tools/generate_registry.py`, `plan_order.py`, `validate_schema.py` | reprodukovatelné generování, plán a kontrola schématu |

## Ověřená fakta (spuštěno 13. 11. 2026, Linux, .NET 8.0.134, Python 3)
- `data/recipe-catalog.json` má SHA‑256 `4f89bdfb…57e6f8`, shodně s `docs/recipe-import.json`. Všech 1892 souborů odpovídá 1:1 souborům `behavior_pack/recipes` na commitu `15701fa9c13923f12662984c32c124327e40e3d3` (verze 1.26.60.5).
- Oficiální registry na stejném commitu: `metadata/vanilladata_modules/mojang-items.json` (1627 položek) a `mojang-blocks.json` (1466 bloků s vlastnostmi). Balíček npm `@minecraft/vanilla-data@1.26.60` (Mojang/Microsoft, integrity v registru) obsahuje jen ID typů, **žádné členství ve značkách**.
- **Členství ve značkách Bedrocku Mojang/Microsoft souhrnně nepublikuje.** Microsoft Learn „Tags for Recipes“ uvádí jen názvy značek (aktualizováno 2024‑08‑15, „k verzi 1.19.50“). Oficiálně doložené je jen to, co nesou vanilla JSON v `behavior_pack/items|blocks` (např. `shelf_mushroom` → `minecraft:mushrooms_for_stew`). Proto mají v registru `officialMembers: "unknown"` všechny značky kromě `minecraft:mushrooms_for_stew`. U té je seznam jen doložená podmnožina.
- Komunitní tabulka Bedrock‑OSS (commit `0930b394…`, „Preview 1.26.70.21“) je uložená zvlášť jako `communityCandidates` s `communityAuthoritative: false`. Parser odmítne dokument, který by ji označil za autoritativní. Používá se jen k varováním a plánu, nikdy k označení receptu jako dostupného.
- Audit vazeb: dnešní runtime váže `planks` (10–12), `coals` (19, 33) a `stone_crafting_materials` i `stone_tool_materials` (14). **Oprava `stone_tool_materials` zůstává.** Test ji simulovaně odstraní a validátor vrátí chybu `UNBOUND_TAG` u `stone_pickaxe.json`. Na současném stavu: 0 chyb, 0 varování, 4 informace „oficiální členství neznámé“.
- Klasifikace: 887 bloků, 314 předmětů, 8 jen-bloků, 48 pseudo ID (`potion_type:*`), 29 nevyřešených. Tag není blok. Předmět se stejnojmenným blokem (dveře, postel, pšenice) je `item` s `blockForm.placesBlock: "unknown"`.
- Tvar: 332× z oficiálních vlastností bloku, 116× podle jména, 439× `default_assumption` (výslovně nízká jistota, např. `full_cube`).
- Metadata zachovaná v `dataVariants` (560 odkazů, 87 ID), včetně legacy řetězců `minecraft:coal:1`, `wood:N`, `log:N`, `log2:N` a camelCase `minecraft:muttonCooked/Raw`. Nic není sloučené do základního ID. Při normalizaci se ztratilo `unlock` (1601 receptů), `group` (399) a `format_version`. Je to zapsané v auditu, včetně příkladů.
- `run-tests.sh` na kopii mimo projekt: schéma 0 chyb, **102/102 testů PASS**. Generátor je reprodukovatelný (bajtově shodný výstup). Test runneru: úmyslně pokažená aserce dá `failed=1, exit 1`.

## Neověřeno / omezení
- Nic neběželo v Godotu a nic není zapojené do hry. Testy ověřují parser, validaci a adaptér nad daty, ne runtime hry.
- Kategorie (`categoryEvidence: name_pattern`) a část tvarů jsou heuristiky, označené jako takové.
- Planner nezná dropy z těžby (ruda → surovina) ani nerozhoduje o grafice. `obtainedWithoutRecipe=yes` znamená jen „žádný recept to nevyrábí“.
- Oficiální vzorky nemají recepty na dubové, březové a smrkové poklopy, tlačítka a nášlapné desky, `oak_sign` ani `*_chest_boat` (viz `recipeFamilyGaps`). Nedoplnil jsem je.
- `minecraft:chain` (závěsné cedule) je v receptech, ale ne v oficiálním registru stejného commitu.
- Generátor čte vazby značek z textu `RecipeCatalog.cs` a skončí chybou, pokud se ten text změní. Je to záměrná pojistka; po hooku H3 odpadne.

## Integrační hooky (až po kontrole, postupně)
- **H1 Data:** zkopírovat `material-registry.json` do `data/` (jen čtení). Hra ho zatím nepotřebuje.
- **H2 RecipeCatalog:** `Type()`, `Ingredient()` pro značky a `IngredientMatches()` delegovat na `RegistryRuntimeAdapter.StableIdOf/TagCode/CodeAccepts`. Testy dokazují shodné odpovědi pro ID 1–33 a kódy −1/−2/−3, včetně `minecraft:coal:1 ≠ coal` a `minecraft:stone:0 = stone`. Adaptér načíst přes `FileAccess.GetFileAsString("res://data/material-registry.json")` a `MaterialRegistry.Parse`.
- **H3 Vazby značek jako data:** `runtime.tagBindings` je zdroj pravdy. Novou vazbu (`logs`, `wooden_slabs`, …) přidávat tam, ne do `switch`.
- **H4 QA brána:** `run-tests.sh` (nebo `MaterialValidator.Validate`) spouštět ve `tools/verify_*.ps1` mimo Godot. Pravidlo: žádná `Error`.
- **H5 Přidání materiálu:** 1) `BlockType` + `RecipeCatalog` ID (≤ 255), 2) stav v registru (`stableId`, `graphics: own_v1`, `function: implemented`, jinak parser odmítne), 3) znovu `generate_registry.py`, 4) validátor. Při chybě `UNBOUND_TAG` doplnit vazbu.
- **H6 Wire nad 255 (teď NEdělat):** `NetWriter.Stack`, `WorldCodec.BlockChanged`, `SnapEdits`, `PickupDto.Type` a čtení v `SnapshotAssembler`/`NetClient.ReadInventory` jsou dnes U8. Přechod znamená U16 na všech těchto místech + `NetProtocol.ProtocolVersion++`. Save ukládá `int`, takže ID 0–33 zůstanou čitelná beze změny. Kontroly `GameSession.Read` (`BlockCatalog.IsValid/IsBlock`) bude potřeba rozšířit s každým novým ID.

## Jak to zopakovat
```
cp -r collaboration/incoming/claude-material-registry /tmp/mr   # MIMO projekt
bash /tmp/mr/tests/run-tests.sh D:/Projekty/Hradeti/JOSCRAFT      # potřebuje dotnet 8 a python3 + jsonschema
python3 tools/generate_registry.py --catalog data/recipe-catalog.json --runtime runtime/RecipeCatalog.cs --roadmap docs/material-roadmap.csv \
  --samples <bedrock-samples@15701fa9> --community <bedrock-wiki@0930b394>/docs/public/assets/tables/items/vanilla-item-tags/tags.json --out <dir>
```
