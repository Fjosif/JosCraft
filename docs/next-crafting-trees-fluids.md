# Další etapa podle obrázků uživatele

**Aktualizace po navazujícím úkolu se stromy:** dub/bříza/smrk, skutečné těžení kmenů, fyzické kusy dřeva, sběr a přirozený rozpad listí jsou nyní implementované. Aktuální rozsah a omezení shrnuje `tree-reference.md`, architekturu `tree-generation.md`, výsledky `../artifacts/tree-qa.json`. Další částí zůstávají výrobní recepty/pracovní stůl a poté tekutiny. Níže je zachovaný původní plán.

Uživatel 13. 11. 2026 potvrdil referenční uspořádání: malý crafting 2×2 uvnitř inventáře, velký crafting 3×3 po použití pracovního stolu. Výsledek je v samostatném slotu vpravo. První obrázek ukazuje rozhraní velké mřížky (konkrétní zobrazený recept není zadáním všech budoucích receptů); druhý umístění malé mřížky vedle náhledu postavy. Vlastní vzhled a assety JOSCRAFT zůstávají zachované.

Další dva uživatelské obrázky určily přesný inventářový layout: světlá karta, vlevo postavička/výbava, vpravo2×2, pod tím27zásob3×9 a9lišta dole. Tento layout už nyní implementuje GameUi včetně skutečné3Dpostavičky a čtyř interaktivních dočasných surovinových slotů. Recepty zatím nejsou aktivní; současné3materiály žádný výrobní recept nemají. Výtvarné screenshoty tvořivého katalogu nepřepisují uživatelovu volbu přežití. Doplňkový uživatelský odkaz [de.minecraft.wiki/w/Inventar](https://de.minecraft.wiki/w/Inventar) nebylo možné načíst kvůli omezení přístupu; návrh se opírá o skutečně viděné dodané obrázky a níže uvedené primární zdroje.

Tento dokument zachovává zadání navazující práce. Stav stromové části byl aktualizovaný výše; výrobní recepty a tekutiny ještě nejsou hotové.

## Crafting

- Recepty jako C# data: rozměr mřížky, tvar/případně neuspořádané suroviny, množství a výsledek. Stejný matcher pro2×2 i3×3; recept širší než2 nelze provést v malé mřížce.
- Výsledný slot je pouze náhled, suroviny se odečtou až při úspěšném převzetí výsledku. Plný batoh, změna receptu, zavření, pauza i save nesmějí duplikovat/ztrácet kusy. Rozpracovaná mřížka se při zavření vrací do inventáře nebo bezpečně uloží.
- Pracovní stůl je skutečný položený blok, který pravým tlačítkem otevře3×3 v dosahu postavy. Otevření inventáře klávesouE zůstává2×2.
- Nejprve uzavřená surovinová cesta dřevo→prkna→pracovní stůl/tyčky→první nástroj; nesmí jít o nefunkční dekorativní UI. Na začátku dodat získatelné dřevo společně s prvními stromy, nikoli nekonečný katalog. Každý další recept přidat až s dostupnou surovinou a funkčním výsledkem.
- Reference principu: [oficiální How to craft](https://www.minecraft.net/en-us/article/how-craft). Popisuje inventářovou mřížku, zpracování dřeva na prkna a vznik pracovního stolu; nepřebírat grafiku.

## Stromy

Uživatelský odkaz [Trees na mcmobs.fandom.com](https://mcmobs.fandom.com/wiki/Trees) se při průzkumu nepodařilo načíst (HTTP402), proto nebyl jeho obsah vydáván za přečtený. Ověřeny náhradní primární zdroje: [Oak Wood](https://www.minecraft.net/en-us/article/block-week-oak-wood), [Leaves](https://www.minecraft.net/en-us/article/block-week-leaves), [Birch Wood](https://www.minecraft.net/es-es/article/block-week-birch-wood), [Spruce Wood](https://www.minecraft.net/ja-jp/article/block-week-spruce-wood). Jde o historické články, nikoli aktuální úplný seznam druhů Minecraftu.

Pro JOSCRAFT začít vlastními rozpoznatelnými variantami dub/bříza/smrk: jiný kmen, koruna, listí/jehličí a vlastní textury. Deterministicky ze seedu, mimo bezpečný spawn, kmen skutečně na vykreslené půdě. Těžitelné dřevo/listí jsou součást voxelových dat a save; žádné plovoucí náhražky bez kolize. Koruny přes švy chunků musí být stabilní nezávisle na pořadí načítání. Navrhnout pravidla obnovy/sazenic a opory listí až s jasným herním významem.

## Voda a láva

Primární inspirace: [Water](https://www.minecraft.net/en-us/article/block-week-water) a [Lava](https://www.minecraft.net/en-us/article/block-week-lava). Voda je průhledná tekutina se zdroji a šířením do volných buněk, láva je odlišná pomalejší nebezpečná tekutina. Kontakt obou může vytvářet pevné bloky; pravidla JOSCRAFT budou explicitní a testovaná, nikoli závislá na historických číslech článků.

Implementovat po malých krocích: nejdříve uložené zdrojové buňky, oddělený mesh povrchu/stran, průchodnost a reakce hráče; potom deterministická omezená fronta toku dolů/do stran. Žádné nekonečné iterace při načtení; rozpočet aktualizací, švy, save/reload a omezení aktivních buněk jsou nutné. Láva musí mít jasný vizuální rozdíl a bezpečné pravidlo poškození/obnovy, pokud už je zavedená životní energie. Nebezpečnou dekoraci bez funkčního pravidla neprezentovat jako hotové přežití.

Samostatné QA pro další etapu: konzervace crafting surovin, dostupnost receptů, stromové kořeny/kolize a švy, determinismus růstu/generování, tekutinové hranice a reakce, save/reload, desktopové a mobilní snímky. Runtime zůstává C#.
