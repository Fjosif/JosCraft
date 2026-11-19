# Prompt pro Antigravity — první sada neplných bloků JOSCRAFT

Tvůj pes/zajíc/liška/zombie už jsou integrovaní v JOSCRAFTu a ověření v Godotu. Kočičí model je také předaný a archivovaný; nyní potřebujeme stavební sadu pro další etapu. Projekt D:\Projekty\Hradeti\JOSCRAFT, Godot4.7.2 Mono / C#, Blender5.2.2.

Všechny výstupy ulož POUZE do:
D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-blocks-v1

Neměň runtime, assets, existující Blender zdroje, saves, project.godot, Git/release ani jiné incoming složky. Nezavírej běžící aplikace. C# příklady pouze *.cs.txt. Nepoužívej převzaté Minecraft modely/textury. Použij vlastní již hotové JOSCRAFT materiály jako vzhledovou referenci: assets/textures/oak-planks-v1.png, oak-bark-v1.png, log-end-v1.png, table-side-v1.png, cobblestone-v1.png.

Vytvoř pět ORIGINÁLNÍCH herních GLB modelů v měkkém hranatém stylu JOSCRAFT:
1. `oak-slab`: spodní dubová deska,1×0.5×1m.
2. `oak-stairs`: dvoustupňové dubové schody v jedné buňce1×1×1m; pevné spojení spodní a horní části, bez zbytečných vnitřních ploch.
3. `oak-door`: dubové dveře široké1m a vysoké2m, přibližně0.1875m silné, pant vlevo. Oddělený pivot, animace `Open` a `Close`, přesné koncové stavy0°/90°. Bez nápisů/logo. Textura vlastní, žádná kopie Minecraft dveří.
4. `oak-fence`: modulární plot — centrální sloupek a samostatné pojmenované větve `North`, `South`, `East`, `West`, aby je Godot mohl zapnout podle sousedů. Geometrie i kolize mají doloženou výšku do1.5m; neviditelnou přesahující kolizi nevydávej za hotové řešení.
5. `wooden-chest`: jednoduchá vlastní dřevěná truhla, přibližně0.875m široká i hluboká,0.875m vysoká. Víko s pantem a `Open`/`Close`, zavřený a otevřený stav, měkké bevely a jednoduché kování. Bez kopie specifických Minecraft detailů.

Souřadnice/export:
- Godot metry,+Y nahoru,-Z dopředu. Origin každého kořenového modelu uprostřed základny naY=0; půdorys buňkyX/Z=-0.5..0.5. Pant dveří je pojmenovaný poduzel, ne přemístěný kořen. Přesné skutečné bounds uveď po reimportu GLB v Godot souřadnicích.
- Aplikované transformace, správné normály, žádné záporné měřítko, žádné externí absolutní cesty. Malý počet materiálů a max. přibližně2000trojúhelníků na model; plot může být výrazně lehčí.
- JOSCRAFT textury smíš vložit do GLB; použité vlastní soubory zkopíruj do svého texturového podadresáře. Nikdy nepřepisuj původní assets. UV orientace dřeva musí dávat smysl.
- Dveře zabírají dvě buňky nad sebou; otevřené víko truhly může zasahovat nad jednu buňku. Uveď potřebný volný prostor, podmínku podpory a kotvu. Modely v klidu musí spodní geometrií skutečně ležet naY=0. Nehlásit herní fyziku PASS bez integrace.

Dodání:
- source/blocks-v1.blend, reprodukovatelný source/generate_blocks.py a související vlastní textury.
- exports/ pět GLB, případně jednotlivé plotové poduzly v jednom GLB.
- previews/ přehledy zepředu, zboku a shora; otevřené/zavřené dveře a truhla.
- block-models.json pro Codex: namespacedID (minecraft:oak_slab, oak_stairs, oak_door, oak_fence, chest), soubor, rozměry, Godot bounds, anchor/support, occupancy cells, seznam poduzlů/animací a SHA256.
- collision-boxes.json: konzervativní složení AABB pro každý pevný stav. Každý box `center:[x,y,z]`, `size:[x,y,z]` v Godot souřadnicích; schody ze dvou boxů, deska z jednoho, dveře zvlášť pro zavřeno/otevřeno, truhla má tělo oddělené od víka. Nespoléhej na velký plný cube collider místo skutečného tvaru.
- HANDOFF.md: co je reálně ověřené, co ještě musí Codex prověřit ve hře, žádné domnělé PASS.

Neimplementuj vlastní inventář, recepty ani runtime. Claude připravuje registr a Codex provede integraci tvarů, sousedních spojů, obsazených buněk, renderu a kolizí. Na konci oznam přesnou cestu HANDOFF.md.
