"""Import factual recipe patterns/quantities, not Minecraft art or executable code.
Reference clone stays ignored under build/. Generated catalogue records its exact commit.
"""
from pathlib import Path
import json, re, subprocess, hashlib, csv
from collections import Counter
ROOT=Path(__file__).resolve().parents[1]
repo=ROOT/'build/mojang-reference'
def read(path):
    s=path.read_text(encoding='utf-8-sig')
    s=re.sub(r'("(?:\\.|[^"\\])*")|//[^\n]*|/\*[\s\S]*?\*/',lambda m:m.group(1) or '',s)
    return json.loads(re.sub(r',\s*([}\]])',r'\1',s))
def ingredient(value):
    if isinstance(value,list):return [ingredient(x) for x in value]
    if isinstance(value,str):return {'id':value if ':' in value else 'minecraft:'+value,'count':1,'tag':False}
    if isinstance(value,dict):
        name=value.get('item',value.get('tag',''))
        if not name:return {'id':'','count':1,'tag':False}
        return {'id':name if ':' in name else 'minecraft:'+name,'count':value.get('count',1),'tag':'tag' in value,**({'data':value['data']} if 'data' in value else {})}
    return {'id':str(value),'count':1,'tag':False}
recipes=[]
for path in sorted((repo/'behavior_pack/recipes').glob('*.json')):
    original=read(path)
    for key,value in original.items():
        if not key.startswith('minecraft:recipe_'):continue
        kind=key.removeprefix('minecraft:recipe_')
        r={'id':value.get('description',{}).get('identifier',path.stem),'kind':kind,'file':path.name,'stations':value.get('tags',[])}
        if 'pattern' in value:r['pattern']=value['pattern'];r['keys']={k:ingredient(v) for k,v in value.get('key',{}).items()}
        if 'ingredients' in value:r['ingredients']=[ingredient(i) for i in value['ingredients']]
        for field in ['input','reagent','base','addition','template']:
            if field in value:r[field]=ingredient(value[field])
        output=value.get('result',value.get('output',[]))
        r['outputs']=[ingredient(i) for i in output] if isinstance(output,list) else [ingredient(output)]
        if 'priority' in value:r['priority']=value['priority']
        recipes.append(r)
commit=subprocess.check_output(['git','-C',str(repo),'rev-parse','HEAD'],text=True).strip()
materials={}
def record(x,recipe):
    if isinstance(x,list):
        for v in x:record(v,recipe)
    elif isinstance(x,dict) and 'id' in x:
        if not x['id']:return
        k=('#' if x.get('tag') else '')+x['id'];materials.setdefault(k,set()).add(recipe)
for r in recipes:
    for x in list(r.get('keys',{}).values())+r.get('ingredients',[])+r['outputs']+[r.get(f) for f in ['input','reagent','base','addition','template']]:record(x,r['id'])
data={'source':{'repository':'https://github.com/Mojang/bedrock-samples','commit':commit,'bedrockVersion':read(repo/'version.json')['latest']['version'],'recipeFiles':len(list((repo/'behavior_pack/recipes').glob('*.json'))),'scope':'All recipes in official Bedrock sample behavior_pack/recipes at this commit; not a claim to cover every Java-only or special hardcoded mechanic.'},'recipes':recipes,'materials':[{'id':k,'recipeCount':len(v)} for k,v in sorted(materials.items())]}
(ROOT/'data').mkdir(exist_ok=True)
dest=ROOT/'data/recipe-catalog.json';dest.write_text(json.dumps(data,ensure_ascii=False,separators=(',',':')),encoding='utf-8')
report={'recipes':len(recipes),'files':data['source']['recipeFiles'],'kinds':dict(Counter(r['kind'] for r in recipes)),'materials':len(materials),'commit':commit,'bedrockVersion':data['source']['bedrockVersion'],'sha256':hashlib.sha256(dest.read_bytes()).hexdigest()}
(ROOT/'docs/recipe-import.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
mapping={k:int(v) for k,v in re.findall(r'\["([^"]+)"\]=(\d+)',(ROOT/'runtime/RecipeCatalog.cs').read_text(encoding='utf-8'))}
with (ROOT/'docs/material-roadmap.csv').open('w',newline='',encoding='utf-8-sig') as f:
    writer=csv.writer(f);writer.writerow(['Minecraft ID or tag','Recipe dependencies','JOSCRAFT stable ID','State'])
    for key,recipes_using in sorted(materials.items(),key=lambda pair:(-len(pair[1]),pair[0])):
        stable=mapping.get(key.replace('minecraft:',''), '')
        writer.writerow([key,len(recipes_using),stable,'ingredient tag' if key.startswith('#') else 'available' if stable else 'to prepare'])
print(json.dumps(report,indent=2))
