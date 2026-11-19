"""Eight real Blender action poses, for visual review; not runtime QA."""
import bpy, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.render.resolution_x=1700; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Animation review studio'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.4,.47,.55,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
def aim(o,p): o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
clips=[('Idle',12),('Walk',8),('Run',6),('Jump',16),('Attack',14),('Hit',10),('Sit',40),('Ride',8)]
for i,(clip,frame) in enumerate(clips):
    with bpy.data.libraries.load(str(ROOT/'source/tiger.blend'),link=False) as (a,b): b.objects=a.objects; b.actions=[clip]
    for o in b.objects:
        if o: bpy.context.collection.objects.link(o)
    rig=next(o for o in b.objects if o.type=='ARMATURE')
    for tr in rig.animation_data.nla_tracks: tr.mute=True
    rig.animation_data.action=b.actions[0]
    # All objects share one frame: freeze the selected authored frame by
    # translating key times, so scene frame0 evaluates our desired clip pose.
    for slot in b.actions[0].slots:
        for layer in b.actions[0].layers:
            for strip in layer.strips:
                bag=strip.channelbag(slot)
                if bag:
                    for fc in bag.fcurves:
                        for kp in fc.keyframe_points: kp.co.x-=frame; kp.handle_left.x-=frame; kp.handle_right.x-=frame
    rig.location=((i%4-1.5)*2.9,(.5-i//4)*4.0,0)
    bpy.ops.object.text_add(location=(rig.location.x-.48,rig.location.y+1.05,.015)); tx=bpy.context.object; tx.data.body=clip; tx.data.size=.27; tx.rotation_euler[2]=math.pi
    tx.location.x+=.9
    mat=bpy.data.materials.get('Dark text') or bpy.data.materials.new('Dark text'); mat.diffuse_color=(.015,.025,.04,1); tx.data.materials.append(mat)
bpy.ops.mesh.primitive_plane_add(size=100,location=(0,0,-.012)); fl=bpy.context.object
mat=bpy.data.materials.new('Matte floor'); mat.diffuse_color=(.75,.77,.74,1); fl.data.materials.append(mat)
for loc,energy,size in [((-5,6,12),1800,8),((6,-4,7),1200,7)]:
    bpy.ops.object.light_add(type='AREA',location=loc); li=bpy.context.object; li.data.energy=energy; li.data.size=size; aim(li,(0,0,1))
bpy.ops.object.camera_add(location=(4,16,10)); camera=bpy.context.object; camera.data.type='ORTHO'; camera.data.ortho_scale=13.6; aim(camera,(0,0,1)); scene.camera=camera
scene.frame_set(0); scene.render.filepath=str(ROOT/'artifacts/blender-animation-poses.png'); bpy.ops.render.render(write_still=True)
