# Automatická vydání pro Windows

Repozitář aktualizací: `Fjosif/JosCraft`, veřejné stabilní GitHub Releases. `AutoUpdater.CurrentVersion` je současně herní síťová verze. Pouhé nahrání zdrojového souboru není vydání; je nutný spustitelný release ZIP a `update.json` se stejnou verzí, velikostí a SHA256.

První instalace: rozbalit `build/JosCraft-windows-x64.zip` do zapisovatelné složky a spouštět `JosCraft.vbs`. Balíček obsahuje samostatný Godot/.NET runtime; děti nepotřebují editor ani SDK. Vývojový `SPUSTIT_JOSCRAFT.vbs` zůstává pro tento pracovní projekt a sám jeho zdroje nepřepisuje.

Vydaná hra kontroluje Releases po startu a pak přibližně každé3minuty. Stahuje na pozadí, kontroluje velikost/SHA256, čeká na uzavření inventáře, uloží svět, zobrazí aktualizaci a spustí externí pomocník. Ten vyčká na konec procesu, odmítne nebezpečné ZIP cesty, rozbalí novou verzi do nové složky, atomicky přepne `current.json` a spustí hru s návratem do světa. Původní verzi ani uživatelské saves nemaže. Pokud nový proces nepotvrdí start, obnoví předchozí verzi. Selhané vydání označí, aby se neopakovalo dokola; příští vyšší verze se zkusí normálně.

V LAN multiplayeru host nejprve uloží společný svět a odpojí klienty; po aktualizaci znovu otevře LAN. Klienti si krátce pamatují hostitele a připojí se jen při shodné verzi. Kontrola původního návrhu a skutečné loopback QA jsou oddělené od fyzické internetové/zařízení zkoušky. Bez vydané novější verze nelze tvrdit, že na dětském PC proběhlo živé stažení z GitHubu; lokální QA testuje skutečný export, výměnu, porušený ZIP a rollback.

Vydání připraví `powershell -ExecutionPolicy Bypass -File tools/package_release.ps1 -Version 0.4.0`. Před změnou verze uprav konstantu i export metadata. `tools/publish_release.ps1` je explicitní příkaz k vydání po úspěšných kontrolách; žádný skript nenahrává živé uložené hry. Android/iOS aktualizace mají jiný instalační mechanismus a v této etapě nejsou implementované.

Reference: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases), [Godot Windows export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_windows.html), [Godot feature tags](https://docs.godotengine.org/en/stable/tutorials/export/feature_tags.html).
