"""JOSCRAFT original block pets. Run with Blender 5.2 --background --python this.py.

No purchased geometry, textures or rigs. Authoring references are local school SVG
portraits + roster colours. Every mesh is one vertex-coloured skinned surface.
Blender +Z up, +Y face; glTF/Godot +Y up, -Z face. Units metres. Origin floor.
"""
import bpy, bmesh, math, json, hashlib, struct, sys
from mathutils import Vector, Matrix, Quaternion
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REF = ROOT.parent / 'DetiSkola'
# The archived roster is public character metadata only (no school profiles).
# Keep builds self-contained when JOSCRAFT is copied away from the old project.
ROSTER = json.loads((ROOT/'source/character-roster.json').read_text(encoding='utf-8-sig'))
FPS = 24
CLIPS = {'Idle':48,'Walk':32,'Run':24,'Jump':32,'Attack':28,'Hit':20,'Sit':40,'Ride':32}
WHITE='#FFFFFF'; DARK='#24314A'; PINK='#F096AE'; GOLD='#FFD47D'

def rgba(h):
    def lin(v): return v/12.92 if v<=.04045 else ((v+.055)/1.055)**2.4
    return tuple(lin(int(h[i:i+2],16)/255) for i in (1,3,5))+(1,)

# One reusable chamfered cube topology, deliberately planar (no smoothing).
bm=bmesh.new(); bmesh.ops.create_cube(bm,size=1)
bmesh.ops.bevel(bm,geom=list(bm.edges),offset=.065,segments=1,affect='EDGES')
bm.verts.ensure_lookup_table(); bm.verts.index_update()
BOXV=[v.co.copy() for v in bm.verts]; BOXF=[[v.index for v in f.verts] for f in bm.faces]; bm.free()

class Builder:
    def __init__(self): self.v=[]; self.f=[]; self.colors=[]; self.groups=[]
    def add(self,vs,fs,c,bone):
        start=len(self.v); self.v.extend(vs); self.groups.extend([bone]*len(vs))
        self.f.extend([[start+i for i in f] for f in fs]); self.colors.extend([rgba(c)]*len(fs))
    def box(self,c,size,color,bone='body',rot=None):
        m=Matrix.Identity(3) if rot is None else rot.to_matrix()
        vs=[Vector(c)+m@Vector((v.x*size[0],v.y*size[1],v.z*size[2])) for v in BOXV]
        self.add(vs,BOXF,color,bone)
    def bar(self,a,b,width,color,bone='body',depth=None):
        a,b=Vector(a),Vector(b); d=b-a
        self.box((a+b)/2,(width,depth or width,d.length),color,bone,Vector((0,0,1)).rotation_difference(d.normalized()))
    def prism(self,xz,y,depth,color,bone='head'):
        n=len(xz); vs=[(x,y+side*depth/2,z) for side in (-1,1) for x,z in xz]
        fs=[list(range(n-1,-1,-1)),list(range(n,n*2))]
        fs += [[i,(i+1)%n,(i+1)%n+n,i+n] for i in range(n)]
        self.add(vs,fs,color,bone)
    def finish(self,name,rig):
        mesh=bpy.data.meshes.new(name+'_mesh'); mesh.from_pydata(self.v,[],self.f); mesh.update()
        obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
        col=mesh.color_attributes.new(name='PetPalette',type='FLOAT_COLOR',domain='CORNER')
        for poly,c in zip(mesh.polygons,self.colors):
            for i in poly.loop_indices: col.data[i].color=c
        mat=bpy.data.materials.new(name+'_Palette'); mat.use_nodes=True
        nodes=mat.node_tree.nodes; bs=nodes.get('Principled BSDF'); bs.inputs['Roughness'].default_value=.83
        attr=nodes.new('ShaderNodeVertexColor'); attr.layer_name='PetPalette'; mat.node_tree.links.new(attr.outputs['Color'],bs.inputs['Base Color'])
        obj.data.materials.append(mat)
        for bone in set(self.groups):
            vg=obj.vertex_groups.new(name=bone); vg.add([i for i,g in enumerate(self.groups) if g==bone],1,'REPLACE')
        obj.modifiers.new('OriginalPetRig','ARMATURE').object=rig; obj.parent=rig
        return obj

def make_rig(id):
    data=bpy.data.armatures.new(id+'_original_rig'); arm=bpy.data.objects.new(id+'_Rig',data); bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active=arm; arm.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
    def bone(name,p,parent='root'):
        b=data.edit_bones.new(name); b.head=p; b.tail=Vector(p)+Vector((0,0,.18))
        if parent: b.parent=data.edit_bones[parent]
    bone('root',(0,0,0),None); bone('body',(0,0,.65)); bone('head',(0,0,1.35),'body')
    if id=='octo':
        for i in range(8):
            a=2*math.pi*i/8; bone('tentacle_'+str(i),(.3*math.cos(a),.3*math.sin(a),.55),'body')
            bone('tip_'+str(i),(.63*math.cos(a),.63*math.sin(a),.17),'tentacle_'+str(i))
    else:
        for s,label in [(-1,'L'),(1,'R')]:
            bone('leg_'+label,(s*.23,0,.6))
            bone(('wing_' if id=='owl' else 'arm_')+label,(s*.48,0,1.18),'body')
            bone('ear_'+label,(s*.33,0,1.96),'head')
        bone('tail',(0,-.21,.8),'body')
        if id=='dragon':
            bone('wing_L',(-.35,-.12,1.2),'body'); bone('wing_R',(.35,-.12,1.2),'body')
    bpy.ops.object.mode_set(mode='OBJECT'); arm.show_in_front=True
    for p in arm.pose.bones: p.rotation_mode='QUATERNION'
    return arm

def face(B,id,fur,belly,outline,z=1.64,width=.96,depth=.72):
    y=depth/2
    # Portrait-like large square eyes with two glints, cheeks, and friendly muzzle.
    if id=='owl':
        for s in (-1,1): B.box((s*.235,y+.018,z+.025),(.43,.055,.49),belly,'head')
    if id=='robot': B.box((0,y+.018,z+.015),(.82,.06,.44),outline,'head')
    for s in (-1,1):
        x=s*.222
        if id=='panda': B.box((x,y+.022,z+.015),(.34,.06,.40),outline,'head',Quaternion((0,1,0),s*.17))
        B.box((x,y+.065,z+.035),(.235,.065,.28),DARK,'head')
        B.box((x-.046,y+.104,z+.107),(.075,.016,.086),WHITE,'head')
        B.box((x+.054,y+.104,z-.028),(.032,.016,.034),WHITE,'head')
        B.box((s*.355,y+.045,z-.165),(.13,.018,.065),PINK,'head')
    if id=='owl':
        B.prism([(-.105,z-.16),(.105,z-.16),(0,z-.34)],y+.14,.20,GOLD)
    elif id=='robot':
        B.box((0,y+.066,z-.208),(.27,.022,.055),WHITE,'head')
        for x in (-.08,0,.08): B.box((x,y+.083,z-.208),(.015,.014,.055),outline,'head')
    else:
        muzzle=.45 if id not in ('fox','unicorn') else .52
        B.box((0,y+.07,z-.225),(muzzle,.24,.23),belly,'head')
        B.box((0,y+.203,z-.17),(.13,.065,.075),outline if id=='dragon' else DARK,'head')
        # Three-piece smiling mouth, resting over the cream muzzle surface.
        B.box((0,y+.196,z-.256),(.035,.018,.10),DARK,'head')
        B.box((-.067,y+.196,z-.292),(.12,.018,.025),DARK,'head',Quaternion((0,1,0),-.14))
        B.box((.067,y+.196,z-.292),(.12,.018,.025),DARK,'head',Quaternion((0,1,0),.14))
        if id=='bunny': B.box((0,y+.21,z-.327),(.11,.025,.095),WHITE,'head')

def anatomy(B,r):
    id=r['id']; fur=r['fur']; cream=r['belly']; ink=r['outline']
    if id=='octo':
        B.box((0,0,1.22),(1.12,.86,1.08),fur,'head'); B.box((0,.04,.72),(.76,.62,.32),fur,'body')
        face(B,id,fur,cream,ink,z=1.27,width=1.12,depth=.86)
        for x,z,s in [(-.3,1.69,.11),(-.05,1.75,.075),(.22,1.71,.065)]: B.box((x,.437,z),(s,.025,s),cream,'head')
        for i in range(8):
            a=2*math.pi*i/8; d=Vector((math.cos(a),math.sin(a),0)); p0=d*.3+Vector((0,0,.56)); p1=d*.67+Vector((0,0,.15)); p2=d*.93+Vector((0,0,.11))
            B.bar(p0,p1,.24,fur,'tentacle_'+str(i)); B.bar(p1,p2,.21,fur,'tip_'+str(i))
            # Flat wide distal paddles supply a real floor contact patch.
            B.box(p2,(.25,.25,.22),fur,'tip_'+str(i))
            for t in (.64,.82):
                p=d*t+Vector((0,0,.055)); B.box(p,(.10,.10,.045),cream,'tip_'+str(i))
        return
    # Teddy base is intentionally shared among mammals, with species-specific
    # silhouette, muzzle, appendages and markings; owl has avian wings/feet.
    w=.88 if id in ('bear','panda') else .72
    if id=='owl': w=.88
    B.box((0,0,.93),(w,.58,.84),fur,'body')
    B.box((0,.302,.92),(w*.68,.045,.57),cream,'body')
    headsize=(1.0,.73,.80)
    if id=='fox': headsize=(1.04,.72,.72)
    if id=='robot': headsize=(.99,.72,.73)
    if id=='owl': headsize=(1.08,.78,.85)
    B.box((0,0,1.65),headsize,fur,'head')
    face(B,id,fur,cream,ink,depth=headsize[1])
    for s,label in [(-1,'L'),(1,'R')]:
        limb=ink if id=='panda' else fur
        # Feet have flat soles in z0 and broad planted contact.
        if id=='owl':
            B.box((s*.23,0,.25),(.15,.17,.37),ink,'leg_'+label)
            for j in (-1,0,1): B.box((s*.23+j*.068,.145,.075),(.063,.42,.15),GOLD,'leg_'+label)
            B.box((s*.57,-.015,.94),(.29,.41,.66),ink,'wing_'+label,Quaternion((0,1,0),s*.16))
            for k in range(3): B.box((s*(.52+k*.07),.212,.86-k*.035),(.075,.035,.35),fur,'wing_'+label)
            B.prism([(s*.22,1.99),(s*.52,1.99),(s*.46,2.25)],0,.44,ink,'ear_'+label)
        else:
            B.box((s*.23,-.015,.36),(.285,.32,.53),limb,'leg_'+label)
            B.box((s*.23,.12,.12),(.38,.54,.24),limb,'leg_'+label)
            B.box((s*.23,.367,.14),(.24,.035,.115),cream,'leg_'+label)
            B.box((s*(w/2+.13),0,.97),(.29,.34,.58),limb,'arm_'+label,Quaternion((0,1,0),s*-.10))
            B.box((s*(w/2+.14),.15,.76),(.22,.08,.19),cream,'arm_'+label)
        if id in ('bear','panda','tiger'):
            B.box((s*.39,0,2.075),(.31,.31,.32),ink if id=='panda' else fur,'ear_'+label)
            B.box((s*.39,.165,2.075),(.18,.028,.19),cream,'ear_'+label)
        elif id in ('fox','cat','unicorn'):
            top=2.25 if id in ('fox','cat') else 2.17
            B.prism([(s*.20,1.99),(s*.51,1.99),(s*.46,top)],0,.35,fur,'ear_'+label)
            B.prism([(s*.29,2.015),(s*.45,2.015),(s*.43,top-.09)],.188,.022,PINK if id=='cat' else cream,'ear_'+label)
        elif id=='bunny':
            B.box((s*.29,0,2.22),(.24,.25,.72),fur,'ear_'+label,Quaternion((0,1,0),s*.13))
            B.box((s*.29,.142,2.23),(.12,.023,.52),PINK,'ear_'+label,Quaternion((0,1,0),s*.13))
        elif id=='robot':
            B.box((s*.55,0,1.65),(.16,.33,.32),ink,'head'); B.box((s*.639,.05,1.72),(.025,.12,.12),'#A6F6DE','head')
        elif id=='dragon':
            B.prism([(s*.26,2.00),(s*.47,2.00),(s*.43,2.27)],-.06,.22,GOLD,'head')
    if id=='tiger':
        for s in (-1,1):
            for j in range(3):
                B.prism([(s*.5,1.93-j*.19),(s*.5,1.84-j*.19),(s*.35,1.86-j*.19)],.378,.017,'#3A2724','head')
            for j in range(2): B.box((s*(w/2+.273),.03,1.06-j*.18),(.015,.28,.065),'#3A2724','arm_'+('L' if s<0 else 'R'))
        B.prism([(-.075,2.055),(.075,2.055),(0,1.84)],.379,.018,'#3A2724','head')
        B.bar((0,-.22,.73),(.3,-.8,.63),.17,fur,'tail'); B.bar((.3,-.8,.63),(.48,-.86,.94),.16,fur,'tail')
        for j in range(3): B.box((.12+j*.11,-.44-j*.13,.70-j*.015),(.20,.065,.19),'#3A2724','tail')
    elif id=='fox':
        B.bar((0,-.24,.7),(.36,-.73,.91),.36,fur,'tail'); B.bar((.36,-.73,.91),(.45,-.88,1.42),.32,fur,'tail'); B.box((.45,-.88,1.48),(.34,.34,.35),cream,'tail',Quaternion((1,0,0),-.15))
        # Dark socks and diamond forehead blaze distinguish fox from cat.
        for s,label in [(-1,'L'),(1,'R')]: B.box((s*.23,.13,.13),(.39,.56,.25),ink,'leg_'+label)
        B.prism([(-.11,1.99),(.11,1.99),(0,1.80)],.377,.02,cream)
    elif id=='dragon':
        for s,label in [(-1,'L'),(1,'R')]:
            B.prism([(s*.35,1.20),(s*.94,1.66),(s*1.10,1.07),(s*.82,1.17),(s*.68,.93)],-.18,.10,cream,'wing_'+label)
            B.bar((s*.36,-.11,1.20),(s*.94,-.18,1.66),.085,ink,'wing_'+label)
        B.bar((0,-.25,.75),(.0,-.85,.49),.22,fur,'tail'); B.bar((0,-.85,.49),(.16,-1.05,.76),.16,fur,'tail')
        B.prism([(.02,.77),(.30,.77),(.16,1.00)],-1.06,.17,GOLD,'tail')
        for z in (.78,.94,1.1): B.box((0,.339,z),(.40,.021,.035),ink,'body')
        for i in range(3): B.prism([(-.07+i*.07,2.05),(.01+i*.07,2.05),(-.03+i*.07,2.16)],-.15,.13,GOLD)
    elif id=='unicorn':
        B.prism([(-.105,2.05),(.105,2.05),(0,2.46)],.13,.20,GOLD)
        for i,c in enumerate(['#B8A5EE','#82D7E8','#F3B7DB']):
            B.box((-.49-i*.025,-.1,1.83-i*.14),(.19,.49,.29),c,'head')
            B.bar((0,-.25,.8),(.15-i*.12,-.72,.56-i*.04),.15,c,'tail')
        for s,label in [(-1,'L'),(1,'R')]: B.box((s*.23,.12,.10),(.39,.56,.20),'#B8A5EE','leg_'+label)
    elif id=='robot':
        B.box((0,0,2.11),(.09,.09,.30),ink,'head'); B.box((0,0,2.29),(.22,.22,.19),PINK,'head')
        B.box((0,.341,.97),(.44,.03,.25),ink,'body')
        for i,c in enumerate([GOLD,'#A6F6DE',PINK]): B.box((-.13+i*.13,.365,.97),(.075,.021,.075),c,'body')
        for s in (-1,1):
            for z in (1.86,1.43): B.box((s*.40,.375,z),(.045,.02,.045),ink,'head')
    elif id=='cat':
        B.bar((0,-.22,.72),(.45,-.67,.83),.14,fur,'tail'); B.bar((.45,-.67,.83),(.54,-.68,1.30),.14,fur,'tail')
        for s in (-1,1):
            for j in (-1,1): B.bar((s*.27,.522,1.42+j*.035),(s*.50,.49,1.42+j*.07),.019,ink,'head')
    elif id=='bunny':
        B.box((0,-.39,.73),(.35,.34,.35),cream,'tail')
    elif id in ('bear','panda'):
        B.box((0,-.35,.73),(.24,.24,.25),fur,'tail')
        if id=='bear':
            for i in range(3): B.box((-.12+i*.11,0,2.075+i*.013),(.16,.28,.15),'#E7B98C','head',Quaternion((0,1,0),-.2))

def reset_pose(arm):
    for p in arm.pose.bones: p.location=(0,0,0); p.rotation_quaternion=(1,0,0,0); p.scale=(1,1,1)

def rotate(arm,name,x=0,y=0,z=0):
    if name not in arm.pose.bones: return
    p=arm.pose.bones[name]; rest=p.bone.matrix_local.to_quaternion()
    q=Quaternion((1,0,0),x)@Quaternion((0,1,0),y)@Quaternion((0,0,1),z)
    p.rotation_quaternion=rest.inverted()@q@rest

def translate(arm,name,v):
    if name not in arm.pose.bones: return
    p=arm.pose.bones[name]; p.location=p.bone.matrix_local.to_3x3().inverted()@Vector(v)

def min_z(obj):
    bpy.context.view_layer.update(); dg=bpy.context.evaluated_depsgraph_get(); ev=obj.evaluated_get(dg); mesh=ev.to_mesh()
    z=min((ev.matrix_world@v.co).z for v in mesh.vertices); ev.to_mesh_clear(); return z

def pose(arm,id,clip,t):
    phase=2*math.pi*t; wave=math.sin(phase); ease=math.sin(math.pi*min(t*2,1)/2)**2
    # root stays fixed laterally: locomotion is in place, driven by C# body.
    if clip=='Idle':
        rotate(arm,'head',z=.07*wave); rotate(arm,'body',x=.012*wave)
        for s,l in [(-1,'L'),(1,'R')]: rotate(arm,'arm_'+l,y=s*.025*wave); rotate(arm,'ear_'+l,x=.035*wave)
    elif clip in ('Walk','Run'):
        amp=.36 if clip=='Walk' else .68
        for s,l in [(-1,'L'),(1,'R')]:
            rotate(arm,'leg_'+l,x=s*amp*wave)
            rotate(arm,'arm_'+l,x=-s*amp*wave,y=s*.06)
            if id=='owl': rotate(arm,'wing_'+l,y=s*(.10+(.20 if clip=='Run' else .06)*wave))
        rotate(arm,'head',z=.045*wave); rotate(arm,'body',x=-.03 if clip=='Walk' else -.10)
    elif clip=='Jump':
        lift=max(0,math.sin(math.pi*t))*.72
        translate(arm,'root',(0,0,lift)); rotate(arm,'body',x=-.10*math.sin(math.pi*t))
        for s,l in [(-1,'L'),(1,'R')]:
            rotate(arm,'leg_'+l,x=.40*math.sin(math.pi*t)); rotate(arm,'arm_'+l,y=s*.65*math.sin(math.pi*t)); rotate(arm,'wing_'+l,y=s*.85*math.sin(math.pi*t))
    elif clip=='Attack':
        punch=math.sin(math.pi*t)**3
        rotate(arm,'body',z=-.22*punch); rotate(arm,'head',x=-.12*punch)
        rotate(arm,'arm_R',x=-1.25*punch,y=-.12); rotate(arm,'arm_L',x=-.45*punch)
        rotate(arm,'wing_R',y=-.8*punch); rotate(arm,'wing_L',y=.4*punch)
    elif clip=='Hit':
        recoil=math.sin(math.pi*t)*(1-.3*t)
        rotate(arm,'body',x=.24*recoil); rotate(arm,'head',x=.25*recoil,z=.12*math.sin(phase*2)*recoil)
        for s,l in [(-1,'L'),(1,'R')]: rotate(arm,'arm_'+l,y=s*.32*recoil); rotate(arm,'wing_'+l,y=s*.45*recoil)
    elif clip=='Sit':
        # Feet extend forward; haunches genuinely settle to the floor.
        translate(arm,'body',(0,-.08*ease,-.45*ease)); rotate(arm,'head',x=-.04*ease)
        for l in ('L','R'):
            rotate(arm,'leg_'+l,x=1.4*ease); translate(arm,'leg_'+l,(0,.04*ease,-.10*ease))
            rotate(arm,'arm_'+l,x=-.22*ease)
        if id=='owl':
            for s,l in [(-1,'L'),(1,'R')]: rotate(arm,'wing_'+l,y=s*-.2*ease)
    elif clip=='Ride':
        # Isolated pose clip: no mount or riding gameplay exists.
        for s,l in [(-1,'L'),(1,'R')]:
            rotate(arm,'leg_'+l,x=1.10,y=s*.25); rotate(arm,'arm_'+l,x=-.9,y=s*-.15)
            rotate(arm,'wing_'+l,y=s*.3)
        translate(arm,'body',(0,0,-.12+.045*wave)); rotate(arm,'head',x=.05*wave)
    if 'tail' in arm.pose.bones: rotate(arm,'tail',z=.12*wave*(2 if clip=='Run' else 1))
    if id=='dragon' and clip not in ('Jump',):
        for s,l in [(-1,'L'),(1,'R')]: rotate(arm,'wing_'+l,y=s*.10*wave)
    if id=='octo':
        for i in range(8):
            a=2*math.pi*i/8; v=math.sin(phase+i*math.pi/2)
            if clip=='Idle': amount=.035*v
            elif clip=='Walk': amount=.16*v
            elif clip=='Run': amount=.27*v
            elif clip=='Jump': amount=.35*math.sin(math.pi*t)
            elif clip=='Attack': amount=.75*math.sin(math.pi*t) if i in (1,2,3) else .04*v
            elif clip=='Hit': amount=-.25*math.sin(math.pi*t)
            elif clip=='Sit': amount=-.12*ease
            else: amount=.20+.09*v
            rotate(arm,'tentacle_'+str(i),x=math.sin(a)*amount,y=-math.cos(a)*amount)
            rotate(arm,'tip_'+str(i),x=-math.sin(a)*amount*.55,y=math.cos(a)*amount*.55)

def animate(arm,obj,id):
    arm.animation_data_create(); report={}
    for clip,end in CLIPS.items():
        action=bpy.data.actions.new(clip); arm.animation_data.action=action
        # NLA is disabled while baking each independent action.
        for track in arm.animation_data.nla_tracks: track.mute=True
        roots=[]; lows=[]
        for f in range(end+1):
            bpy.context.scene.frame_set(f); reset_pose(arm); pose(arm,id,clip,f/end)
            if clip!='Jump' and clip!='Ride':
                low=min_z(obj); p=arm.pose.bones['root']; p.location += p.bone.matrix_local.to_3x3().inverted()@Vector((0,0,-low))
            elif clip=='Jump':
                # Preserve intentional jump arc, remove pose-dependent sinking.
                lift=.72*max(0,math.sin(math.pi*f/end)); low=min_z(obj)
                p=arm.pose.bones['root']; p.location += p.bone.matrix_local.to_3x3().inverted()@Vector((0,0,lift-low))
            roots.append(float(arm.pose.bones['root'].location.length)); lows.append(float(min_z(obj)))
            for p in arm.pose.bones:
                p.keyframe_insert(data_path='location',frame=f,group=p.name)
                p.keyframe_insert(data_path='rotation_quaternion',frame=f,group=p.name)
        # Keep interpolation linear to bound planted contacts between samples.
        for slot in action.slots:
            for layer in action.layers:
                for strip in layer.strips:
                    bag=strip.channelbag(slot)
                    if bag:
                        for fc in bag.fcurves:
                            for k in fc.keyframe_points: k.interpolation='LINEAR'
        arm.animation_data.action=None
        track=arm.animation_data.nla_tracks.new(); track.name=clip; track.strips.new(clip,0,action)
        track.mute=True
        report[clip]={'frames':end+1,'seconds':end/FPS,'floor_min':min(lows),'floor_max':max(lows),'root_translation_max':max(roots),'intentional_airborne':clip in ('Jump','Ride'),'notes':'Ride is a pose demo without a mount; Jump includes vertical preview root motion.' if clip in ('Jump','Ride') else 'Baked per-frame minimum skinned mesh contact = floor 0.'}
    for track in arm.animation_data.nla_tracks: track.mute=False
    reset_pose(arm); bpy.context.scene.frame_set(0)
    return report

def glb_info(path):
    raw=path.read_bytes(); n,typ=struct.unpack_from('<II',raw,12); gl=json.loads(raw[20:20+n])
    return {'animations':[a['name'] for a in gl.get('animations',[])],'meshes':len(gl.get('meshes',[])),'materials':len(gl.get('materials',[])),'skins':len(gl.get('skins',[])),'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest()}

def build(r):
    bpy.ops.wm.read_factory_settings(use_empty=True); scene=bpy.context.scene; scene.render.fps=FPS; scene.frame_start=0; scene.frame_end=48
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    arm=make_rig(r['id']); B=Builder(); anatomy(B,r); obj=B.finish(r['id']+'_BlockPet',arm)
    clips=animate(arm,obj,r['id'])
    # Default source opens with Idle active, not superposed NLA tracks.
    for tr in arm.animation_data.nla_tracks: tr.mute=True
    idle=bpy.data.actions.get('Idle'); arm.animation_data.action=idle; scene.frame_set(0); bpy.context.view_layer.update()
    coords=[v.co for v in obj.data.vertices]
    bounds={'min':[min(v[i] for v in coords) for i in range(3)],'max':[max(v[i] for v in coords) for i in range(3)]}
    source=ROOT/'source'/(r['id']+'.blend'); bpy.ops.wm.save_as_mainfile(filepath=str(source))
    arm.animation_data.action=None
    for tr in arm.animation_data.nla_tracks: tr.mute=False
    export=ROOT/'assets/characters'/(r['id']+'.glb')
    bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); obj.select_set(True); bpy.context.view_layer.objects.active=arm
    bpy.ops.export_scene.gltf(filepath=str(export),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='NLA_TRACKS',export_nla_strips=True,export_force_sampling=True,export_frame_range=False,export_materials='EXPORT',export_skins=True,export_yup=True,export_apply=False,export_cameras=False,export_lights=False,export_def_bones=True,export_extras=True)
    info=glb_info(export); mesh=obj.data; mesh.calc_loop_triangles()
    return {'id':r['id'],'name':r['name'],'source':str(source.relative_to(ROOT)).replace('\\','/'),'glb':str(export.relative_to(ROOT)).replace('\\','/'),'rig_family':'cephalopod-eight-two-segment-arms' if r['id']=='octo' else 'avian-wing-feet' if r['id']=='owl' else 'block-biped-with-species-appendages','bones':[b.name for b in arm.data.bones],'vertices':len(mesh.vertices),'triangles':len(mesh.loop_triangles),'blender_rest_bounds':bounds,'clips':clips,'export':info,'reference':'../DetiSkola/deployment/oliver/pets/'+r['id']+'.svg','palette':{'fur':r['fur'],'belly':r['belly'],'outline':r['outline']}}

if __name__=='__main__':
    for d in ('source','assets/characters','artifacts'): (ROOT/d).mkdir(parents=True,exist_ok=True)
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    selected=[r for r in ROSTER if not args or r['id'] in args]
    entries=[]
    for r in selected:
        print('BUILD PET',r['id'],flush=True); entries.append(build(r))
    path=ROOT/'artifacts/model-manifest.json'
    if args and path.exists():
        old=json.loads(path.read_text(encoding='utf8')); by={e['id']:e for e in old['characters']}; by.update({e['id']:e for e in entries}); entries=[by[r['id']] for r in ROSTER if r['id'] in by]
    manifest={'version':1,'author':'Original JOSCRAFT procedural Blender authoring by Codex, commissioned in this chat; school portrait identity/palette retained. No third-party mesh, rig, texture or Minecraft asset.','blender':bpy.app.version_string,'units':'metres','runtime_up':'+Y','runtime_forward':'-Z','blender_up':'+Z','blender_forward':'+Y','pivot':'origin; resting lowest sole/appendage at floor y=0','rendering':'1 joined skinned mesh and 1 vertex-color material per character; planar chamfered geometry','animation_fps':FPS,'required_clips':list(CLIPS),'characters':entries}
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf8')
    print('JOSCRAFT MODEL BUILD COMPLETE',len(entries),flush=True)
