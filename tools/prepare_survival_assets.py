"""Extract generated atlas cells; create original code-native inventory symbols."""
from pathlib import Path
from PIL import Image
import json, shutil
ROOT=Path(__file__).resolve().parents[1]
source=Path(r'C:\Users\fjosi\.codex\generated_images\01a1d4c5-c83e-7751-81f5-c818413cb95e\exec-cf7d194e-305e-4787-8388-6659bd9ecbf2.png')
atlas=ROOT/'source/textures/survival-atlas-v1.png'
if source.exists(): shutil.copy2(source,atlas)
im=Image.open(atlas)
names=['oak-planks','birch-planks','spruce-planks','bricks','clay','cobblestone','table-top','table-side']
for i,name in enumerate(names):
    col,row=i%4,i//4
    cell=im.crop((round(col*im.width/4),round(row*im.height/2),round((col+1)*im.width/4),round((row+1)*im.height/2)))
    path=ROOT/f'assets/textures/{name}-v1.png';cell.save(path)
source2=Path(r'C:\Users\fjosi\.codex\generated_images\01a1d4c5-c83e-7751-81f5-c818413cb95e\exec-49b1ae51-817c-470b-8fa0-b9bf9d73c5fc.png')
atlas2=ROOT/'source/textures/furnace-atlas-v1.png'
if source2.exists(): shutil.copy2(source2,atlas2)
im2=Image.open(atlas2)
for i,name in enumerate(['coal-ore','furnace-front','furnace-top','furnace-side']):
    x,y=i%2,i//2
    im2.crop((round(x*im2.width/2),round(y*im2.height/2),round((x+1)*im2.width/2),round((y+1)*im2.height/2))).save(ROOT/f'assets/textures/{name}-v1.png')
icons=ROOT/'assets/icons';icons.mkdir(exist_ok=True)
shapes={
 'stick':'<path d="M19 51L46 12" stroke="#553521" stroke-width="9"/><path d="M21 49L47 13" stroke="#c69a59" stroke-width="4"/>',
 'coal':'<path d="M12 45L8 28L24 10L47 17L55 40L35 54Z" fill="#30343d" stroke="#171d28" stroke-width="3"/><path d="M14 29L27 18L41 22L30 32Z" fill="#68707c"/>',
 'clay':'<path d="M9 42Q4 23 25 16Q43 8 53 27L52 45Q31 58 9 42" fill="#8796a5" stroke="#556375" stroke-width="3"/>',
 'brick':'<path d="M7 25L43 15L57 25L57 43L22 53L7 43Z" fill="#a84f35" stroke="#5b302a" stroke-width="3"/><path d="M7 25L22 35L57 25M22 35V53" fill="none" stroke="#f2a66b" stroke-width="3"/>',
 'bone':'<path d="M15 11Q4 6 8 20L20 26L38 46Q34 59 46 54Q58 60 55 47L44 38L26 20Q29 5 18 9Z" fill="#eee7d3" stroke="#9b937f" stroke-width="3"/>',
 'carrot':'<path d="M19 25L48 19L16 56Z" fill="#ed8c2f" stroke="#ab4c26" stroke-width="3"/><path d="M37 22L33 5M39 21L54 8M40 22L59 22" stroke="#55904e" stroke-width="7"/>',
 'berries':'<path d="M32 22L24 8L45 12L40 25" fill="#609447"/>'+''.join(f'<circle cx="{x}" cy="{y}" r="11" fill="{c}" stroke="#61243c" stroke-width="2"/>' for x,y,c in [(20,32,'#c04c6b'),(40,31,'#db6282'),(30,47,'#ab3e60')])
}
for mat,color in [('wood','#b18148'),('stone','#9ba6ae')]:
 for tool in ['pickaxe','axe','sword']:
    base='<path d="M17 53L43 19" stroke="#573a29" stroke-width="9"/><path d="M19 51L44 19" stroke="#c89e62" stroke-width="4"/>'
    head={'pickaxe':f'<path d="M12 19L18 9L48 15L57 36L45 25L22 21Z" fill="{color}" stroke="#343c42" stroke-width="3"/>', 'axe':f'<path d="M22 8L48 15L42 36L15 32L12 18Z" fill="{color}" stroke="#343c42" stroke-width="3"/>', 'sword':f'<path d="M19 44L47 6L56 8L53 21L28 49Z" fill="{color}" stroke="#343c42" stroke-width="3"/><path d="M15 36L37 53" stroke="#d6b966" stroke-width="6"/>'}[tool]
    shapes[f'{mat}-{tool}']=base+head
for name,shape in shapes.items():
 (icons/f'{name}.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">'+shape+'</svg>',encoding='utf-8')
print('Extracted8 original atlas cells; created13 inventory SVG symbols.')
