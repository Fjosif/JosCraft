# Prompt pro Claude — zkopíruj celý tento soubor

Pracujeme na JOSCRAFT, vlastní blokové survival hře Godot 4.7.2 Mono / C# / net8.0 pro Windows. Projekt: D:\Projekty\Hradeti\JOSCRAFT. Codex právě mění výrobu, inventář, Ecology, updater a ukládání. Navrhni a implementuj první skutečný multiplayer: PC hostuje autoritativní společný svět, děti se připojují přes LAN/IP. Budoucí rocketweb.cz může zprostředkovat vyhledání místnosti, ale nyní nevymýšlej existující server ani API. Žádné cloudové nasazování, žádný push, žádné úpravy původního DetiSkola.

NEUPRAVUJ žádné existující soubory ani běžící hru. Všechny výstupy zapisuj výhradně do D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\claude-multiplayer. Zdrojové C# návrhy zatím ukládej jako *.cs.txt (Godot automaticky kompiluje všechny .cs v projektu). Nedělej vlastní runtime/ kopii projektu. Máš povoleno číst aktuální runtime/*.cs, project.godot, Joscraft.csproj, docs/*.md. Znovu je přečti před finálním návrhem — Codex je souběžně upravuje.

Požadavky:
- Godot ENet, vlastní server na PC, port volitelný (výchozí24567), nejvýše4hráči. UI návrh host/join, jméno a adresa. Číslo protokolu/verze: odmítnout nesoulad s čitelnou hláškou, žádná změna save.
- Server rozhoduje o voxelech, dosahu, těžbě/čase, vlastnictví, spotřebě při stavění/výrobě, lootu, NPC, zdraví a denní době. Klient neposílá celé autoritativní inventory/edit DTO. Ověřovat typ/počet/rate/dosah/viditelnost; neudělat creative cheat přes RPC.
- Sdílet stejný seed, masku stromů a uložené editace. Snapshot pro nového klienta musí zahrnout stav po těžbě i ležící kusy dřeva; následně pořadovaná změnová zpráva. Přenos po bounded dávkách, žádný obří nespolehlivý UDP paket. Vyřešit snapshot vs živé změny.
- Oddělené inventáře a pozice hráčů v hostově save. Spolehlivý profil/ID při reconnectu, ne důvěra v podvržené jméno; stačí lokální vygenerovaný token pro tuto první LAN verzi. Žádné osobní údaje/tokeny do logu.
- Odpojení/ukončení hosta: bezpečně uložit, čitelně vrátit klienta do menu. Host menu pauzy nesmí zastavit ostatní; inventář je lokální. Žádný klient nesmí přepsat lokální singleplayer save stavem hosta.
- Před aktualizací synchronizovaně uložit a odpojit; automatické opětovné připojení až ke stejné verzi. Updater nebudeš přepisovat.
- Vzdálené postavy použij existující originální GLB z assets/characters. Nečti/neupravuj jiný projekt.

Přečti zejména BlockWorld.cs, SurvivalWorld.cs, GameSession.cs, GameMenu.cs, GameUi.cs, BlockTerrain.cs, WoodPickup.cs, Ecology.cs a AutoUpdater.cs, pokud už existuje. Nabídni malé explicitní integrační hooky; nezávislé moduly mohou být partial BlockWorld, ale patch popiš zvlášť. Nesmíš použít GDScript.

Dodání do své incoming složky:
1. Zdrojové návrhy *.cs.txt.
2. INTEGRATION.md s přesnými hooky a seznamem souborů, které má Codex po kontrole upravit.
3. TEST_PLAN.md: dvě skutečné procesové instance, host+join, snapshot již upraveného světa, těžba/stavění bez duplikace, rozdílná inventáře, reconnect, odpojení, odmítnutí jiné verze a neplatných RPC. Neoznačuj testy PASS, pokud jsi je skutečně nespustil.
4. HANDOFF.md: co je hotové, co nebylo ověřené, známá omezení.

Žádný Git init/push/release, žádné uzavírání uživatelovy hry, žádný zápis do saves/, žádná zařízení ani firewall/router úpravy. Až dokončíš, oznam uživateli přesnou cestu HANDOFF.md a že Codex musí výsledky integrovat.
