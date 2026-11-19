# Stromy, dřevo a listí

Implementace: `runtime/TreeGenerator.cs`, `BlockTerrain.cs`, `BlockCatalog.cs`. Všechny stromové buňky jsou skutečné voxely se stejným exposed-face mesherem a odpovídající kolizí jako půda a hráčovy stavby. Žádné dekorativní modely s nesouvisející kolizí.

## Deterministický plán

`TreeGenerator.Generate(seed, excluded)` vrací globální `Plan.Trees`, `Blocks` a `NaturalLeaves`. Základ je8×8jitterovaná síť s rozestupem12m, stabilní ID0…63 podle slotu; vyloučené sloty ostatní ID neposouvají. Seed určuje odchylku polohy, druh a výšku kmene. Část slotů se vynechá pro bezpečný start, hranice a případnou kolizi jiného stromu. Konkrétní počty/druhy dokládá runtime QA.

`TreeRecord` má `Id`, `Species`, `Root`, `LogType`, `LeavesType`, `Logs[]`, `Leaves[]`. Root je první log buňka nad půdou, tedy `Root.Y=BaseHeightAt(Root.X,Root.Z)`. Celý voxelový objem každé koruny je mimo bezpečný poloměr7m od startu `(0.5,0.5)`; kontroluje se nejbližší okraj buňky, nejen její střed. Stromy zůstávají v96×96mapě a Y≤24. Představují se tři vlastní siluety:

- Dub: nízký4–5blokový kmen a široká zaoblená koruna.
- Bříza: vysoký6–7blokový světlý kmen a úzká vysoko posazená koruna.
- Smrk:7–8blokový tmavý kmen, odstupňované spodní větve a špičatá kuželová koruna.

Plán vzniká globálně před chunky. Plochy i podpora listí tak používají stejnou buňku na obou stranách švu; unload nevytváří duplicitní stromy ani nezruší sousedův kmen. Přirozené stromy se neodstraňují po těžbě spodního logu. Zbývající bloky kmene je třeba vytěžit jednotlivě.

## Save a ochrana starších staveb

`BlockTerrain.Regenerate(seed, IEnumerable<int>? excludedTrees=null)` vytvoří stromový podklad se stabilní maskou. `TreeExclusions` vrací kopii masky. Nový svět používá prázdnou masku. Starý save, který pole ještě nemá, jednou zavolá `TreeGenerator.LegacyExclusions(seed, BlockEdit[], playerPosition)`. Migrace vynechá celý strom při překryvu libovolné staré editované buňky (včetně Air), při starém výkopu pod kořenem nebo při zásahu třímetrového vodorovného okolí uložené postavy. Maska se pak uloží se světem a při dalších běžných load ji už nelze přepočítávat z těžebních editací.

Přirozený strom je součást podkladu: při těžbě kmene/listí je jeho Air změna skutečně uložena, nesmí se z overlay smazat jen proto, že by na stejném místě byla v původním height-only terénu prázdná buňka. `GetChanges`/`RestoreChanges` i unload/reload zachovávají takové změny. `Regenerate` jako založení nového světa overlay naopak čistí. Diskové uložení a pickup/inventář obsluhuje herní relace.

## Podpora a odpadávání listí

Podpůrný výpočet je multisource BFS od všech skutečných logů (přirozených i hráčem položených). Postupuje pouze šestisměrnými sousedními leafy, nejvýše6kroků. Druh listí/logu nemusí být shodný. BFS vzorkuje globální `GetBlock`, i když sousední část právě nemá mesh. Podpora se přepočítává po editaci logu/listí či změně cesty; nikoli kompletním BFS pro každou jednotlivou buňku každý snímek.

Jen původně plánované a stále odpovídající přirozené listí bez podpory dostane časovač. Po6sekundách aktivního načteného času a stabilním seedovaném odstupu0…2s zmizí do Air. Po unloadu se nezralý čas odstraní a po load/RestoreChanges začne znovu; pauza zastaví Terrain proces. Již zmizelé listí je uložená editace. Ruční zničení listí trvá0,3s a žádný předmět nedává. Přirozené zmizení také nevytváří item.

Změny se dávkují nejvýše po8listech z jedné části za snímek a nejvýše3dotčené chunky se přestaví synchronně kvůli hranám. Streaming má přednost; listí a růst trávy si nekonkurují dvojím uploadem v témž snímku. Ve velké dávce nepodpořených korun může dokončení přidat další snímky nad nominálních6…8s; nejde o garantovaný přesný čas každého voxelového zmizení.

## Materiály a meze

ID: OakLog4, BirchLog5, SpruceLog6, OakLeaves7, BirchLeaves8, SpruceLeaves9. Kmeny jsou svislé, ruční těžba3s, každý vytěžený log znamená1kus příslušného druhu dřeva. Nevytvářejí se nekonečné zásoby. Boční plochy používají vlastní kůru druhu, horní/spodní vlastní společný řez. Tři samostatné foliage textury odlišují druhy i v koruně. Vlastní ImageGen zdroje a prompty jsou evidované v `source/textures/prompts-trees-v1.json`, bez převzatých Minecraft bitmap.

Listí je nyní neprůhledný pevný voxel, tedy žádná neviditelná kolize za průhledným obrazem. Import textur256px+mipy+VRAM a lineární filtrování odpovídají předchozím materiálům; počet materiálových skupin je11. Desktopové drawcalls/čas se musí změřit skutečně, nelze je přenášet na telefon. Nejsou implementované sazenice, jablka, crafting, natočení osy pokládaného kmene, fyzikální pád celého stromu ani úplná simulace Minecraftu.
