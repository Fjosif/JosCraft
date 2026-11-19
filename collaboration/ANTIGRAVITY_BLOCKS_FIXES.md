# Antigravity — opravy blokové sady po nezávislé kontrole GLB

Pracuj pouze v D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-blocks-v1. Neměň runtime/assets/saves/Git ani ostatní incoming složky. Zachovej zdroje, oprav generátor a znovu vytvoř exporty, náhledy, metadata, SHA256 a HANDOFF.md.

Codex ověřil skutečné GLB chunky, node transformace a koncové transformační klíče Open/Close, nikoliv jen Blender scénu. Všech pět souborů má správné SHA256, zabalené zdroje a počet trojúhelníků pod 2000. Deska a schody odpovídají uvedeným rozměrům. Náhledy otevřených dveří a truhly byly vizuálně zkontrolované.

Oprav:
1. Dveře A TRUHLA jsou v exportovaném výchozím stavu OTEVŘENÉ. Exportuj skutečný výchozí uzlový stav zavřený. Nestačí přejmenovat metadata. Načtení GLB bez přehrání animace musí odpovídat konci Close. Open a Close musí zůstat funkční a mít opačné koncové stavy.
2. Metadata zavřených stavů neodpovídají GLB. Skutečný konec Close dveří: min [-0.5,0,-0.51625], max [0.5,2,-0.29625]. Truhla Close: min [-0.445,0,-0.4775], max [0.445,0.8825,0.4375]. Po opravě exportu přeměř skutečné transformované vrcholy exportovaného GLB a aktualizuj bounds. Uveď metodu; Blender reimport neoznačuj za test Godot fyziky.
3. Konzervativní kolizní boxy nyní nepokrývají některé části rámu/kování dveří a truhly. Přesné podrobnosti jsou v D:\Projekty\Hradeti\JOSCRAFT\artifacts\blocks-delivery-review.json, reprodukovatelné kontrolou python tools\qa_block_delivery.py z kořene projektu. Dodělej boxy nebo zdokumentuj záměrně nekolidující malé ozdobné části explicitně; neoznačuj nepokrývající boxy za přesně konzervativní vůči celé geometrii. Nepoužívej velkou plnou kostku místo tvarů. Víko a tělo musí zůstat oddělené.
4. Otevřená truhla zasahuje do +Z sousední buňky až na z=0.7575. clearance_cells nyní uvádí jen horní buňku. Doplň zadní i horní-zadní buňku podle skutečného průběhu otevírání, nebo uprav pant/geometrii. U dveří uveď přesah do -X (aktuálně x=-0.51625) a požadovaný prostor během otáčení; clearance_cells nesmí zůstat bez vysvětlení prázdné. Ověř celý průběh animace, ne pouze konce.
5. U plotu výslovně ponech informaci, že výška geometrie je 1 m. Nehlásit Minecraftové nepřeskočení plotu bez budoucí herní implementace.

Modely nyní nejsou přijaté jako hotová herní fyzika. Po dodání je Codex znovu ověří a provede zapojení do Godotu. Žádné změny hry nedělej sám.
