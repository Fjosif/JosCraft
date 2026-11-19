"""Blender-only authored mesh contact sheet (separate from Godot runtime QA)."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'artifacts/model-manifest.json').read_text(encoding='utf8'))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.render.resolution_x=1700; scene.render.resolution_y=1150; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Soft blue studio'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.34,.43,.52,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
scene.view_settings.view_transform='AgX'
def aim(o,p): o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
def load(char,pos):
    with bpy.data.libraries.load(str(ROOT/char['source']),link=False) as (a,b): b.objects=a.objects
    for o in b.objects:
        if o: bpy.context.collection.objects.link(o)
    rig=next(o for o in b.objects if o.type=='ARMATURE'); rig.location=pos
    return rig
for i,c in enumerate(manifest['characters']):
    col=i%4; row=i//4; x=(col-1.5)*2.9; y=(1-row)*3.2
    rig=load(c,(x,y,0))
    # Labels lie on floor in front of each pet, facing the elevated camera.
    bpy.ops.object.text_add(location=(x-.65,y+.87,.015)); text=bpy.context.object; text.data.body=c['name']; text.data.size=.24; text.data.extrude=.001; text.rotation_euler=(0,0,0)
    # Blender text lies XY by default; use material with strong dark contrast.
    mat=bpy.data.materials.get('Labels') or bpy.data.materials.new('Labels'); mat.diffuse_color=(.025,.045,.07,1); text.data.materials.append(mat)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015)); floor=bpy.context.object
mat=bpy.data.materials.new('Warm cream floor'); mat.diffuse_color=(.72,.76,.75,1); floor.data.materials.append(mat)
bpy.ops.object.light_add(type='AREA',location=(-5,7,12)); light=bpy.context.object; light.data.energy=1700; light.data.shape='DISK'; light.data.size=8; aim(light,(0,0,0))
bpy.ops.object.light_add(type='AREA',location=(6,-4,9)); light=bpy.context.object; light.data.energy=1300; light.data.size=7; aim(light,(0,0,1))
bpy.ops.object.camera_add(location=(5,16,13)); camera=bpy.context.object; camera.data.type='ORTHO'; camera.data.ortho_scale=15.5; aim(camera,(0,0,1)); scene.camera=camera
scene.render.filepath=str(ROOT/'artifacts/blender-lineup.png'); scene.frame_set(0); bpy.ops.render.render(write_still=True)
print('BLENDER CONTACT SHEET READY',flush=True)
