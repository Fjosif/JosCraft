"""Read-only verification of saved, editable Blender sources."""
import bpy,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
report=[]
for path in sorted((ROOT/'source').glob('*.blend')):
    if args and path.stem not in args:continue
    bpy.ops.wm.open_mainfile(filepath=str(path))
    arms=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    clips={}
    for action in bpy.data.actions:
        curves=[]
        for layer in action.layers:
            for strip in layer.strips:
                for slot in action.slots:
                    bag=strip.channelbag(slot)
                    if bag:curves+=list(bag.fcurves)
        clips[action.name]={'curves':len(curves),'keyframes':sum(len(c.keyframe_points) for c in curves),'varying_curves':sum(max(k.co.y for k in c.keyframe_points)-min(k.co.y for k in c.keyframe_points)>1e-6 for c in curves if c.keyframe_points)}
    ok=len(arms)==1 and len(meshes)==1 and all(c in clips and clips[c]['varying_curves']>0 for c in ['Idle','Walk','Run','Jump','Attack','Hit','Sit','Ride']) and any(m.type=='ARMATURE' and m.object==arms[0] for m in meshes[0].modifiers)
    report.append({'id':path.stem,'armatures':len(arms),'meshes':len(meshes),'bones':[b.name for b in arms[0].data.bones] if arms else [],'vertex_groups':len(meshes[0].vertex_groups) if meshes else 0,'clips':clips,'pass':ok})
if args:
    old=json.loads((ROOT/'artifacts'/'qa-blend.json').read_text(encoding='utf8'))
    updates={r['id']:r for r in report}
    report=[updates.get(r['id'],r) for r in old['sources']]
result={'scope':'Read-only independent reopening of saved .blend sources in Blender5.2.2','all_pass':len(report)==11 and all(r['pass'] for r in report),'sources':report}
(ROOT/'artifacts'/'qa-blend.json').write_text(json.dumps(result,indent=2),encoding='utf8')
print('JOSCRAFT_BLEND_QA',result['all_pass'],len(report),flush=True)
