# Velikost postav 0.4.2

Historická úprava, nahrazená verzí 0.4.3: `docs/player-sizing-v0.4.3.md`. Samostatné měřítko svislé osy se již nepoužívá.

Uživatel odmítl drobné postavičky z 0.4.1 a výslovně zvolil dveře široké jeden blok a vysoké dva bloky. Herní výška obálky postavy je nyní 1,85 m, fyzická výška přibližně 1,88 m a první osoba má oči přibližně 1,61 m nad zemí.

Kořen herního modelu používá samostatný faktor pro svislou osu a společný faktor pro šířku a hloubku. Postavy tedy zůstávají vysoké a jejich herní silueta je štíhlejší. Tím se nahrazuje rovnoměrné zmenšení z předchozí verze. Kolize je odvozená ze stejné transformované obálky skutečných animací, včetně uší, křídel a ocasu; nezmenšuje se jen neviditelný collider. Síťové avatary používají stejný transform. Původní GLB soubory ani ateliér se neupravují.

Měřítka a původní rozměry jsou v `data/hero-gameplay-sizes.json`. Ověření přes `tools/verify_building.ps1` kontroluje pro všech 11 postav výšku, výšku očí a skutečný fyzikální průchod otevřenými dveřmi mezi stěnami a pod překladem. Stavební test také ověřuje výstup na půlblok/schody a odmítnutí výstupu skrz nízký strop. Screenshot `artifacts/building-player-full-height.png` porovnává model s dveřním otvorem.
