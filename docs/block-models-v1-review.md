# Převzetí blokových modelů v1

Opravená dodávka Antigravity byla převzata do `source/blocks-v1` (zdroje, pět GLB, metadata a kolize) bez změn aktivní hry. Kopie byly porovnány SHA256 s dodávkou; záloha je `artifacts/accepted-blocks-v1.zip`.

Nezávislé binární ověření: pět GLB, správné SHA256 a počty trojúhelníků, zabalené obrázky, rozměry geometrie ve výchozím a koncových stavech, zavřený výchozí stav odpovídající Close, pokrytí vrcholů složenými kolizními boxy včetně plotových větví. Bez nalezených neshod. Report: `artifacts/blocks-delivery-review.json`.

Godot 4.7.2 Mono: izolovaný skutečný import PackedScene, transformované mesh bounds a AnimationPlayer Open/Close — 20 kontrol prošlo. Report: `artifacts/blocks-godot-review.json`. Testovací GDScript se používá pouze v izolovaném build fixture; runtime hry zůstává C#.

Otevřené úkoly herní integrace: stabilní registry ID a recepty, rotace a obsazené buňky dveří, skutečné kolize hráče na desce/schodech, sousední spoje plotu, otevírání a uložený obsah truhly, persistence a síťová synchronizace stavů. Tyto vlastnosti zatím nejsou hotové ani otestované. Při otevírání zkoumat skutečný tvar průchodu; požadavek na prázdné celé sousední buňky by zbytečně zakázal dveře ve stěně.

Navazující herní integrace je nyní popsána a ověřena zvlášť v `docs/building-v0.4.1.md` a `artifacts/building-qa.json`. Výše uvedené převzetí je historický stav před integrací.
