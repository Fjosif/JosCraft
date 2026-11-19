# Proporce postav 0.4.3

Oprava nahrazuje nezdařené odlišné měřítko svislé a vodorovné osy z verze 0.4.2. Herní model nyní používá rovnoměrné měřítko: X = Y = Z. Obličej, trup a animace tak mají stejné poměry jako původní GLB. Naměřená výška animační obálky je přibližně 1,85 m, kamera přibližně 1,61 m nad zemí. Stejnou transformaci používá LAN avatar.

Průchod dveřmi 1×2 se řeší fyzickým herním tělem 0,6×0,6 m a výškou přibližně 1,88 m, ukotveným u chodidel. Uši, ocas, křídla a roztažené končetiny jeho šířku nezvětšují. Jejich viditelná obálka se dál uchovává zvlášť ve VisualFootprintMin/Max; tělo již nepředstírá, že pokrývá každou dekorativní část modelu. V úzkém průchodu tak mohou dekorativní části zasahovat do okolí. To je vědomá změna kolizního pravidla místo deformování postavy.

Originální soubory GLB a ateliér se nemění. Stavební QA kontroluje pro všech 11 postav rovnoměrné měřítko, normální výšku očí a průchod skutečného herního těla rámovaným dveřním otvorem. Screenshot skutečné hry: `artifacts/building-player-full-height.png`. Pravidla výroby, ukládání a otevírání dveří/truhel zůstávají stejná.
