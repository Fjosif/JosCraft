# JOSCRAFT0.4.0

Výroba a inventář: zásoba následuje kurzor, držení a tažení přes políčka připraví rovnoměrné rozdělení; puštění ho atomicky potvrdí. Pravé tlačítko zvedne polovinu nebo položí jeden kus. Rozdělení4kusů přes3políčka je1+1+2. Zavření inventáře vrací suroviny do batohu; nevymaže zásobu, která se nevejde.

Receptář je pod `?` v inventáři. Malý crafting2×2, pracovní stůl3×3 po pravém kliknutí ve světě. Kmeny →prkna, klacíky, stůl, dřevěné/kamenné krumpáče, sekery a meče. Kámen vyžaduje krumpáč a dává dlažební kámen. Barevná povrchová ložiska jílu dávají4hroudy, z nich pec vypálí cihly. Čtyři cihly tvoří stavební blok. Pec:10s na kus, uhlí/dřevěné uhlí80s, kmen/prkno15s. Umí i aktuálně zavedené další vanilla smelting vstupy. Otevřený inventář svět nezastaví; Esc pauza v singleplayeru zastaví čas.

Pochodně jsou tenké skutečné3Dobjekty, nikoli neprůchodná kostka. Stojí na horním pevném povrchu, svítí a mají animovaný plamen. Po odstranění podkladu spadnou jako sbíratelný předmět. V první verzi se nepřipevňují na stěny. Hořící pec má vlastní plamen a světlo jen během hoření. Nejvýše6nejbližších skutečných světel svítí současně kvůli limitům rendereru; plameny vzdálenějších předmětů zůstávají vidět.

Den/noc:20minutový cyklus, čas a zdraví na HUD. V noci vznikají až3zombie, jdou k hráči, útočí; levým tlačítkem bojuj, meč zesílí zásah. Na slunci bez krytu dostávají poškození. Po poražení se hráč vrací na start se zásobami. Zombie v této první verzi dává kost pro psa — vlastní zjednodušené pravidlo JOSCRAFTu. Hlad, kompletní Minecraft mob drops a odolnost nástrojů zatím nejsou zavedené.

Pes se ochočí kostí, zajíc mrkví, liška bobulemi. Výchozí okolí má malé zdroje mrkví/bobulí, pravé tlačítko je sklidí. Pravé tlačítko na ochočeného společníka přepíná čekání/následování. V multiplayeru následuje svého vlastníka a cizí hráč mu nemění režim. Modely, kostry a animace dodal Antigravity; integrace a skutečné kontakty s podlahou byly prověřené v Godotu. Kočičí model byl samostatně dodaný a archivovaný v `source/cat-companion`; kočka ještě není přidaná do herní populace ani systému krmení.

Multiplayer: `Hrát spolu (LAN)` v hlavním menu, host na PC, adresa/port24567, max4hráči. Autoritativní svět a inventáře ukládá hostitel; klient nikdy nepřepisuje vlastní singleplayer slot. Změna verze/protokolu je odmítnutá s hláškou. Pauza hostitele ostatní nezastaví. Podklad dodal Claude a byl po kontrole integrovaný/opravený proti skutečnému Godot API. `tools/verify_net.ps1` používá skutečné samostatné procesy a místní loopback. Fyzické druhé PC/tablet, veřejný internet, NAT průchod a propojení s rocketweb.cz zatím nejsou ověřené ani nasazené. Pohyb klienta má kontrolu rychlosti/dosahu, ale ještě plnou serverovou fyziku proti průchodu zdí nepoužívá.

Stará Version1 uložení zůstávají čitelná. Nové soubory ukládají také čas, zdraví, vlastníka zvířete, pece, pochodně a oddělené vzdálené hráče včetně držených zásob. Exportovaná hra ukládá do `user://saves`, odděleně od instalačních verzí; vývojový projekt nadále používá místní `saves/`.

Ověření: `tools/verify_survival.ps1`, `tools/verify_net.ps1`, `tools/package_release.ps1`, `tools/verify_updater.ps1`; skutečné logy a obrázky v `artifacts/`. Staré reporty popisují své historické etapy, nejsou důkazem nových funkcí.
