# Stromy a sběr dřeva — podklady pro JOSCRAFT

Požadavek uživatele 13.11.2026: stromy a těžení dřeva jako v Minecraftu, s vlastním výtvarným stylem a runtime v C#.

## Co bylo skutečně dohledané

[Oficiální How to craft](https://www.minecraft.net/en-us/article/how-craft) popisuje ruční těžení kmenů, předměty dřeva ve světě a sběr přiblížením do inventáře, pokud je v něm místo. Dále ukazuje zpracování jednoho kmene na čtyři prkna a pracovní stůl; výroba receptů není součástí nynějšího úkolu se stromy.

[Oficiální Leaves](https://www.minecraft.net/en-us/article/block-week-leaves) popisuje rozpad přirozeného listí bez podpory kmene a odlišuje běžné rozbití listí od získání listového bloku nástrojem. Jde o historický článek, proto jeho stará vzdálenostní/procentní čísla nevydáváme za přesnou současnou mechaniku všech edic. Původ a vzhled druhů doplňují [Oak Wood](https://www.minecraft.net/en-us/article/block-week-oak-wood), [Birch Wood](https://www.minecraft.net/es-es/article/block-week-birch-wood) a [Spruce Wood](https://www.minecraft.net/ja-jp/article/block-week-spruce-wood).

## Konkrétní rozsah JOSCRAFTu

- Dub, bříza a smrk mají odlišnou kůru i siluetu, vlastní generované textury a deterministické umístění podle seedu. Kmeny/listí jsou skutečné voxely s kolizemi, nikoli dekorace.
- Každý blok kmene se těží samostatně držením tlačítka; základní ruční doba v JOSCRAFTu je3s. Spodní blok neshodí celý strom. Jeden vytěžený blok dává jeden předmět příslušného dřeva, který sebere přiblížení.
- Běžné rozbití listí rukou nepřidá listový blok do batohu. Přirozené listí bez cesty přes listové sousedy ke kmeni postupně mizí. JOSCRAFT používá omezenou podpůrnou vzdálenost6 a vlastní krátké zpoždění; neprohlašuje úplnou shodu ticků s Minecraftem.
- Sazenice, jejich růst, jablka, další náhodné dropy, sekery, prkna, pracovní stůl a výrobní recepty nejsou v této etapě předstírané. Získané kmeny lze přímo použít jako stavební bloky. Ukládají se zásoby, ležící kusy dřeva i vytěžené části stromu.
- Při přechodu staršího světa na les se jednou určí a dále uloží maska stromů vynechaných kvůli existujícím úpravám a pozici hráče. Běžné pokácení už se za migrační překážku nepovažuje; zbývající části stromu při načtení nemizejí.

Vlastní grafika vznikla vestavěným image_gen. Přesné prompty: `../source/textures/prompts-trees-v1.json`; rozměry, autorství a SHA: `../artifacts/tree-texture-manifest.json`. Žádná Minecraft textura, model, logo nebo zvuk nejsou použitými runtime assety.
