# Prompt pro Antigravity — zkopíruj celý tento soubor

Vytvoř originální nízkopolygonové/blokové modely pro JOSCRAFT v Blenderu, export GLB pro Godot4.7.2 Mono. Projekt D:\Projekty\Hradeti\JOSCRAFT právě souběžně mění Codex. Máš povoleno číst assets/characters a source/ jako stylovou referenci, runtime/Ecology.cs pro skutečné rozměry nových NPC. Původní projekt DetiSkola neměň. Žádná Minecraft stažená grafika/modely/textury nebo cizí chráněné postavy.

VŠECHNY výstupy ukládej výhradně do D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-creatures. Neměň runtime, assets/characters, project.godot, saves, existující .blend, žádný Git ani nasazení. Pokud produkuješ C# skripty, přípona musí být .cs.txt, aby se nedostaly do živé kompilace. Python pro Blender smí být .py.

Čtyři nové typy:
- dog: čitelný milý pes, šedohnědá srst, výrazný čumák, uši a ocas; výška cca1.05m, půdorys max0.6×1.1m.
- rabbit: milý zajíc, dlouhé uši, béžová srst, zadní nohy; celková výška včetně uší max0.95m, půdorys max0.6×0.9m.
- fox: oranžová liška, tmavé tlapky, krémový čumák a bílá špička ocasu; výška max1.05m, půdorys max0.6×1.4m.
- zombie: vlastní nekrvavá stylizovaná hranatá zombie, zelená pleť, modrošedé oblečení, výška2.1m, půdorys max0.7×0.8m. Přátelský rodinný vizuální styl, ale odlišit od zvířat.

Godot souřadnice metry, podlahaY=0, dopředný směr-Z, aplikovat transformace, žádné záporné měřítko, správné normály. Origin pod chodidly. Export musí obsahovat materiály/vertexcolors a kostru/animace, bez externích absolutních texturových cest. Originální GLB postav mají měkké ploché barvy, jemné bevely a vlastní tvary; nový styl k nim má sedět. Jedna postava ideálně do5000trojúhelníků, jeden atlas nebo málo materiálů.

Animace se jmény přesně Idle, Walk, Run, Jump, Sit (zvířata), Attack (zombie), případně Hurt. In-place, žádný root motion do stran, loop Idle/Walk/Run. Klidové a kontaktní fáze nohou leží naY=0; Jump se předává jako vizuální póza, pohyb řeší fyzika Godotu. Sit nesmí podlahu protínat. Ocas/uši mohou mít jemnou animaci.

Dodání:
- source/dog.blend, rabbit.blend, fox.blend, zombie.blend + autorský generate_creatures.py (reprodukovatelné).
- exports/dog.glb, rabbit.glb, fox.glb, zombie.glb.
- previews/ přehled každého modelu a kontakt nohou, případně animační sheet.
- manifest.json: mesh counts, skutečné rozměry/nejnižšíY pro každý klip, seznam animací, relativní cesty, SHA256, vlastní autorství.
- HANDOFF.md: co je ověřené a co zbývá. Žádný falešný PASS usazení ve hře: hru integruje a ověří Codex.

Veškeré modely zůstanou v incoming složce, jejich integraci provede Codex po kontrole skutečně vykreslených modelů a kolizí. Nepřepisuj nic existujícího a nezavírej běžící aplikace. Na konci oznam přesnou cestu HANDOFF.md.
