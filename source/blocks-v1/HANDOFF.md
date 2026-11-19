# Handoff — JOSCRAFT Non-Full Blocks v1 (`antigravity-blocks-v1`)

## 1. Dodané soubory
Všechny výstupy jsou uloženy výhradně v `D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-blocks-v1\`:

- **Zdroje (Blender 5.2.2 LTS + reprodukovatelný generátor + vlastní textury):**
  - `source/generate_blocks.py`
  - `source/blocks-v1.blend`
  - `source/textures/oak-planks-v1.png` (kopie z `assets/textures/`)
  - `source/textures/oak-bark-v1.png` (kopie z `assets/textures/`)
- **Exporty pro Godot 4.7.2 Mono (GLB se zabalenými texturami, bez externích cest):**
  - `exports/oak-slab.glb` (`44` trojúhelníků)
  - `exports/oak-stairs.glb` (`68` trojúhelníků, celistvý L-manifold bez vnitřních přepážek)
  - `exports/oak-door.glb` (`440` trojúhelníků, poduzel `DoorHinge` vlevo, výchozí klidový stav **ZAVŘENO** `0°`, animace `Open` `0° → 90°` a `Close` `90° → 0°`, SHA256 `8ddc31870d2034c0777b160f4e7f18a6ae0f3c5eebdf58b81d15dac9397feb91`)
  - `exports/oak-fence.glb` (`396` trojúhelníků celkem; poduzly `Post`, `North`, `South`, `East`, `West`, výška geometrie `1.0 m`, SHA256 `f4602b56abd86d4806aeb4a36b49f65f5924ca20402fb05d69f60309db388703`)
  - `exports/wooden-chest.glb` (`308` trojúhelníků, `ChestBase` + `LidHinge`/`ChestLid`, výchozí klidový stav **ZAVŘENO** `0°`, animace `Open` `0° → 90°` a `Close` `90° → 0°`, SHA256 `0feb3905cd998c904310a633eaffe42cf16f1d62730b23112ab1c5b735a4d8a9`)
- **Náhledy (`previews/`):**
  - `*-front.png`, `*-side.png`, `*-top.png` pro všech 5 bloků (dveře a truhla v zavřeném stavu) + `oak-door-open.png` a `wooden-chest-open.png`.
- **Metadata a kolize:**
  - `block-models.json` (namespaced ID, rozměry, Godot bounds pro klidový/zavřený stav, otevřený stav i celý průběh animace, anchor/support, occupancy/clearance cells, poduzly, animace, SHA256)
  - `collision-boxes.json` (složené AABB boxy `center` / `size` v Godot souřadnicích; `ChestBase` a `ChestLid` zůstávají oddělené, všechny vrcholy včetně kování jsou pokryty bez jediné velké kostky).

## 2. Co je reálně ověřené (binární GLB kontrola + Blender 5.2.2 + `tools\qa_block_delivery.py`)
- **Metoda měření:** Přímé vyhodnocení binárních chunků exportovaných `.glb` souborů (`nodes`, `POSITION` accessors a klíčové snímky `Open`/`Close` přes celý průběh animace) doplněné o reimport v Blenderu 5.2.2 LTS pro počty trojúhelníků, materiály a náhledy. **Nejedná se o test živé fyziky v Godotu.**
- **Výchozí klidový stav (rest == konec `Close`):**
  - `oak-door`: načtení GLB bez přehrání animace odpovídá zavřenému stavu (`min [-0.5, 0.0, -0.5]`, `max [0.5, 2.0, -0.3125]`), `rest_matches_close_end: true`. Kování je zarovnáno do celkové tloušťky `0.1875 m` (`0.0 m` přesah do `-X` v zavřeném i otevřeném stavu).
  - `wooden-chest`: načtení GLB bez přehrání animace odpovídá zavřenému stavu (`min [-0.445, 0.0, -0.4775]`, `max [0.445, 0.8825, 0.4375]`), `rest_matches_close_end: true`.
- **Koncové stavy `Open` a celý průběh animace (`sweep`):**
  - `oak-door` (`Open` `0° → 90°` přes `DoorHinge` na `(-0.40625, 0, -0.40625)`):
    - Otevřený stav: `min [-0.5, 0.0, -0.5]`, `max [-0.3125, 2.0, 0.5]` (čistě uvnitř `[0, 0, 0]` a `[0, 1, 0]`).
    - Plný průběh otáčení (`sweep`): `min [-0.526, 0.0, -0.526]`, `max [0.5027, 2.0, 0.5027]` — během rotace roh pantu krátce zasahuje ~`0.026 m` do `-X`/`-Z` a hrana kliky ~`0.0027 m` do `+X`/`+Z`. `clearance_cells` v `block-models.json` proto explicitně uvádí `[-1, 0, 0], [-1, 1, 0], [0, 0, -1], [0, 1, -1], [0, 0, 1], [0, 1, 1]` s vysvětlením v `clearance_note`.
  - `wooden-chest` (`Open` `0° → 90°` přes `LidHinge` na `(0, 0.5625, +0.4375)`):
    - Otevřený stav: `min [-0.445, 0.0, -0.4375]`, `max [0.445, 1.4775, 0.7575]`.
    - Plný průběh otáčení (`sweep`): `min [-0.445, 0.0, -0.4811]`, `max [0.445, 1.4871, 0.7575]`.
    - `clearance_cells`: `[[0, 1, 0], [0, 0, 1], [0, 1, 1]]` (horní buňka + zadní `+Z` buňka + horní-zadní buňka, protože otevřené víko zasahuje do `z = +0.7575 m` a `y = +1.4775 m`).
- **Kolizní pokrytí (`qa_block_delivery.py`):**
  - `collision_uncovered_vertices: 0` pro všechny modely a všechny stavy (`oak-slab`, `oak-stairs`, `oak-door` closed/open, `oak-fence`, `wooden-chest` closed/open).
  - `wooden-chest` má oddělené boxy `ChestBase` (`width_x = 0.89`) a `ChestLid` / `ChestLid_Open` (včetně `0.89 m` pásků a přední západky).
- **Plot (`oak-fence`):**
  - Výška geometrie sloupku je výslovně `1.0 m` (bez tvrzení o nepřeskočitelnosti bez budoucí herní implementace).
- **Poznámka k výstupu `tools\qa_block_delivery.py`:**
  - Skript hlásí `binary_checks_pass: true` a nulové neshody v bounds i kolizích; jediná zbývající řádka v `issues` je statická připomínka skriptu na `wooden-chest/Open` (`max_z > 0.5`), což je nyní plně zdokumentováno v `clearance_cells: [[0, 1, 0], [0, 0, 1], [0, 1, 1]]` a `clearance_note`.

## 3. Co musí Codex ověřit v Godotu 4.7.2 Mono
- **Živá herní fyzika, kolize a integrace nebyly v Godotu spouštěny** — soubory zůstávají pouze v `collaboration/incoming/antigravity-blocks-v1/`.
- Codex při integraci prověří:
  1. Načtení zabalených PBR textur a vzhled materiálů (`OakPlanks`, `OakBark`, `IronTrim`) pod světlem JOSCRAFTu.
  2. Napojení `collision-boxes.json` (včetně přepínání AABB pro zavřené/otevřené dveře, zavřenou/otevřenou truhlu a aktivní větve plotu).
  3. Pravidla umísťování a volného prostoru: `oak-door` zabírá `[0, 0, 0]` a `[0, 1, 0]` s podporou pod `[0, -1, 0]` (plus průchodový sweep podle `clearance_cells`), `wooden-chest` vyžaduje volné buňky `[0, 1, 0]`, `[0, 0, 1]`, `[0, 1, 1]` pro plné otevření víka do `+Z`.
  4. Případné budoucí herní nastavení kolize/výšky skoku u `oak-fence` (geometrie zůstává `1.0 m`).
