"""Make a labeled contact sheet from actual Godot screenshots (no scene edits)."""
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
ids=['tiger','fox','dragon','unicorn','robot','owl','panda','cat','bunny','bear','octo']
clips=['idle','walk','run','jump','sit']
out=Image.new('RGB',(1250,2200),'#edf0f4');draw=ImageDraw.Draw(out)
for row,ident in enumerate(ids):
    for col,clip in enumerate(clips):
        img=Image.open(ROOT/'artifacts'/f'{ident}-{clip}.png')
        img=img.crop((390,125,890,504));img.thumbnail((245,175))
        x=col*250;y=row*200
        out.paste(img,(x,y));draw.text((x+5,y+178),f'{ident} / {clip}',fill='#172236')
out.save(ROOT/'artifacts'/'qa-godot-pose-contact.png')
