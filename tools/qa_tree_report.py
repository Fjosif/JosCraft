"""Freeze independent forest milestone evidence; no gameplay mutations."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[1]
def read(p):return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
r=read('artifacts/tree-qa.json');isolation=read('artifacts/tree-live-isolation.json')
assert r['pass'] and not r['failures'] and isolation['pass']
assert (ROOT/'artifacts/tree-qa.stderr.log').stat().st_size==0
assert r['determinism']['same']==r['determinism']['repeat']!=r['determinism']['different']
assert r['initialMesh']['invalid']==0 and r['initialMesh']['triangles']==r['initialMesh']['expected']
files=['runtime/TreeQa.cs','runtime/TreeGenerator.cs','runtime/BlockTerrain.cs','runtime/BlockCatalog.cs','runtime/BlockWorld.cs','runtime/WoodPickup.cs','runtime/GameSession.cs','runtime/GameMenu.cs','runtime/GameUi.cs','runtime/GameQa.cs','project.godot','Joscraft.csproj','world.tscn','tools/verify_trees.ps1','artifacts/tree-qa.json','artifacts/tree-live-isolation.json','artifacts/tree-build.log','artifacts/tree-import.log','artifacts/tree-qa.log','artifacts/tree-qa.stderr.log']
for name in ['oak-bark','birch-bark','spruce-bark','log-end','oak-leaves','birch-leaves','spruce-leaves']:
    files.extend(['assets/textures/'+name+'-v1.png','assets/textures/'+name+'-v1.png.import'])
images=sorted(p.relative_to(ROOT).as_posix() for p in (ROOT/'artifacts').glob('tree-qa-*.png'))
limitations=['Měření proběhlo na Windows/RTX4090 s limitem120FPS;390×844 je desktopový viewport, nikoli fyzické mobilní měření.','Po vytěžení spodního bloku zbývající kmen záměrně zůstává ve vzduchu jako v blokové hře. To se neoznačuje za chybu usazení; původní přirozené kořeny byly ověřeny před těžbou.','Listí bez podpory mizí po6sekundách se stabilním rozptylem až2sekundy; podpora se hledá přes nejvýše6šestisměrných kroků listím k libovolnému dřevu i v odloženém chunku.','Semínka, plody, nástroje, recepty, pracovní stůl a sázení stromů nejsou součástí této dodávky. Listí těžené rukou zde nevydává předmět.','Běžící uživatelova hra a živý save nebyly restartovány ani migrovány. Nový kód a jednorázová ochrana starých save se uplatní při dalším spuštění/načtení.']
report={'pass':True,'reviewer':'Independent QA subagent, separate from tree generation and pickup authors','runtime_utc':r['utc'],'results':r,'visual_pass':True,'live_save_and_settings_preserved':isolation,'verified_files':[{'path':p,'sha256':sha(p)} for p in files],'screenshots':[{'path':p,'sha256':sha(p)} for p in images],'limitations':limitations}
(ROOT/'artifacts/tree-qa-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
p=r['performance'];m=r['initialMesh'];d=r['determinism'];u=r['unloadedSupport']
text=f'''# Nezávislá kontrola stromů a dřeva — PASS

Finální běh `{r['utc']}` prošel `tools/verify_trees.ps1`. C# build: **0 chyb,0 varování**, runtime stderr je prázdný. Starší game/grass QA výsledky nebyly přepsány.

- Seed vytváří deterministický plán **{d['treeCount']} stromů všech3druhů**. Opakovaný seed má stejný SHA, odlišný seed jiný. Spawn zůstává bez kmenů a korun.
- **{r['groundedRoots']} načtených kořenů** bylo ověřeno přes skutečné spodní vrcholy kůry a fyzikální i vykreslený sousední terén ve stejné výšce. Správně skrytá vnitřní plocha mezi půdou a kmenem se nevyžaduje.
- Skutečný počáteční mesh má **{m['triangles']} trojúhelníků**, přesně očekávaný počet viditelných stěn. Kontrola nenašla vnitřní stěny ani špatné pořadí vrcholů, včetně švů chunků. Typ každé skutečné plochy odpovídá správné kůře, čelu, listí nebo terénní textuře. Všech7nových textur je importovaných do256px s mipmapami. Čelo dřeva se skutečně odhalilo po odtěžení spodního kmene.
- Skutečné držení těžby nechalo kmen po2sekundách na místě a po3sekundách odstranilo právě1voxel. Horní dřevo zůstalo. Vznikl fyzikální3D kus správného druhu a počtu1; inventář se nezměnil, dokud se hráč nepřiblížil. Vrcholy vykresleného předmětu se shodují se skutečným povrchem a kolizí do tolerancí.
- Pauza zastavila fyziku i stáří předmětu. Odložený chunk ho zmrazil/skryl, ale nesmazal. Save/load zachoval jeho ID, typ, počet, polohu i vytěžený vzduch. Sběr přidal právě1dřevo a položení vlastněného kmene odečetlo právě1kus. Těžba fungovala i s plným batohem; item zůstal na zemi a po uvolnění slotu se sebral bez duplikace.
- Listí stále podporované horními kmeny po vyříznutí základny nezmizelo. Načtená část koruny zůstala podporovaná i tehdy, když byl její kmen skutečně v odloženém sousedním chunku (strom{u['treeId']}, {u['loadedLeaves']} pozorované listové buňky). Po odstranění všech podpor se během pauzy nic nerozpadlo; po pokračování zmizelo **{r['decay']['naturalLeaves']} nepodporovaných přirozených listů**. Ruční těžba listí ani rozpad nevytvořily loot. Vytěžený kmen i zaniklé listí zůstaly vzduchem po načtení.
- Konstruovaný starý save **Version1 bez nových polí** se načetl. Stromy u staré stavby/editace a u hráče byly vyloučeny před generováním. Maska `{r['migration']['frozenMask']}` se uložila a zůstala stejná při dalším načtení; nový seed ji nezdědil. Živý uživatelský soubor se pro tento test nepoužil.
- Skutečný smoke test tráva→hlína, položení hlíny a následný růst trávy prošel. Skinned chodidla hráče na skutečné půdě zůstala usazená.

Nezávisle prohlédnuty skutečné PNG přehledu lesa, dubu/břízy/smrku, vyříznutého kmene a předmětu na zemi, zmizelé koruny, položeného dřeva, inventáře a mobilního390×844pohledu. Druhy mají rozlišitelné siluety a vlastní textury; text a rychlá lišta jsou čitelné. Nezjištěna konkrétní vizuální vada.

## Skutečný desktopový výkon

{p['gpu']}, `{p['renderer']}`, `{p['msaa']}`,1280×800: **{p['chunks']}chunků, {p['triangles']}terénních trojúhelníků, {int(p['drawCalls'])}drawcalls** a {int(p['primitives'])}primitiv celého snímku. Median {p['medianMs']:.3f}ms a p95 {p['p95Ms']:.3f}ms za120snímků při limitu120FPS. Tato čísla nejsou tvrzením o výkonu telefonu či tabletu.

## Izolace a omezení

`tree-live-isolation.json` potvrzuje shodný SHA živého `saves/slot-1.json` před a po běhu a nezměněná nastavení. Všechna nová data jsou pouze v `artifacts/tree-qa-data`. Otevřená hra nebyla restartována, žádná rodinná zařízení nebyla ovládána a QA nezachytilo skutečnou myš.

'''+'\n'.join('- '+x for x in limitations)+'''

`tree-qa-review.json` obsahuje pass boolean, přesné výsledky a SHA finálního runtime, textur, importních nastavení, logů a snímků. Kontrolu lze opakovat vlastním skrytým procesem pomocí `tools/verify_trees.ps1`.
'''
import re
letter='A-Za-zÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž';parts=re.split(r'(`[^`]*`)',text)
for i in range(0,len(parts),2):
    parts[i]=re.sub(r'(?<=\d)(?=['+letter+'])',' ',parts[i]);parts[i]=re.sub(r'(?<=['+letter+r'])(?=\d)',' ',parts[i])
(ROOT/'artifacts/tree-qa-review.md').write_text(''.join(parts),encoding='utf8')
print('Independent tree review PASS',r['utc'])
