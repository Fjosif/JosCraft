# Stavění a úložiště 0.4.1

Velikost postav popsanou níže nahrazuje od verze 0.4.2 `docs/player-sizing-v0.4.2.md`: postavy jsou znovu vysoké přibližně dva bloky, dveře zůstávají 1×2.

Všechny nové recepty jsou v receptáři pracovního stolu 3×3 a vycházejí z již připnutého katalogu Mojang Bedrock. Výsledky se spotřebují při položení a vrací se při vytěžení.

| Výrobek | Suroviny a rozložení | Výsledek |
|---|---|---|
| Dubová deska | 3 dubová prkna v řadě | 6 |
| Dubové schody | 6 dubových prken ve stupních 1 / 2 / 3 | 4 |
| Dubové dveře | 6 dubových prken, 2 sloupce × 3 řady | 3 |
| Dubový plot | dvě řady: prkno / klacík / prkno | 3 |
| Truhla | 8 libovolných zavedených prken kolem prázdného středu | 1 |

Pravé tlačítko dveře otevře nebo zavře. Dveře jsou jeden blok široké a dva vysoké, vyžadují plnou podporu zespodu. Zásah horní části míří na jedny celé dveře; těžba uvolní obě buňky a vrátí jeden předmět. Při ztrátě podpory dveře spadnou jako sebratelný předmět. Otočení při stavění vychází ze směru pohledu. Schody mají dvě skutečné půlmetrové kolizní části a ovladač umí vystoupit bez skoku, pokud nad hráčem není strop.

Truhla se pravým tlačítkem otevře do úložiště 27 míst. Přesouvání, rozdělení tažením a pravé tlačítko fungují stejně jako v batohu. E nebo Esc úložiště zavře. Víko se pohybuje společně se svou kolizí a potřebuje místo nad truhlou i vzadu. Celý průběh pohybu se předem prověřuje skutečnými kolizními tvary. Hráč nemůže zavřít dveře přes jiného hráče; nelze položit blok do otevřeného víka. Truhlu s obsahem nebo otevřeným úložištěm nelze vytěžit. Dvojité truhly zatím nejsou zavedené.

Plot zapíná viditelné větve i odpovídající kolize směrem k sousednímu plotu nebo plnému bloku. Jeho dodaný model a kolize jsou vysoké jeden metr; plot zatím není nepřeskočitelný jako v Minecraftu. Desky jsou spodní půlbloky; horní desky, spojení dvou desek a rohové schody jsou další etapa.

Herní postavičky jsou při zachování proporcí zmenšené podle naměřené obálky animací (`data/hero-gameplay-sizes.json`), aby jejich skutečné tělo prošlo dveřmi. Stejné měřítko má model, kolize a síťový avatar, výška kamery vychází z velikosti postavy. Původní GLB a ateliér zůstávají beze změn. Průchod všech 11 postaviček je ověřen skutečným fyzikálním pohybovým dotazem v otvoru se stěnami a překladem.

Rotace, otevření a obsah truhly se ukládají se světem. LAN hostitel rozhoduje o stavění, otevírání i přesouvání věcí. Jednu truhlu současně používá jeden hráč, další dostane zprávu; nepoužívá se kopie obsahu, kterou by šlo duplikovat. Síťový protokol je 4. Výchozí ID 0–33 zůstala zachovaná, nová jsou 34 deska, 35 schody, 36 dveře, 37 plot, 38 truhla, 39 interní horní obsazení dveří (nejde sebrat ani vyrobit).

Uložení vzdáleného batohu je nyní čistý snímek: nemění právě držený předmět, výrobní mřížku ani obsah truhly. Zůstává uložená i držená zásoba, která by se do plného batohu nevešla.

Ověření: `tools/verify_building.ps1`, `tools/verify_survival.ps1`, `tools/verify_net.ps1`. Průběh a screenshoty jsou v `artifacts/`. Testy používají izolované světy a porovnávají hash živého uložení před a po testu.

Claudeův registr byl převzat do `source/material-registry-v1`: 102 testů parseru/validátoru/adaptéru a schéma prošly. Dodaná data zachycují starší stav ID 1–33; před aktivací adaptéru se musí doplnit nová ID. Současná výroba používá existující ověřený receptář. Registr sám nepředstavuje 1286 hotových materiálů ve hře.
