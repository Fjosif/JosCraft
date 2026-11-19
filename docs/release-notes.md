# JOSCRAFT 0.4.3

- Oprava deformovaných postav: stejné měřítko X/Y/Z zachovává původní proporce modelů a animací.
- Výška postavy přibližně 1,85 bloku a pohled přibližně 1,61 bloku zůstávají.
- Dveřmi 1×2 prochází herní tělo široké 0,6 bloku, dekorativní části jeho kolizi nerozšiřují.
- Stejné rovnoměrné měřítko mají lokální model i LAN avatar. Originální GLB a ateliér zůstávají zachované.

# JOSCRAFT 0.4.2

- Herní postavy jsou znovu vysoké přibližně dva bloky (obálka 1,85 m), pohled z první osoby je ve výšce přibližně 1,61 m.
- Šířka a hloubka herního modelu jsou přizpůsobené jednoblokovému průchodu; model a skutečná kolize se shodují. Stejný transform mají LAN avatary.
- Dveře zůstávají 1 blok široké a 2 vysoké, podle výslovné volby uživatele.
- Původní GLB a modely v ateliéru se nemění.
- Název herního okna obsahuje verzi, aby šlo rozlišit starší spuštěné okno.

## Předchozí verze

# JOSCRAFT 0.4.1

- Dubové desky, schody, dveře, ploty a truhly: pět skutečných receptů z Mojang katalogu, výroba a stavění.
- Dveře 1×2 bloky, otáčení, skutečně pohyblivé kolize, průchod všech 11 postaviček.
- Truhla s 27 sloty, přetahováním a uloženým obsahem, animovaným víkem a kontrolou volného prostoru.
- Chůze na půlblok a po schodech bez skákání. Plotové větve podle sousedů.
- Rovnoměrně zmenšené herní postavičky včetně fyziky, kamery a LAN avatarů; originální modely zachované.
- LAN replikace rotací a otevření, autoritativní obsah truhly, jeden aktivní uživatel úložiště.
- Uložení hostitele nepřesouvá předměty z kurzoru jiného hráče.

Windows hra se spouští po rozbalení celé složky přes JosCraft.vbs. Živá uložená hra není součástí balíčku. Síťový protokol 4 vyžaduje shodnou verzi u všech hráčů.

## Předchozí etapa

JOSCRAFT0.4.0 přidává skutečnou výrobu v inventáři2×2 a pracovním stole3×3, rozdělování zásob tažením, vlastní prkna/jíl/cihly/pec, první nástroje, pochodně a osvětlený oheň v peci.

Den a noc, první zombie a ochočitelní pes/zajíc/liška s animacemi a uložením. První autoritativní multiplayer pro4hráče na stejné LAN s oddělenými inventáři. Receptář obsahuje1892normalizovaných receptů z oficiálních Bedrock dat Mojangu; výroba je aktivní pro již zavedené materiály a stanice. Zbytek materiálů je připravený v rozvojovém katalogu.

Windows balíček obsahuje vlastní runtime a automatický aktualizátor přes stabilní GitHub Releases. Rozbal celou složku a spusť `JosCraft.vbs`. Další vyšší vydané verze se stáhnou na pozadí, uloží svět a přejdou na novou verzi. Uložené hry jsou oddělené od instalačních verzí.

První LAN verze je ověřená skutečnými procesy na jednom PC; druhé fyzické zařízení, hraní přes internet/rocketweb.cz a mobilní export zatím ověřené nejsou. Kočičí model je připravený samostatně pro další integraci. Voda, láva, hlad a úplná sada Minecraft bloků/stanic ještě nejsou součástí hry. Vlastní grafika JOSCRAFT; Minecraft art nebyl převzatý.
