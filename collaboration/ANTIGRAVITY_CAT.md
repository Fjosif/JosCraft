# Prompt pro Antigravity — kočka jako zvířecí společník

Vytvoř nový originální model domácí KOČKY na čtyřech nohách pro blokovou survival hru JOSCRAFT. Je to zvířecí společník, samostatný model od již existující hratelné postavy cat.glb. Použij stejný měkký low-poly/blokový styl jako tvoje nové modely dog/rabbit/fox: jemné bevely, výrazný čumák a oči, trojúhelníkové uši, dlouhý pružný ocas, zřetelné tlapky. Přívětivá mourovatá kočka se zrzavou srstí, jemnými tmavšími pruhy, krémovým čumákem/hrudí a zelenýma očima. Nevytvářej kopii žádného Minecraft modelu.

Projekt: D:\Projekty\Hradeti\JOSCRAFT. Godot4.7.2 Mono / C#, Blender5.2.2. Codex souběžně integruje ostatní modely; žádné stávající soubory nepřepisuj.

VŠECHNY výstupy ulož výhradně do:
D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-cat

Nesmíš měnit runtime/, assets/characters/cat.glb, project.godot, uložené hry, jiné incoming složky ani původní projekt DetiSkola. Žádný Git/push/release, žádné zavírání aplikací. C# návrhy jen s příponou .cs.txt.

Technické požadavky:
- Metry, Godot +Y nahoru, dopředu -Z. Origin pod tlapkami naY=0, aplikované transformace, bez záporného měřítka, správné normály.
- Klidová výška včetně uší přibližně0.75m, šířka nejvýše0.45m, hloubka včetně ocasu přibližně1.1m. Uveď skutečné rozměry i za pohybu.
- Do5000trojúhelníků, ideálně1vertex-color materiál, bez externích absolutních texturových cest.
- Kostra a samostatné in-place animace přesně Idle, Walk, Run, Jump, Sit, Hurt, případně Purr. Idle/Walk/Run smyčky. Jemně animovaný ocas/uši. Žádný boční root motion.
- V kontaktních fázích tlapky naY=0, žádný průnik podlahou. Sit nesmí procházet podlahou. Jump uváděj zvlášť jako záměrný pohyb nad podlahou; fyzické skákání řídí Godot.
- Tělo musí být odlišitelné od psa i lišky i z herní vzdálenosti. Žádné oblečení a žádná lidská vzpřímená postava.

Budoucí chování ve hře bude řešit Codex: ochočení krmením, následování hráče, pravé tlačítko přepíná čekání/následování. Teď neimplementuj vlastní rybaření ani nové herní recepty. Připrav pouze kvalitní model/animace a integrační údaje.

Dodej:
1. source/cat-companion.blend a source/generate_cat.py (reprodukovatelný generátor).
2. exports/cat-companion.glb s materiály, kostrou a všemi animacemi.
3. previews/cat-preview.png, cat-side.png a přehled animací.
4. manifest.json: vlastní autorství, počty vrcholů/trojúhelníků/materiálů, skutečné bounds a minY všech klipů po re-importu GLB, seznam animací, relativní cesty, SHA256.
5. HANDOFF.md: co bylo skutečně ověřeno a co musí Codex ověřit v Godotu.

Nehlaš ověření ve hře, pokud nebylo skutečně provedeno. Výstupy nech v incoming složce; Codex je zkontroluje, zkopíruje a zapojí do hlavní hry. Na konci uživateli oznam přesnou cestu HANDOFF.md.
