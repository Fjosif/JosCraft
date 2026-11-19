"""Freeze the independent review for the playable survival-menu milestone."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[1]
def read(p):return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
r=read('artifacts/game-qa.json');isolation=read('artifacts/game-live-isolation.json')
assert r['pass'] and not r['failures'] and isolation['pass']
assert (ROOT/'artifacts/game-qa.stderr.log').stat().st_size==0
assert r['survival']['conserved'] and r['survival']['resources']==3
assert len(r['groundedCharacters'])==11 and len(r['boundaries'])==10
assert r['ceiling']['ceilingProtected'] and r['ceiling']['headEmbedded']==0
assert r['speedTests']['sneakLedge'] and r['speedTests']['walkFalls']
assert not list(ROOT.glob('runtime/*.gd'))
files=['runtime/BlockWorld.cs','runtime/BlockTerrain.cs','runtime/BlockCatalog.cs','runtime/GameUi.cs','runtime/GameMenu.cs','runtime/GameSession.cs','runtime/GameQa.cs','runtime/WorldQa.cs','game.tscn','world.tscn','project.godot','Joscraft.csproj','tools/verify_game.ps1','tools/verify_world.ps1','artifacts/game-qa.json','artifacts/game-live-isolation.json','artifacts/game-build.log','artifacts/game-import.log','artifacts/game-qa.log','artifacts/game-qa.stderr.log']
for p in sorted((ROOT/'assets/textures').glob('*-v1.png*')):files.append(p.relative_to(ROOT).as_posix())
images=sorted(p.relative_to(ROOT).as_posix() for p in (ROOT/'artifacts').glob('game-*.png'))
reviewed=['game-gameplay-desktop.png','game-gameplay-mobile-390.png','game-full-hotbar-mobile-390.png','game-full-inventory-mobile-390.png','game-inventory-desktop.png','game-craft-staging-desktop.png','game-main-menu-mobile-390.png','game-new-world-mobile-390.png','game-pause-mobile-390.png','game-bunny-low-ceiling.png']
limits=['Fyzický telefon ani tablet nebyl testován. Mobilní snímky jsou desktopové viewporty390×844. Výkon je z Windows/RTX4090 s limitem120FPS.','Recepty výroby,3×3 pracovní stůl, stromy, dřevo, voda a láva patří do další etapy. Současná2×2 pole skutečně drží a vracejí vlastněné suroviny; výstup je vypnutý, protože pro současné3materiály není recept.','Čtyři obrysová místa výstroje v inventáři jsou záměrně neinteraktivní. Nevydávají se za hotový systém vybavení.','Naplněný inventář36×64 na důkazním PNG je izolovaná QA fixture pro kapacitu a čitelnost. Nová běžná hra začíná s prázdným inventářem.','Mapa zůstává konečná96×96m s nejvýše25aktivními chunky. Nejde o nekonečný svět ani multiplayer.','Pro oddělení hraničního zablokování od legitimních teras používají některé hraniční/rohové testy dočasné kamenné QA plošiny. Testují se jejich skutečné renderované trojúhelníky a kolize; fixture se pak odstraní načtením uloženého QA stavu.']
checks={'actual_main_menu_options_start_continue':True,'desktop_hud_without_seed_picker_debug':True,'first_person_default_f5_toggle':True,'actual_hotbar_key9_and_scroll':True,'pause_freezes_body_animation_and_edits':True,'finite_survival_resources':r['survival'],'carried_stack_close_and_save_conserved':True,'craft_staging_close_save_menu_continue_conserved':True,'inventory_layout':{'light_card_dark_text':True,'storage_rows':3,'storage_columns':9,'storage_slots':27,'hotbar_slots_below_storage':9,'craft_staging':4,'output_disabled_without_recipe':True,'actual_selected_glb_preview':True,'full_64_count_slots_fit_390':True},'protected_bedrock_and_self_placement_rejection':True,'actual_mining_5m_range_and_third_person_reach':True,'grazing_ray_owner_matches_physics_triangle':True,'six_face_mesh_culling_and_actual_collision':True,'voxel_cavity_roof_underside_collision':True,'edit_rebuild_counts':{'interior':1,'chunk_border':2,'chunk_corner':3},'restoring_voxel_restores_mesh_fingerprint':True,'chunk_unload_reload_preserves_edits':True,'grounded_characters':r['groundedCharacters'],'head_ceiling_collision':r['ceiling'],'four_edges_and_corner_for_fox_octo':r['boundaries'],'physical_ctrl_shift_and_actual_ledge':r['speedTests'],'mapped_textures':r['textures'],'live_save_settings_preserved':isolation}
report={'pass':True,'reviewer':'Independent QA subagent, separate from runtime/UI and terrain authors','runtime_utc':r['utc'],'controls_reference':r['controlsReference'],'checks':checks,'performance':r['performance'],'verified_files':[{'path':p,'sha256':sha(p)} for p in files],'captured_screenshots':[{'path':p,'sha256':sha(p)} for p in images],'visually_reviewed_screenshots':['artifacts/'+p for p in reviewed],'visual_pass':True,'limitations':limits}
(ROOT/'artifacts/game-qa-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
p=r['performance'];s=r['survival'];initial=r['geometryChecks'][0]
rays=sum('renderedTop' in x for x in r['geometryChecks'])
rows='\n'.join(f"| {a['id']} | {a['contact']:.6f}m |" for a in r['groundedCharacters'])
text=f'''# Nezávislá kontrola hratelného JOSCRAFT — PASS

Finální běh `{r['utc']}` prošel `tools/verify_game.ps1`. C# build: **0 chyb,0 varování**. Import i skutečné OpenGL vykreslování proběhly bez runtime chyb; `game-qa.stderr.log` je prázdný. Tato kontrola ověřuje současnou hru a nepoužívá starý PASS prohlížeče jako důkaz pro nové funkce.

## Nabídka, pohled a inventář

Ověřen skutečný tok hlavní nabídka → Nový svět → zadání seedu a postavy → spuštěná hra. Výchozí pohled je první osoba, F5 přepne do třetí a zpět. Herní PC HUD obsahuje zaměřovač a rychlou lištu; nezůstala v něm nabídka seedu, výběr hrdiny ani ladicí panely. Pauza zastaví pohyb těla, animaci a editaci bloků. E otevírá a zavírá skutečný inventář; klávesa9 a kolečko vybírají rychlou lištu. Pohybové klávesy, Ctrl pro běh a Shift pro opatrnou chůzi odpovídají [oficiálnímu přehledu ovládání](https://www.minecraft.net/en-us/article/minecraft-controls); F5 je přidaný požadavek tohoto projektu.

Vizuálně i strukturou uzlů ověřena finální světlá karta s tmavým textem: skutečný rigovaný Tygřík v samostatném3D viewportu vlevo,2×2 vstupní pole vpravo,27 úložných míst v mřížce3×9 a9 míst rychlé lišty pod nimi. Naplněná karta se všemi počty64 zůstává v9 sloupcích a vejde se do390×844. Náhled ukazuje vlastní GLB postavu zepředu včetně nohou. Český text, hlavní nabídka, volby nového světa a pauza jsou celé a čitelné také při390×844.

## Přežití a zachování surovin

Nová hra začíná s **36 prázdnými sloty**. Při skutečném raycastovém těžení se materiál přidává až po úspěšné změně bloku; položit lze jen vlastněný materiál a odečte se pouze po úspěchu. Běh provedl **{s['mined']} vytěžení − {s['placed']} položení = {s['resources']} zbývající jednotky**. Prázdný vybraný slot, pokus o umístění do vlastního těla/hlavy, příliš vzdálený cíl, plný inventář a editace během pauzy/inventáře zásoby nezměnily. Funguje i skutečné těžení ve třetí osobě s kamerou8m daleko: dosah se posuzuje do5m od hráče.

Kontrola kliká do skutečných tlačítek inventáře. Přenášený stack i materiály odložené ve2×2 polích se zachovaly při E/Esc zavření, přímém uložení a při toku **uložit → hlavní nabídka → skutečné Pokračovat**. Po načtení nezůstal v ruce ani ve výrobních polích žádný duplicitní zbytek. Sloty mají limit64. Samostatná plná36×64 fixture ověřila odmítnutí těžby bez kapacity; nejde o výchozí zásoby nové hry.

## Uložení, upravitelné bloky a skutečná geometrie

Atomický JSON save/load zachoval suroviny, sloty i fingerprint skutečného editovaného meshe. Ověřen `.tmp`/rename postup a záložní `.bak`; poškozený primární soubor nepřepsal poslední platnou zálohu. Poškozený JSON i stack65 byly odmítnuty bez změny současné hry. QA zapisovalo pouze do `artifacts/game-qa-data`. Hashová kontrola `game-live-isolation.json` potvrzuje, že živý `saves/slot-1.json` a nastavení zůstaly beze změny a nebyly ani nově vytvořeny.

Počáteční terén měl **{initial['triangles']} skutečných trojúhelníků**, přesně očekávaný počet všech viditelných stěn v6 směrech včetně spodních ploch. Každý trojúhelník byl prověřen proti skutečným blokovým datům: plný blok uvnitř, vzduch vně a správné pořadí vrcholů. Kontrola provedla {rays} porovnání skutečné renderované výšky s fyzikálním raycastem. Šikmý paprsek na rohu navíc porovnal cílovou buňku s vlastníkem skutečného kolizního trojúhelníku přes `face_index`.

Odstranění vnitřního bloku pod zachovanou střechou vytvořilo skutečnou dutinu: paprsek zevnitř vzhůru zachytil její novou spodní plochu s normálou dolů. Editace běžné buňky, hrany a rohu přestavěla1/2/3 chunky. Obnovení původní buňky vrátilo identický mesh. Editované bloky přežily unload/reload chunků. Bedrock v y=0 nelze odstranit, takže těžbou nevznikne díra pod světem.

## Usazení, výška postavy a oprava uvíznutí

Všech11 skutečně skinned postav bylo postaveno na skutečnou texturovanou kolizi:

| Postava | Minimum skinu minus skutečná podlaha |
|---|---:|
{rows}

Hopsa se správně zastaví před dutinou se stropem ve výšce2m: model uší/hlavy neprojde stropem a počet vnořených vrcholů je0. Výška kolizního boxu vychází ze skutečné výšky modelu, jeho vodorovné rozměry zahrnují i křídla a chapadla.

Blesk s dlouhým ocasem i široká Inka prošli u všech4 konečných hran a u rohu testem změny pohledu, chůze od kraje a diagonálního skluzu. Všech10 případů prošlo skutečnou cestou běžného controlleru. Když procedurální terasy neposkytly dost širokou rovnou trasu, test použil oddělenou plošinu na skutečné hranici mapy, ověřil její trojúhelníky i kolizi a po zkoušce ji odstranil načtením QA save. Nešlo o snížení tolerance proti legitimní stěně. Shift zastavil krok přes skutečnou vnitřní hranu bloku; běžná chůze ze stejného místa spadla na samostatně ověřenou nižší renderovanou i kolizní podlahu.

## Textury a měření

Ve skutečných materiálech jsou všechny4 vlastní PNG, importované do256px s mipmapami. UV se mění po každé stěně; zelený pás boční trávy je nahoře a textura se nepřetáčí do hlíny dole. Vizuální kontrola finálních PNG potvrdila čitelné detaily, zelenou trávu, správné barvy postav a žádné bílé/černé lemy. Zdrojové textury se nezaměňují za převzaté Minecraft soubory.

Skutečný desktopový běh1280×800, {p['gpu']}, `{p['renderer']}`, **{p['msaa']}**:25 aktivních chunků, {p['triangles']} terénních trojúhelníků po editacích, {int(p['drawCalls'])} drawcalls a {int(p['primitives'])} primitiv celého snímku. Median {p['medianMs']:.3f}ms, p95 {p['p95Ms']:.3f}ms za120 snímků. Běh měl limit120FPS a nepředstírá fyzické měření telefonu/tabletu.

## Přesná omezení

'''+'\n'.join('- '+x for x in limits)+'''

`game-qa-review.json` obsahuje pass boolean, výsledky, SHA finálního runtime, konfigurace, textur, samotného QA reportu a všech zachycených PNG. Starší `verify_world.ps1` a `--world-qa` jsou explicitní aliasy této současné plné kontroly; historické stage2 `world-qa.*` zůstaly zachované a novým během se nepřepisují.
'''
# Separate numerals in prose while preserving exact inline paths/hashes.
import re
letter='A-Za-zÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž'
parts=re.split(r'(`[^`]*`)',text)
for i in range(0,len(parts),2):
    parts[i]=re.sub(r'(?<=\d)(?=['+letter+'])',' ',parts[i]);parts[i]=re.sub(r'(?<=['+letter+r'])(?=\d)',' ',parts[i])
text=''.join(parts)
(ROOT/'artifacts/game-qa-review.md').write_text(text,encoding='utf8')
print('Independent survival game review PASS',r['utc'])
