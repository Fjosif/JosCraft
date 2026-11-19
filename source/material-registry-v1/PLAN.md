# JOSCRAFT – plán zavádění materiálů

Pořadí je spočítané, ne odhadnuté. `tools/plan_order.py` v každém kroku vybere materiál, který zpřístupní nejvíc receptů. Recept se počítá jako zpřístupněný, když:
- jeho výsledek už hra zná,
- zná i všechny vstupy (vstupy s `data>0` nikdy),
- každá značka má aspoň jednoho známého člena,
- hra má aspoň jednu z jeho stanic.

Remízy rozhoduje výhled o jeden krok dopředu (řetězec ruda → ingot), potom připravenost (lze ho získat hned) a potom počet receptů, kterých se materiál účastní. Hra dnes zvládne 22 receptů z katalogu. Po všech sedmi sadách (194 materiálů) by jich bylo 228.

Úplný seznam je v `implementation-order.csv`: pořadí, sada, tvar s důkazem, počet nově zpřístupněných receptů, chybějící závislosti, potřebná vazba značky a nová stanice.

| Sada | Materiálů | Nových receptů | Celkem dostupných | Proč tady |
|---|---|---|---|---|
| 1 Dřevo | 41 | 48 | 70 | Kmeny, prkna, klacíky, stůl i sekery už hra má, takže nic dalšího nechybí. Začíná první neplnou sadou pro Antigravity. |
| 2 Kámen a stavba | 20 | 14 | 84 | Kámen, dlažba a cihly už ve hře jsou. Desky a další bloky potřebují kamenořez, proto jsou zde jen 0 a čekají na sadu 7. |
| 3 Sklo a písek | 14 | 11 | 95 | Písek je jediná nová surovina ze světa. Sklo a pískovec dělá už existující pec. |
| 4 Základní rudy | 51 | 63 | 158 | Nejvíc receptů na jeden krok: ingoty, nástroje, kbelík, nůžky. |
| 5 Jídlo a pěstování | 42 | 19 | 177 | Málo receptů, hodně herní logiky (růst, farmland). |
| 6 Tekutiny | 11 | 2 | 179 | Skoro nic neodemknou, ale potřebují nejvíc enginu. Voda a láva jsou připnuté na začátek sady. |
| 7 Stanice | 15 | 49 | 228 | Kamenořez sám zpřístupní 33 receptů, proto má sada 7 smysl zařadit hned po sadě 2, pokud bude kamenořez technicky snazší než rudy. |

## Sada 1 – první neplné bloky (Antigravity)
Pořadí je pevně dané zadáním: `minecraft:oak_slab` (deska), `minecraft:oak_stairs` (schody), `minecraft:wooden_door` (Bedrock ID dubových dveří, předmět s blokovou podobou dveří), `minecraft:oak_fence` (plot), `minecraft:chest` (truhla). Každý z nich má recept jen z prken a klacíků, takže je zahraje hned, jakmile existuje tvar a grafika.

Dál v sadě 1:
- loupané kmeny a dřevo (z kmene sekerou, mimo recepty),
- miska, dřevěná motyka a lopata,
- desky, schody, dveře, ploty, branky, cedule a lodě březové a smrkové,
- žebřík a sudy.

Sudy a kompostér potřebují vazbu značky `minecraft:wooden_slabs`; validátor ji vyžádá jako chybu, jakmile budou mít ID. Závěsné cedule čekají na `minecraft:chain`, které je v oficiálních receptech, ale v oficiálním registru na stejném commitu chybí (nevyřešená reference).

**Mezera ve zdroji:** Oficiální vzorky na připnutém commitu nemají recepty na:
- dubové, březové a smrkové poklopy (`minecraft:trapdoor`, `birch_trapdoor`, `spruce_trapdoor`),
- tlačítka (`wooden_button`, `birch_button`, `spruce_button`),
- nášlapné desky (`wooden_pressure_plate`, `birch_pressure_plate`, `spruce_pressure_plate`),
- dubovou ceduli (`oak_sign`; recept vyrábí legacy `minecraft:sign`),
- loďky s truhlou (`*_chest_boat`; recept vyrábí `minecraft:chest_boat` s `data`).

Jiné druhy dřeva recepty mají. Proto je neuvádím jako vyrobitelné. Buď je JOSCRAFT doplní jako výslovně označené vlastní recepty (jako 1 kmen → 4 klacíky), nebo počká na zdroj. Seznam je v `material-audit.json` → `recipeFamilyGaps`.

## Navazující pravidla
- **Nové stabilní ID:** dnes se smí přidat jen ID ≤ 255. Pro wire formát je ve `RegistryRuntimeAdapter` hlídač `EnsureWireCompatible`. Hromadné přidělení ID zde nedělám.
- **Kamenné desky a schody:** v Bedrocku jsou desky z kamene a dlažby jen přes kamenořez nebo přes legacy ID s `data`. Proto mají v CSV chybějící závislost `station:stonecutter`.
- **Železo:** ingot je v pořadí dřív než ruda, protože odemyká nejvíc receptů. Hratelné to bude až s rudou a surovým železem (také v sadě 4). Planner nezná drop z rudy (těžba není recept) a ukazuje to ve sloupci `missingDependencies`.
- **Značky:** `minecraft:logs` (táborák, udírna) a `minecraft:wooden_slabs` (sud, kompostér) zatím nemají runtime vazbu. Validátor je hlásí jako chybu ve chvíli, kdy dostane ID výsledek, který by jinak zůstal tiše nedostupný. Je to stejná třída chyby jako dřívější `stone_tool_materials`.
