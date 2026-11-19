"""Render a reference-only contact sheet without touching the source project."""
from pathlib import Path
import pymupdf
import re
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT.parent/'DetiSkola'/'deployment'/'oliver'/'pets'
ids=['tiger','fox','dragon','unicorn','robot','owl','panda','cat','bunny','bear','octo']
out=Image.new('RGB',(1200,900),'#edf0f4');draw=ImageDraw.Draw(out)
for n,ident in enumerate(ids):
    # MuPDF does not support SVG paint-server gradients. Resolve the roster's
    # base stop into a flat fill for this clearly documented reference preview.
    source=(SRC/f'{ident}.svg').read_text(encoding='utf8')
    palette={}
    for gradient in ET.fromstring(source).iter():
        if not gradient.tag.endswith('Gradient'): continue
        stops=[(s.attrib.get('stop-color','#ffffff'),s.attrib.get('data-stop-color','')) for s in gradient]
        palette[gradient.attrib['id']]=next((c for c,k in stops if k in ('fur','belly')),stops[-1][0])
    source=re.sub(r'url\(#([^\)]+)\)',lambda m:palette.get(m[1],'#ff00ff'),source)
    svg=pymupdf.open('svg',source.encode('utf8'))
    pdf=pymupdf.open('pdf',svg.convert_to_pdf())
    pix=pdf[0].get_pixmap(matrix=pymupdf.Matrix(.4,.4),alpha=True)
    im=Image.frombytes('RGBA',(pix.width,pix.height),pix.samples)
    im.thumbnail((270,260));x=(n%4)*300+(300-im.width)//2;y=(n//4)*300
    out.paste(im,(x,y),im);draw.text(((n%4)*300+15,y+275),ident,fill='#182238')
draw.text((910,875),'SVG gradients flattened to roster base colors',fill='#182238')
out.save(ROOT/'artifacts'/'qa-reference-contact.png')
