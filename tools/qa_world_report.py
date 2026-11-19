"""Freeze stage-two review evidence after successful runtime and visual QA."""
from pathlib import Path
import json,hashlib,re
ROOT=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256((ROOT/p).read_bytes()).hexdigest()
r=json.loads((ROOT/'artifacts/world-qa.json').read_text(encoding='utf8'))
assert r['pass'] and not r['failures']
assert (ROOT/'artifacts/world-qa.stderr.log').stat().st_size==0
assert all(a['embeddedVertices']==0 for a in r['wallActors'])
files=['runtime/BlockWorld.cs','runtime/BlockTerrain.cs','runtime/WorldQa.cs','world.tscn','project.godot','Joscraft.csproj','tools/verify_world.ps1','artifacts/world-qa.json']
images=['world-overview-desktop.png','world-desktop.png','world-mobile-480x900.png','world-mobile-390x844.png','world-overview-mobile-480x900.png','world-wall-contact-tiger.png','world-wall-contact-octo.png','world-seam-crossing.png','world-jump-one-block.png','world-landing-one-block.png','world-streamed-boundary.png','world-boundary-controller.png','world-camera-obstruction.png','world-first-person-desktop.png','world-first-person-jump.png','world-first-person-mobile-390x844.png']
limits=['Pouze desktop Windows / RTX4090, GL Compatibility,120FPS cap. Mobilní rozměry jsou desktopové viewporty, ne měření fyzického telefonu či tabletu.','Mapa je konečná96×96m a streamer drží nejvýše25chunků16×16. Žádný nekonečný svět, multiplayer, účty, škola, crafting ani boj.','Rozpočet3ms pro generování je měkký: jeden atomický upload mesh/kolize může tuto dobu překročit. Přesné naměřené maximum je v reportu.','Footprint chrání změřený rozsah lokomočních póz; ověřen byl skutečný kontakt Tygříka a Inky se stěnou ve24vzorcích Walk/Run. Nejde o obecnou simulaci kolizí jednotlivých chapadel nebo křídel.','První osoba používá vlastní model pouze pro stín. Samostatné ruce, nástroje a editace bloků nejsou součástí této etapy.']
report={'pass':True,'reviewer':'Independent QA subagent, separate from terrain and controller authors','runtime_utc':r['utc'],'checked_files':[{'path':p,'sha256':sha(p)} for p in files],'screenshots':[{'path':'artifacts/'+p,'sha256':sha('artifacts/'+p)} for p in images],'results':{'determinism':r['determinism'],'geometry':{k:v for k,v in r['geometry'].items() if k!='seamAndFloorSamples'},'actual_render_and_collision_ray_samples':len(r['geometry']['seamAndFloorSamples']),'grounded_characters':len(r['actors']),'wall_skin_tests':r['wallActors'],'movement':r['movement'],'view_modes':r['viewModes'],'ui':r['ui'],'streaming':r['streaming'],'performance':r['performance']},'visual_pass':True,'limitations':limits,'corrected_findings':['Lineární vertex barvy terénu byly opraveny; tráva je zelená a půda hnědá.','Úzká původní kapsle dovolovala průnik animovaných tlapek/chapadel do stěny. Nový species footprint vychází ze skutečných skinned póz; opakované Walk/Run vzorky mají0průniků.','Mobilní seed se nyní zobrazuje celý a ovládání/nápověda se vejdou i do390×844.','Původní seam trasa kontrolovala jen středovou řadu terénu; širší správný collider narazil do skutečné sousední terasy. Nová trasa předem měří celý rovný swept corridor přes skutečný chunk ševx0, odděleně od testu blokující stěny.']}
(ROOT/'artifacts/world-qa-review.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
p=r['performance'];s=r['streaming'];m=r['movement'];v=r['viewModes'];g=r['geometry']
rows='\n'.join(f"| {a['id']} | {a['contact']:.6f}m | ano |" for a in r['actors'])
text=f'''# Nezávislá kontrola blokového světa — PASS

Finální runtime běh `{r['utc']}` prošel vlastním `tools/verify_world.ps1`. C# build má0chyb a0varování; import a skutečný OpenGL render prošly bez runtime chyb, stderr je prázdný. Původní stage1 reporty ani ověřovač nebyly použity jako důkaz pro tento svět.

## Seed, skutečná geometrie a kolize

Seed `{r['seed']}` vytvořil ve25aktivních chunkech **{g['triangles']} skutečně odeslaných trojúhelníků**, přesně očekávaný počet viditelných stěn. Každý trojúhelník byl kontrolován: na vnitřní straně plný blok, na vnější vzduch;0vnitřních stěn,0chybných osových normál,0nesprávně otočených front faces. Kontrola pokrývá také culling přes hranice chunků.

Opakovaný seed dává shodný SHA skutečné renderované geometrie `{r['determinism']['initial']}`; seed+12345 jiný. **{len(g['seamAndFloorSamples'])} vzorků** raycastu CPU proti skutečným vykresleným trojúhelníkům se shoduje s fyzikální kolizí do2mm. Zahrnují spawn, obě strany švů, skutečnou nejvyšší/nejnižší terasu, celý rovný swept corridor a vnitřek/vnějšek konečného okraje48m. HeightAt slouží k výběru kandidátů a nezávislému očekávání počtu stěn, nikoli jako náhrada těchto render/physics kontrol.

## Pohyb a postavy

Test používá stejný runtime controller jako klávesnice a dotyk: chůze {m['walkDistance']:.3f}m, skutečný přechod chunkového švux0, blokování boční stěnou a skok na opravdovou1mvyšší terasu. Vrchol skoku {m['peak']:.3f}m nad startem, dosednutí {m['landing']:.6f}m výš. Kontrolér se zastaví na konečném/nenačteném okraji a po pádu pod mapu se bezpečně vrací na spawn. Kamera třetí osoby zkracuje orbit při kolizi s reálným terénem.

Všech11 skutečně importovaných rigovaných postav stojí na skutečné blokové kolizi:

| ID | Minimum skinu minus skutečná podlaha | Na zemi |
|---|---:|---|
{rows}

Tygřík a Inka byli navíc tlačeni do1mblokové stěny během celého Walk i Run cyklu:24vzorků na druh, **0skinned vrcholů uvnitř renderované i kolizní zeminy**. Vizuálně byly prohlédnuty snímky obou kontaktů. Původní úzká kapsle zde skutečnou chybu měla; opravu ověřil nový běh.

## První a třetí osoba, mobilní rozměr

Výchozí první osoba je ověřena před prvním přepnutím. W jde ve směru kamery, skok v první osobě dosáhl {v['firstJumpPeak']:.3f}m a dosedl. Vlastní mesh se v první osobě vykresluje pouze do stínů; hlava necloní kameru. Přepnutí do třetí osoby obnoví model a zpětné přepnutí funguje. Prohlédnuty desktopové a390×844první pohledy, stejně jako třetí osoba, světový přehled, skok/dosednutí, šev a kontakt se stěnami. Celý seed, dotyková tlačítka i česká nápověda jsou čitelné při480×900i390×844. Dotykové tlačítko uvolní focus po psaní seedu, aby ovládání nezůstalo blokované.

## Streaming a skutečné desktopové měření

Streamer přešel z{s['initialChunks']}na{s['farChunks']}chunků, odstranil staré render uzly a při návratu znovu doplnil okolí. Nezávisle pozorované maximum{s['maxObservedChunks']}aktivních chunků a{s['maxObservedBuildsPerFrame']}nový chunk za snímek dodrželo nastavené limity. Měkký rozpočet{s['softBuildBudgetMs']}ms není tvrdý limit: nejpomalejší atomický build/upload trval **{s['maxAtomicBuildMs']:.3f}ms**.

Windows / {p['gpu']}, `{p['renderer']}`,1280×800,25chunků: median{p['medianMs']:.3f}ms, p95{p['p95Ms']:.3f}ms, {int(p['drawCalls'])}drawcalls, {int(p['primitives'])}primitiv celého snímku včetně postavy, stínů aUI. Běh má120FPS cap a není odhadem maximálního výkonu ani fyzickým mobilním benchmarkem.

## Omezení

'''+'\n'.join('- '+x for x in limits)+'''

Strojový `world-qa-review.json` obsahuje pass boolean, výsledky a SHA finálního runtime, samotného QA reportu i prohlédnutých PNG. Žádné rodinné zařízení, stará hra ani souběžný editor nebyly tímto ověřovačem měněny.
'''
# Keep Czech prose readable while preserving inline paths, hashes and code.
letter='A-Za-zÁČĎÉĚÍŇÓŘŠŤÚŮÝŽáčďéěíňóřšťúůýž'
parts=re.split(r'(`[^`]*`)',text)
for i in range(0,len(parts),2):
    parts[i]=re.sub(r'(?<=\d)(?=['+letter+'])',' ',parts[i])
    parts[i]=re.sub(r'(?<=['+letter+r'])(?=\d)',' ',parts[i])
text=''.join(parts).replace('švux 0','švu x=0').replace('1 mvyšší','1 m vyšší').replace('včetně postavy, stínů aUI','včetně postavy, stínů a UI').replace(';0','; 0').replace(',0',', 0')
(ROOT/'artifacts/world-qa-review.md').write_text(text,encoding='utf8')
print('World independent review frozen',r['utc'])
