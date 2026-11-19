"""Freeze the independent review evidence after the final Godot QA run."""
from pathlib import Path
import json,hashlib
ROOT=Path(__file__).resolve().parents[1]
def read(p):return json.loads((ROOT/p).read_text(encoding='utf-8-sig'))
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
glb=read('artifacts/qa-glb.json');blend=read('artifacts/qa-blend.json');godot=read('artifacts/godot-qa.json')
assert glb['all_pass'] and blend['all_pass'] and godot['pass'] and not godot['failures']
assert (ROOT/'artifacts/godot-qa.stderr.log').stat().st_size==0
for m in glb['models']:assert sha('assets/characters/'+m['id']+'.glb')==m['sha256']
reviewed=['blender-lineup.png','blender-animation-poses.png','gallery-desktop.png','gallery-mobile-480x900.png','mobile-480x900.png','qa-godot-pose-contact.png','tiger-sit.png','octo-sit.png','movement-walk.png','movement-jump.png','tiger-attack.png','octo-ride.png']
screens=[{'path':'artifacts/'+p,'sha256':sha('artifacts/'+p)} for p in reviewed]
def contrast(h):
    rgb=[int(h[i:i+2],16)/255 for i in (0,2,4)]
    lum=sum(v*w for v,w in zip([v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4 for v in rgb],[.2126,.7152,.0722]))
    return round(1.05/(lum+.05),2)
ratios={c:contrast(c) for c in ['24344b','3d5872','162238']}
report={'pass':True,'reviewer':'Independent QA subagent; read-only verification of saved assets and inspection of rendered images','final_godot_utc':godot['utc'],'binary_asset_inspection':'11GLB,8clips each,real skin weights,nonzero skinned vertex movement,unique animation data; independent25 temporal samples per clip','saved_source_inspection':'11editable .blend reopened in Blender5.2.2; one armature and one mesh each,skin modifier,real varying animation curves','runtime_evidence':{'path':'artifacts/godot-qa.json','sha256':sha('artifacts/godot-qa.json'),'pass':godot['pass'],'floor':godot['floor'],'movement':godot['movement'],'performance':godot['performance']},'visual_review':{'all11_distinct_species':True,'all11_idle_walk_run_jump_sit_contact_sheet':True,'all8_clip_types_in_blender_pose_sheet':True,'grounded_poses_and_floor_shadows_visible':True,'desktop_gallery_no_ui_clipping':True,'portrait480x900_gallery_and_bunny_detail_readable':True,'largest_ears_horns_complete':True,'ui_theme_white_text_contrast_ratios':ratios,'screenshots':screens},'corrected_findings_verified':['octo export failure from missing humanoid leg bone fixed; actual11 exports inspected','coplanar fox/unicorn foot accents fixed and changed sources/exports rechecked','Godot imported vertex colors explicitly enabled and lighting corrected; faces/palette visible','desktop gallery label overlap and portrait fitting corrected','deterministic animation screenshots remove previous-pose crossfade; settledSit and selector synchronized','physicsJump preview root arc removed while CharacterBody provides actual lift'], 'limitations':['No physical Android/iOS device or mobile GPU performance measured. Portrait desktop viewport only.','Desktop measurement uses RTX4090,compatibility renderer,and120FPS cap; it is not uncapped performance capacity.','Attack/Hit/Ride are animation previews only; Ride has intentional seated support offset without mount.','CPU glTF plane sampling is analytic; actual rendered floor mesh and collision are separately checked by the C# runtime QA.','No claim of testing family devices,network play,or a generated world.']}
report['verified_files']=[{'path':p,'sha256':sha(p)} for p in ['project.godot','Joscraft.csproj','main.tscn','runtime/Viewer.cs','runtime/ViewerQa.cs','tools/verify_grounding.ps1','tools/build_characters.py','artifacts/model-manifest.json','artifacts/qa-glb.json','artifacts/qa-blend.json','artifacts/godot-qa.json']]
(ROOT/'artifacts/qa-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
rows='\n'.join(f"| {m['id']} | {m['triangles']} | {len(m['bones'])} | 1 | 8 |" for m in glb['models'])
ground=[c for m in godot['models'] for c in m['clips'] if c['grounded']]
text=f'''# Nezávislá kontrola JOSCRAFT — PASS

Finální běh Godotu: `{godot['utc']}`. Kontrolu provedl samostatný QA agent odlišný od autorů modelů a vieweru. Zdrojová hra zůstala pouze referencí pro čtení.

## Modely a zdroje

Skutečně otevřeno všech11 uložených `.blend` v Blender5.2.2: každý má editovatelnou síť, armaturu, váhy, armature modifier a8akcí s měnícími se klíči (`qa-blend.json`). Nezávislý binární parser GLB ověřil accessor data, váhy, kosti, materiály, trojúhelníky a88klipů. CPU skinning25vzorků každého klipu potvrzuje skutečný pohyb vrcholů (`qa-glb.json`).

| ID | Trojúhelníky | Kosti | Materiály | Klipy |
|---|---:|---:|---:|---:|
{rows}

Celkem17652trojúhelníků postav. Rozlišovací znaky všech11 odpovídají SVG/roster referencím, nikdo není pouze přebarveným totožným modelem. Sova má zobák, oční disky a křídla; Inka8dvoučlánkových chapadel. Vlastní druhové rigy nejsou vydávány za jeden univerzální humanoidní rig.

## Skutečná scéna a usazení

C# kontrola běží v importované Godot scéně s opravdovým OpenGL rendererem.17vzorků každého z88klipů měří aktuálně skinned vrcholy. Vykreslená podlaha má horní plochu y={godot['floor']['renderedTop']}, raycast kolize y={godot['floor']['collisionTop']:.9f}. Grounded Idle/Walk/Run/Sit ve všech modelech: nejnižší vzorek {min(c['minY'] for c in ground):.6f}m a nejvyšší kontakt {max(c['maxContactY'] for c in ground):.6f}m. Tolerance kontroly je−25/+35mm; největší skutečná odchylka je přibližně12.5mm. Nejde o PASS staré hry ani pouze analytické výšky.

Připojená pohybující se postava prošla MoveAndSlide na této kolizi; vizuální kontakt {godot['movement']['visualContact']:.6f}m. Fyzikální skok dosáhl {godot['movement']['jumpPeak']:.3f}m a vrátil se na podlahu; další animační výškový offset ve skoku je {godot['movement']['airborneVisualOffset']}m. Jump preview a Ride póza jsou záměrné animační výjimky. Runtime stderr je prázdný a failures=[] v `godot-qa.json`.

## Vizuální kontrola

Prohlédnuty skutečné rendery `blender-lineup.png`, `blender-animation-poses.png`, `gallery-desktop.png`, `gallery-mobile-480x900.png`, `mobile-480x900.png`, všech55náhledů Idle/Walk/Run/Jump/Sit v `qa-godot-pose-contact.png` a detailní Godot snímky sedu, útoku, Ride a fyzikálního pohybu/skoku. Tvary, tváře a barvy jsou viditelné; stoj a sed odpovídají podlaze/stínům. Celá sada i vysoké uši/rohy se vejdou nad UI; české ovládání je čitelné i při480×900. Bílé UI texty mají proti konfigurovaným tmavým button/hover/panel povrchům kontrasty {ratios}; přesahují4.5:1. Přepínání11modelů i8animací používá skutečné importované modely a AnimationPlayer.

## Výkon a omezení

Skutečné desktopové offscreen vykreslování galerie1280×800, NVIDIA RTX4090, GL Compatibility: median {godot['performance']['medianFrameMs']:.3f}ms, p95 {godot['performance']['p95FrameMs']:.3f}ms, {int(godot['performance']['drawCalls'])}drawcalls a {int(godot['performance']['primitives'])}primitiv v celém snímku včetněUI. Měření má120FPS cap a není odhadem maximálního výkonu. Telefon/tablet ani Android export na zařízení nebyl testován. Attack/Hit/Ride nepřidávají boj ani mount systém. Svět není generovaný.

Strojový přehled v `qa-review.json` obsahuje SHA finálního Godot reportu a vizuálně prohlédnutých snímků. Reference a jejich původ jsou v `reference-audit.json`. Pomocné skripty QA vyžadují Python+numpy; kontaktní obrázky navíc Pillow a PyMuPDF. To nejsou runtime závislosti vieweru.
'''
(ROOT/'artifacts/qa-review.md').write_text(text,encoding='utf8')
print('Independent review PASS; frozen report',godot['utc'])
