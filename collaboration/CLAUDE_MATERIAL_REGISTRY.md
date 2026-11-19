# Prompt pro Claude — další etapa: přesný katalog materiálů a značek

JOSCRAFT0.4.0 je vlastní Godot4.7.2 Mono / C# / net8 hra v D:\Projekty\Hradeti\JOSCRAFT. Tvoje předchozí LAN moduly už Codex integroval, opravil a spustil na skutečném Godotu. Nyní je NEPŘEPISUJ. Probíhá dokončení první verze a balíčku. Další práce je datový základ pro postupné zavedení všech bloků/předmětů podle oficiálních receptů Mojangu.

Čti aktuální `data/recipe-catalog.json`, `docs/recipe-catalog.md`, `docs/recipe-import.json`, `docs/material-roadmap.csv`, `runtime/RecipeCatalog.cs`, `runtime/BlockCatalog.cs`, `runtime/CatalogExtras.cs`, `runtime/Crafting.cs`, `runtime/Net/NetProtocol.cs` a `runtime/Net/WorldReplica.cs`. Katalog má1892receptů z oficiálních Bedrock samples na připnutém commitu. Současná hra má stabilní ID0–33 a21crafting variant včetně3výslovně uživatelem chtěných receptů1kmen→4klacíky. Ostatní základní recepty se čtou z dat, nikoli ručně.

Všechny výstupy zapisuj POUZE do:
D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\claude-material-registry

Neměň existující zdroje, saves, Git, release, běžící hru, zařízení, firewall ani původní DetiSkola. C# návrhy VŽDY jako *.cs.txt, protože .NET automaticky kompiluje i .cs v ignorovaných adresářích. Nezapisuj nové .cs do hlavního projektu. Zachovej současná číselná ID; nové číselné ID hromadně nepřiděluj.

Úkol:
1. Zpracuj všechny materiálové reference v katalogu a rozliš skutečný blok, předmět, ingredienční tag a nevyřešenou referenci. Tag není jeden vyrobitelný blok. Neztrácej metadata/data varianty, pokud jsou ve zdroji. Neoznačuj všechny materiály automaticky za bloky.
2. Podle aktuálních OFICIÁLNÍCH zdrojů Mojang/Microsoft dohledávej členství tagů a registry. Zaznamenej přesné URL, verzi/commit a původ každé klasifikace. Nevymýšlej členy tagů; co nelze doložit, explicitně označ unknown. Oficiální vanilla-data metadata lze použít, pokud jejich obsah/verzi skutečně ověříš. Nepřebírej cizí Minecraft art ani celý runtime.
3. Proveď audit tagů používaných již zavedenými předměty. Dříve chybějící `minecraft:stone_tool_materials` už byl v RecipeCatalog opraven; neměň ho zpět na pouhý stone_crafting_materials. Připrav automatickou kontrolu, která zjistí další podobná opomenutí při přidání materiálů.
4. Navrhni datový registr podle namespacedID, s kategorií, tvarem (`full_cube`, `slab`, `stairs`, `door`, `fence`, `chest`, `plant`, `fluid`, `item` atd.), základními vlastnostmi a závislostmi receptů. Chybějící grafika/funkce musí být explicitní stav; žádné předstírané hotové bloky.
5. Připrav priority zavádění: dřevěná sada, kámen/stavba, sklo/písek, základní rudy, jídlo/pěstování, tekutiny, další stanice. Priorita podle počtu odemčených receptů a nutných závislostí. První sadu neplných bloků modeluje Antigravity: dubová deska, schody, dveře, plot, truhla.
6. Navrhni malé read-only C# adaptéry a validační testy pro budoucí integraci. Upozorni na nynější wire typy U8: přes255ID je nutná vědomá změna naU16 a zvýšení protokolu; neprováděj ji teď. Současný save ukládá int, stávající ID0–33 musí zůstat čitelná.

Dodání do své složky:
- material-registry.json (verze, zdroje, entries, tag membership, explicitní unknown).
- material-audit.json (počty, pokrytí, všechny nevyřešené reference a metadata).
- implementation-order.csv a stručný PLAN.md s navazujícími sadami a důvodem pořadí.
- registry-schema.json a případné *.cs.txt návrhy.
- skutečné testy parseru/validace v oddělené tests/; nespoléhej na náhradní Godot stub jako důkaz runtime PASS.
- HANDOFF.md s přesnými cestami, ověřenými fakty, neověřeným a potřebnými integračními hooky.

Nekóduj novou hru ani druhý multiplayer. Neřeš grafiku a neměň základní herní pravidla. Tvoje data Codex po kontrole postupně připojí do skutečných bloků. Na konci oznam uživateli cestu HANDOFF.md.
