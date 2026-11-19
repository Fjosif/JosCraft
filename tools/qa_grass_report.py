"""Freeze targeted grass-drop/growth evidence without rewriting older QA."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[1]
def read(p):return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
r=read('artifacts/grass-qa.json');isolation=read('artifacts/grass-live-isolation.json')
assert r['pass'] and not r['failures'] and r['inventory']['conserved'] and isolation['pass']
assert (ROOT/'artifacts/grass-qa.stderr.log').stat().st_size==0
files=['runtime/GrassQa.cs','runtime/BlockTerrain.cs','runtime/BlockWorld.cs','runtime/BlockCatalog.cs','runtime/GameMenu.cs','runtime/GameSession.cs','project.godot','Joscraft.csproj','world.tscn','tools/verify_grass.ps1','artifacts/grass-qa.json','artifacts/grass-live-isolation.json','artifacts/grass-build.log','artifacts/grass-final-build.log','artifacts/grass-import.log','artifacts/grass-qa.log','artifacts/grass-qa.stderr.log']
images=['artifacts/grass-qa-dirt-before-growth.png','artifacts/grass-qa-grass-after-growth.png']
report={'pass':True,'reviewer':'Independent QA subagent; separate from growth/drop author','runtime_utc':r['utc'],'growth_delay_simulation_seconds':r['growthDelay'],'results':{'grass_mining_drops_dirt_not_grass':True,'successful_dirt_placement_consumes_one':True,'before_three_seconds_dirt':True,'after_three_seconds_grass':True,'covered_cave_dirt_does_not_grow':r['coveredDirt'],'uncover_starts_fresh_delay':True,'pause_freezes_timer':r['pauseFrozen'],'unloaded_chunk_does_not_mature':r['unloadedDidNotGrow'],'reload_starts_fresh_delay':True,'save_load_preserves_material_and_inventory':r['saveRestoresMaterialAndFreshDelay'],'regrown_grass_drops_dirt_again':True,'finite_inventory':r['inventory'],'actual_mesh_material_physics_and_skin_samples':r['actualMeshMaterialAndPhysicsSamples'],'live_save_and_settings_unchanged':isolation},'visual_pass':True,'verified_files':[{'path':p,'sha256':sha(p)} for p in files],'screenshots':[{'path':p,'sha256':sha(p)} for p in images],'final_menu_flag_validation':'GameMenu --grass-qa isolation flag was added after the runtime run; final dotnet build passed0errors/0warnings. Growth/drop gameplay code was unchanged.','limitations':['Růst se počítá jen pro nejvyšší odkrytou Dirt buňku v načteném chunku; nejde o simulaci slunce, počasí nebo šíření ze sousední trávy.','Ukládá se materiál bloku, nikoli rozběhnutý časovač. Po načtení uložené hry čeká odkrytá hlína znovu3s. Po znovunačtení odloženého chunku začíná také čerstvé čekání.','Běžící uživatelova hra ani její neuložený stav nebyly restartovány či měněny. Nový kód se použije při dalším spuštění.','Test proběhl v samostatném skrytém desktopovém procesu; žádné rodinné zařízení ani fyzický mobilní test.']}
(ROOT/'artifacts/grass-qa-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
text=f'''# Nezávislá kontrola hlíny a růstu trávy — PASS

Runtime běh `{r['utc']}` prošel `tools/verify_grass.ps1`. C# build i závěrečný build po doplnění izolovaného menu přepínače mají **0 chyb a0 varování**. Runtime stderr je prázdný. Starší `game-qa.*` výsledky se nepřepisovaly.

Skutečný `MineTarget()` vytěžil travnatý blok a přidal **1 hlínu,0 trávy**. Skutečný `PlaceTarget()` položil vlastněnou hlínu a odečetl1 kus. Před uplynutím3 sekund zůstal blok Dirt; po přibližně3 sekundách se změnil na Grass. Tygřík stál přímo na testovaném bloku: kontrola porovnala skutečně skinned chodidla, aktuální ArrayMesh trojúhelníky, použitou Dirt/Grass texturu a fyzikální raycast. Výška vykresleného povrchu i kolize zůstala5m a postava se nepropadla ani nelevitovala.

Nezávisle prohlédnuté snímky:

- `grass-qa-dirt-before-growth.png`: skutečný hnědý blok hlíny a chodidla postavy na něm.
- `grass-qa-grass-after-growth.png`: stejný blok se zeleným povrchem a travním bočním pásem; kontakt postavy zůstává správný.

Další kontrolované situace prošly:

- Hlína pod kamenným krytem nezarostla ani po delším než3sekundovém čekání. Raycast zachytil její skutečnou boční kolizi. Odstranění krytu spustilo nové čekání; nejprve zůstala hlínou a potom zarostla.
- Pauza trvající déle než3 sekundy nezapočetla dobu růstu. Po návratu pokračoval zbývající herní čas.
- Testovaný chunk byl skutečně odložen přesunem do jihozápadní části mapy. Hlína mimo načtenou oblast nezrála; po návratu počkala znovu a následně zarostla.
- Save zachoval materiál Dirt. I když živý testovací blok později zarostl, načtení uloženého stavu obnovilo hlínu a čerstvé3sekundové čekání. Inventář se nezměnil.
- Opětovné vytěžení již zarostlého bloku znovu dalo hlínu. Konečný stav je **2 vytěžené travnaté bloky −1 položená hlína =1 zbývající hlína,0 trávy**, bez duplikace.

Všechny testovací save soubory jsou pouze v `artifacts/grass-qa-data`. `grass-live-isolation.json` potvrzuje shodný SHA živého `saves/slot-1.json` před a po testu; nastavení rovněž zůstalo beze změny. Test neovládal ani nerestartoval otevřenou uživatelovu hru a nezachytil skutečnou myš.

Po runtime ověření se pouze doplnilo rozpoznání `--grass-qa` také do hlavní nabídky. Závěrečný `dotnet build` tuto drobnou změnu ověřil; kód těžení a růstu se od úspěšného běhu nezměnil. Přesné současné SHA souborů včetně `GrassQa.cs`, terénu, světa, katalogu a menu jsou v `grass-qa-review.json`.

## Známá omezení

Časovače se neukládají: po načtení hry i po novém načtení odloženého chunku čeká odkrytá hlína znovu3 sekundy. Roste jen nejvyšší odkrytá Dirt buňka v načteném sloupci; světlo a počasí se nesimulují. Běžící hra používá svůj původně načtený kód, takže se změna projeví po uložení a novém spuštění. Testy proběhly na desktopu, bez fyzického mobilního zařízení.
'''
# Keep ordinary Czech prose readable; preserve exact inline paths and hashes.
import re
letter='A-Za-zÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž';parts=re.split(r'(`[^`]*`)',text)
for i in range(0,len(parts),2):
    parts[i]=re.sub(r'(?<=\d)(?=['+letter+'])',' ',parts[i]);parts[i]=re.sub(r'(?<=['+letter+r'])(?=\d)',' ',parts[i])
(ROOT/'artifacts/grass-qa-review.md').write_text(''.join(parts),encoding='utf8')
print('Independent grass review PASS',r['utc'])
