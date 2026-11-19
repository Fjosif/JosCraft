"""
Original non-full block models generator for JOSCRAFT (Godot 4.7.2 Mono).
Generates:
  - source/blocks-v1.blend
  - exports/oak-slab.glb, oak-stairs.glb, oak-door.glb, oak-fence.glb, wooden-chest.glb
  - previews/ (front, side, top, and open/closed states)
  - block-models.json & collision-boxes.json

Author: Antigravity (original geometry, UVs, pivots, animations, and collision definitions)
Blender: 5.2+
"""

import hashlib
import json
import math
from pathlib import Path

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

BASE_DIR = Path(r"D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-blocks-v1")
SOURCE_DIR = BASE_DIR / "source"
TEX_DIR = SOURCE_DIR / "textures"
EXPORT_DIR = BASE_DIR / "exports"
PREVIEW_DIR = BASE_DIR / "previews"


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def load_packed_image(filename: str) -> bpy.types.Image:
    path = TEX_DIR / filename
    img = bpy.data.images.load(str(path), check_existing=True)
    img.pack()
    return img


def make_textured_material(name: str, img: bpy.types.Image, roughness: float = 0.85) -> bpy.types.Material:
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (320, 0)

    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (20, 0)
    bsdf.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.22

    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.location = (-280, 0)
    tex.image = img

    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def make_iron_material(name: str = "IronTrim") -> bpy.types.Material:
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (280, 0)

    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (0, 0)
    bsdf.inputs["Base Color"].default_value = (0.14, 0.155, 0.185, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.72
    bsdf.inputs["Roughness"].default_value = 0.44
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def apply_box_uvs(bm: bmesh.types.BMesh, vertical_grain: bool = False, uv_scale: float = 1.0) -> None:
    uv_layer = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        for loop in f.loops:
            co = loop.vert.co
            if az >= ax and az >= ay:
                u, v = co.x, co.y
            elif ax >= ay and ax >= az:
                u, v = co.y, co.z
            else:
                u, v = co.x, co.z
            if vertical_grain:
                # Rotate 90 deg if needed on horizontal faces, keep vertical axis on V
                loop[uv_layer].uv = (u * uv_scale + 0.5, v * uv_scale + 0.5)
            else:
                loop[uv_layer].uv = (u * uv_scale + 0.5, v * uv_scale + 0.5)


class MeshBuilder:
    def __init__(self, name: str, collection: bpy.types.Collection):
        self.name = name
        self.collection = collection
        self.bm = bmesh.new()
        self.materials: list[bpy.types.Material] = []

    def mat_index(self, mat: bpy.types.Material) -> int:
        if mat not in self.materials:
            self.materials.append(mat)
        return self.materials.index(mat)

    def add_box(
        self,
        size: tuple[float, float, float],
        center: tuple[float, float, float],
        mat: bpy.types.Material,
        bevel: float = 0.010,
        vertical_grain: bool = False,
        uv_scale: float = 1.0,
        rot_deg: tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> None:
        sub = bmesh.new()
        sx, sy, sz = size
        bmesh.ops.create_cube(sub, size=1.0)
        for v in sub.verts:
            v.co.x *= sx
            v.co.y *= sy
            v.co.z *= sz

        bev = min(bevel, min(sx, sy, sz) * 0.25)
        if bev > 0.0004:
            bmesh.ops.bevel(
                sub,
                geom=list(sub.edges),
                offset=bev,
                segments=1,
                profile=0.5,
                affect="EDGES",
                clamp_overlap=True,
            )

        rot = Euler(tuple(math.radians(a) for a in rot_deg), "XYZ").to_matrix().to_4x4()
        trans = Matrix.Translation(Vector(center))
        bmesh.ops.transform(sub, matrix=trans @ rot, verts=list(sub.verts))
        bmesh.ops.recalc_face_normals(sub, faces=list(sub.faces))
        apply_box_uvs(sub, vertical_grain=vertical_grain, uv_scale=uv_scale)

        uv_src = sub.loops.layers.uv.verify()
        uv_dst = self.bm.loops.layers.uv.verify()
        mi = self.mat_index(mat)

        v_map = {sv: self.bm.verts.new(sv.co) for sv in sub.verts}
        for sf in sub.faces:
            nf = self.bm.faces.new([v_map[v] for v in sf.verts])
            nf.smooth = False
            nf.material_index = mi
            for sl, dl in zip(sf.loops, nf.loops):
                dl[uv_dst].uv = sl[uv_src].uv

        sub.free()

    def add_stairs_manifold(self, mat: bpy.types.Material, bevel: float = 0.012) -> None:
        """
        Single manifold L-shaped two-step stairs in 1x1x1m cell (no internal dividing faces).
        In Blender: +Y is Godot -Z (front, lower step Z: 0..0.5m), -Y is Godot +Z (back, upper step Z: 0..1.0m).
        """
        # Create two overlapping halves? No: extrude/build exact 8-face L-prism cleanly!
        # Cross-section in (Y, Z):
        #   (-0.5, 0.0) -> (0.5, 0.0) -> (0.5, 0.5) -> (0.0, 0.5) -> (0.0, 1.0) -> (-0.5, 1.0)
        profile = [
            (-0.5, 0.0),
            (0.5, 0.0),
            (0.5, 0.5),
            (0.0, 0.5),
            (0.0, 1.0),
            (-0.5, 1.0),
        ]
        sub = bmesh.new()
        left_verts = [sub.verts.new((-0.5, y, z)) for (y, z) in profile]
        right_verts = [sub.verts.new((0.5, y, z)) for (y, z) in profile]

        # Left cap (-X), Right cap (+X)
        sub.faces.new(list(reversed(left_verts)))
        sub.faces.new(right_verts)
        n = len(profile)
        for i in range(n):
            j = (i + 1) % n
            sub.faces.new([left_verts[i], right_verts[i], right_verts[j], left_verts[j]])

        bmesh.ops.recalc_face_normals(sub, faces=list(sub.faces))
        bmesh.ops.bevel(
            sub,
            geom=list(sub.edges),
            offset=bevel,
            segments=1,
            profile=0.5,
            affect="EDGES",
            clamp_overlap=True,
        )
        bmesh.ops.recalc_face_normals(sub, faces=list(sub.faces))
        apply_box_uvs(sub, vertical_grain=False, uv_scale=1.0)

        uv_src = sub.loops.layers.uv.verify()
        uv_dst = self.bm.loops.layers.uv.verify()
        mi = self.mat_index(mat)

        v_map = {sv: self.bm.verts.new(sv.co) for sv in sub.verts}
        for sf in sub.faces:
            nf = self.bm.faces.new([v_map[v] for v in sf.verts])
            nf.smooth = False
            nf.material_index = mi
            for sl, dl in zip(sf.loops, nf.loops):
                dl[uv_dst].uv = sl[uv_src].uv

        sub.free()

    def build_object(self, parent: bpy.types.Object | None = None, location: tuple[float, float, float] = (0, 0, 0)) -> bpy.types.Object:
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        mesh = bpy.data.meshes.new(f"{self.name}_Mesh")
        self.bm.to_mesh(mesh)
        self.bm.free()

        for m in self.materials:
            mesh.materials.append(m)

        obj = bpy.data.objects.new(self.name, mesh)
        self.collection.objects.link(obj)
        obj.location = Vector(location)
        if parent:
            obj.parent = parent
        return obj


def add_object_nla_rot(
    obj: bpy.types.Object,
    clip_name: str,
    frame_end: int,
    rot_start_deg: tuple[float, float, float],
    rot_end_deg: tuple[float, float, float],
) -> None:
    if not obj.animation_data:
        obj.animation_data_create()
    action = bpy.data.actions.new(name=clip_name)
    action.use_frame_range = True
    action.frame_start = 0
    action.frame_end = frame_end
    obj.animation_data.action = action
    obj.rotation_mode = "XYZ"

    obj.rotation_euler = tuple(math.radians(a) for a in rot_start_deg)
    obj.keyframe_insert(data_path="rotation_euler", frame=0)
    obj.rotation_euler = tuple(math.radians(a) for a in rot_end_deg)
    obj.keyframe_insert(data_path="rotation_euler", frame=frame_end)

    track = obj.animation_data.nla_tracks.new()
    track.name = clip_name
    strip = track.strips.new(clip_name, start=0, action=action)
    strip.name = clip_name
    obj.animation_data.action = None
    obj.rotation_euler = (0.0, 0.0, 0.0)


def make_collection(name: str) -> bpy.types.Collection:
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    return col


def build_all_blocks():
    planks_img = load_packed_image("oak-planks-v1.png")
    bark_img = load_packed_image("oak-bark-v1.png")

    mat_planks = make_textured_material("OakPlanks", planks_img, roughness=0.86)
    mat_bark = make_textured_material("OakBark", bark_img, roughness=0.90)
    mat_iron = make_iron_material("IronTrim")

    collections = {}

    # 1. oak-slab (1 x 0.5 x 1 m, bottom half Y=0..0.5)
    c_slab = make_collection("oak-slab")
    collections["oak-slab"] = c_slab
    root_slab = bpy.data.objects.new("oak_slab", None)
    c_slab.objects.link(root_slab)
    mb = MeshBuilder("OakSlabMesh", c_slab)
    mb.add_box((1.0, 1.0, 0.5), (0.0, 0.0, 0.25), mat_planks, bevel=0.012)
    mb.build_object(parent=root_slab)

    # 2. oak-stairs (1 x 1 x 1 m, single L-shaped manifold)
    c_stairs = make_collection("oak-stairs")
    collections["oak-stairs"] = c_stairs
    root_stairs = bpy.data.objects.new("oak_stairs", None)
    c_stairs.objects.link(root_stairs)
    mb = MeshBuilder("OakStairsMesh", c_stairs)
    mb.add_stairs_manifold(mat_planks, bevel=0.012)
    mb.build_object(parent=root_stairs)

    # 3. oak-door (1m wide, 2m tall, 0.1875m thick, left hinge pivot)
    # In Godot: forward is -Z, left is -X.
    # Closed leaf occupies X: [-0.5, +0.5], Y: [0, 2.0], Z: [-0.5, -0.3125].
    # In Blender: +Y is Godot -Z, so closed leaf is Y: [0.3125, 0.5].
    # Hinge pivot in Blender: (-0.40625, 0.40625, 0.0) -> Godot (-0.40625, 0.0, -0.40625).
    c_door = make_collection("oak-door")
    collections["oak-door"] = c_door
    root_door = bpy.data.objects.new("oak_door", None)
    c_door.objects.link(root_door)

    hinge = bpy.data.objects.new("DoorHinge", None)
    c_door.objects.link(hinge)
    hinge.parent = root_door
    hinge.location = Vector((-0.40625, 0.40625, 0.0))

    # Build door leaf in hinge-local coords (hinge at X=0, Y=0, Z=0):
    # Leaf X spans [-0.09375, +0.90625] (center +0.40625, width 1.0)
    # Leaf Y spans [-0.09375, +0.09375] (center 0.0, thickness 0.1875)
    # Leaf Z spans [0.0, 2.0]
    mb = MeshBuilder("DoorLeaf", c_door)
    # Outer vertical stiles (left & right) — recessed slightly to 0.165m so 0.1875m iron hardware stays flush within cell
    mb.add_box((0.16, 0.165, 2.0), (-0.01375, 0.0, 1.0), mat_bark, bevel=0.008, vertical_grain=True)
    mb.add_box((0.16, 0.165, 2.0), (0.82625, 0.0, 1.0), mat_bark, bevel=0.008, vertical_grain=True)
    # Bottom, middle, and top horizontal rails
    mb.add_box((0.68, 0.165, 0.24), (0.40625, 0.0, 0.12), mat_bark, bevel=0.008)
    mb.add_box((0.68, 0.165, 0.20), (0.40625, 0.0, 1.00), mat_bark, bevel=0.008)
    mb.add_box((0.68, 0.165, 0.22), (0.40625, 0.0, 1.89), mat_bark, bevel=0.008)
    # Recessed lower & upper plank panels
    mb.add_box((0.68, 0.11, 0.66), (0.40625, 0.0, 0.57), mat_planks, bevel=0.006)
    mb.add_box((0.68, 0.11, 0.68), (0.40625, 0.0, 1.44), mat_planks, bevel=0.006)
    # Simple iron hinges on left & handle/latch on right (flush within 0.1875m total thickness)
    mb.add_box((0.12, 0.1875, 0.07), (0.01, 0.0, 0.36), mat_iron, bevel=0.004)
    mb.add_box((0.12, 0.1875, 0.07), (0.01, 0.0, 1.64), mat_iron, bevel=0.004)
    mb.add_box((0.09, 0.1875, 0.06), (0.79, 0.0, 0.98), mat_iron, bevel=0.005)
    mb.build_object(parent=hinge)

    # Rotate -90 deg around Z in Blender (= -90 deg around +Y in Godot) to swing inward (+Z).
    # Add Close first, Open second so Open (frame 0 = 0 deg CLOSED) is on top of the NLA stack at export frame 0.
    add_object_nla_rot(hinge, "Close", 18, (0, 0, -90), (0, 0, 0))
    add_object_nla_rot(hinge, "Open", 18, (0, 0, 0), (0, 0, -90))

    # 4. oak-fence (modular: Post + North, South, East, West)
    # Post: 0.25 x 1.0 x 0.25m (Y=0..1.0 in Godot, height <= 1.5m)
    # Rails: North (+Y Blender / -Z Godot), South (-Y Blender / +Z Godot), East (+X), West (-X)
    c_fence = make_collection("oak-fence")
    collections["oak-fence"] = c_fence
    root_fence = bpy.data.objects.new("oak_fence", None)
    c_fence.objects.link(root_fence)

    mb_post = MeshBuilder("Post", c_fence)
    mb_post.add_box((0.25, 0.25, 1.0), (0.0, 0.0, 0.5), mat_bark, bevel=0.010, vertical_grain=True)
    mb_post.build_object(parent=root_fence)

    rail_specs = {
        "North": ((0.12, 0.375, 0.14), (0.0, 0.3125, 0.35), (0.0, 0.3125, 0.78)),
        "South": ((0.12, 0.375, 0.14), (0.0, -0.3125, 0.35), (0.0, -0.3125, 0.78)),
        "East": ((0.375, 0.12, 0.14), (0.3125, 0.0, 0.35), (0.3125, 0.0, 0.78)),
        "West": ((0.375, 0.12, 0.14), (-0.3125, 0.0, 0.35), (-0.3125, 0.0, 0.78)),
    }
    for branch_name, (rsize, loc_low, loc_high) in rail_specs.items():
        mb_b = MeshBuilder(branch_name, c_fence)
        mb_b.add_box(rsize, loc_low, mat_planks, bevel=0.008)
        mb_b.add_box(rsize, loc_high, mat_planks, bevel=0.008)
        mb_b.build_object(parent=root_fence)

    # 5. wooden-chest (0.875 x 0.875 x 0.875 m, front -Z in Godot = +Y in Blender)
    c_chest = make_collection("wooden-chest")
    collections["wooden-chest"] = c_chest
    root_chest = bpy.data.objects.new("wooden_chest", None)
    c_chest.objects.link(root_chest)

    mb_base = MeshBuilder("ChestBase", c_chest)
    mb_base.add_box((0.875, 0.875, 0.5625), (0.0, 0.0, 0.28125), mat_planks, bevel=0.012)
    # Iron corner/side bands
    mb_base.add_box((0.89, 0.10, 0.54), (0.0, 0.36, 0.28), mat_iron, bevel=0.005)
    mb_base.add_box((0.89, 0.10, 0.54), (0.0, -0.36, 0.28), mat_iron, bevel=0.005)
    mb_base.build_object(parent=root_chest)

    # Lid hinge at back edge: Godot (0, 0.5625, +0.4375) -> Blender (0, -0.4375, 0.5625)
    lid_hinge = bpy.data.objects.new("LidHinge", None)
    c_chest.objects.link(lid_hinge)
    lid_hinge.parent = root_chest
    lid_hinge.location = Vector((0.0, -0.4375, 0.5625))

    mb_lid = MeshBuilder("ChestLid", c_chest)
    # Lid in hinge-local coords: Y in [0, +0.875] -> center +0.4375; Z in [0, 0.3125] -> center +0.15625
    mb_lid.add_box((0.875, 0.875, 0.3125), (0.0, 0.4375, 0.15625), mat_planks, bevel=0.012)
    # Iron straps & front latch clasp (+Y is front in Blender)
    mb_lid.add_box((0.89, 0.10, 0.32), (0.0, 0.7975, 0.16), mat_iron, bevel=0.005)
    mb_lid.add_box((0.89, 0.10, 0.32), (0.0, 0.0775, 0.16), mat_iron, bevel=0.005)
    mb_lid.add_box((0.14, 0.06, 0.14), (0.0, 0.885, -0.02), mat_iron, bevel=0.005)
    mb_lid.build_object(parent=lid_hinge)

    # Rotate +90 deg around X in Blender so front (+Y) swings UP (+Z) and back.
    # Add Close first, Open second so Open (frame 0 = 0 deg CLOSED) is on top of the NLA stack at export frame 0.
    add_object_nla_rot(lid_hinge, "Close", 16, (90, 0, 0), (0, 0, 0))
    add_object_nla_rot(lid_hinge, "Open", 16, (0, 0, 0), (90, 0, 0))

    return collections


def export_collection_glb(col: bpy.types.Collection, glb_path: Path) -> None:
    bpy.context.scene.frame_set(0)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in col.objects:
        obj.select_set(True)

    bpy.ops.export_scene.gltf(
        filepath=str(glb_path),
        use_selection=True,
        export_format="GLB",
        export_yup=True,
        export_apply=False,
        export_materials="EXPORT",
        export_image_format="AUTO",
        export_texcoords=True,
        export_normals=True,
        export_animations=True,
        export_animation_mode="NLA_TRACKS",
        export_nla_strips=True,
        export_nla_strips_merged_animation_name="",
        export_optimize_animation_size=False,
    )


def measure_godot_bounds(objs: list[bpy.types.Object], depsgraph) -> dict:
    xs, ys, zs = [], [], []
    for mo in objs:
        if mo.type != "MESH":
            continue
        eval_obj = mo.evaluated_get(depsgraph)
        tmp = eval_obj.to_mesh()
        mat_w = eval_obj.matrix_world
        for v in tmp.vertices:
            w = mat_w @ v.co
            gx, gy, gz = w.x, w.z, -w.y
            xs.append(gx)
            ys.append(gy)
            zs.append(gz)
        eval_obj.to_mesh_clear()
    return {
        "min_x": round(min(xs), 4),
        "max_x": round(max(xs), 4),
        "min_y": round(min(ys), 4),
        "max_y": round(max(ys), 4),
        "min_z": round(min(zs), 4),
        "max_z": round(max(zs), 4),
        "width_x": round(max(xs) - min(xs), 4),
        "height_y": round(max(ys) - min(ys), 4),
        "depth_z": round(max(zs) - min(zs), 4),
    }


def render_views(name: str, bounds: dict, suffix: str = "") -> list[str]:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 720
    scene.render.resolution_y = 540
    scene.render.image_settings.file_format = "PNG"

    # Floor plane at Z = -0.001
    ground = bpy.data.objects.get("PreviewFloor")
    if not ground:
        bpy.ops.mesh.primitive_plane_add(size=3.0, location=(0, 0, -0.001))
        ground = bpy.context.active_object
        ground.name = "PreviewFloor"

    cam_obj = bpy.data.objects.get("PreviewCam")
    if not cam_obj:
        cam_data = bpy.data.cameras.new("PreviewCam")
        cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
        scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    cam_data = cam_obj.data

    cx = (bounds["min_x"] + bounds["max_x"]) * 0.5
    cy_b = -(bounds["min_z"] + bounds["max_z"]) * 0.5
    cz_b = (bounds["min_y"] + bounds["max_y"]) * 0.5
    span = max(1.0, bounds["width_x"], bounds["height_y"], bounds["depth_z"])

    def point(loc, target):
        cam_obj.location = Vector(loc)
        cam_obj.rotation_euler = (Vector(target) - cam_obj.location).to_track_quat("-Z", "Y").to_euler()

    written = []
    if not suffix:
        # 3/4 front view (+Y in Blender = -Z in Godot)
        cam_data.type = "PERSP"
        d = span * 1.9
        point((cx + d * 0.72, cy_b + d * 0.85, cz_b + d * 0.45), (cx, cy_b, cz_b))
        p = PREVIEW_DIR / f"{name}-front.png"
        scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        written.append(f"previews/{p.name}")

        # Orthographic side view (+X)
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = span * 1.45
        point((cx + 3.0, cy_b, cz_b), (cx, cy_b, cz_b))
        p = PREVIEW_DIR / f"{name}-side.png"
        scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        written.append(f"previews/{p.name}")

        # Orthographic top view (+Z)
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = span * 1.45
        point((cx, cy_b - 0.001, cz_b + 3.0), (cx, cy_b, cz_b))
        p = PREVIEW_DIR / f"{name}-top.png"
        scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        written.append(f"previews/{p.name}")
    else:
        cam_data.type = "PERSP"
        d = span * 1.9
        point((cx + d * 0.72, cy_b + d * 0.85, cz_b + d * 0.45), (cx, cy_b, cz_b))
        p = PREVIEW_DIR / f"{name}-{suffix}.png"
        scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        written.append(f"previews/{p.name}")

    return written


def _read_glb_chunks(glb_path: Path):
    import struct
    data = glb_path.read_bytes()
    magic, version, total_len = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, f"Invalid GLB magic in {glb_path}"
    offset = 12
    gltf_json = None
    bin_chunk = b""
    while offset < len(data):
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        offset += 8
        chunk_data = data[offset:offset + chunk_len]
        offset += chunk_len
        if chunk_type == 0x4E4F534A:
            gltf_json = json.loads(chunk_data.decode("utf-8"))
        elif chunk_type == 0x004E4942:
            bin_chunk = chunk_data
    return gltf_json, bin_chunk


def _read_accessor_floats(gltf: dict, bin_chunk: bytes, acc_idx: int) -> list[tuple[float, ...]]:
    import struct
    acc = gltf["accessors"][acc_idx]
    bv = gltf["bufferViews"][acc["bufferView"]]
    base_off = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = bv.get("byteStride")
    comp_counts = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
    n_comp = comp_counts[acc["type"]]
    assert acc["componentType"] == 5126, f"Expected FLOAT accessor, got {acc['componentType']}"
    elem_size = 4 * n_comp
    step = stride if stride else elem_size
    out = []
    for i in range(acc["count"]):
        out.append(struct.unpack_from(f"<{n_comp}f", bin_chunk, base_off + i * step))
    return out


def _measure_glb_binary(glb_path: Path) -> dict:
    from mathutils import Quaternion as MQuat
    gltf, bin_chunk = _read_glb_chunks(glb_path)
    nodes = gltf.get("nodes", [])
    meshes = gltf.get("meshes", [])
    scenes = gltf.get("scenes", [])
    roots = scenes[gltf.get("scene", 0)]["nodes"] if scenes else [0]

    # Build parent -> children map
    children_map = {i: n.get("children", []) for i, n in enumerate(nodes)}

    def eval_bounds(node_overrides: dict | None = None) -> dict:
        overrides = node_overrides or {}
        xs, ys, zs = [], [], []

        def walk(ni: int, parent_mat: Matrix):
            n = nodes[ni]
            ov = overrides.get(ni, {})
            t = Vector(ov.get("translation", n.get("translation", (0.0, 0.0, 0.0))))
            rq = ov.get("rotation", n.get("rotation", (0.0, 0.0, 0.0, 1.0)))  # glTF xyzw
            q = MQuat((rq[3], rq[0], rq[1], rq[2]))
            s = Vector(ov.get("scale", n.get("scale", (1.0, 1.0, 1.0))))
            local = Matrix.Translation(t) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((s.x, s.y, s.z, 1.0))
            world = parent_mat @ local
            if "mesh" in n:
                mesh = meshes[n["mesh"]]
                for prim in mesh.get("primitives", []):
                    pos_acc = prim["attributes"]["POSITION"]
                    for vx, vy, vz in _read_accessor_floats(gltf, bin_chunk, pos_acc):
                        w = world @ Vector((vx, vy, vz))
                        xs.append(w.x)
                        ys.append(w.y)
                        zs.append(w.z)
            for ci in children_map.get(ni, []):
                walk(ci, world)

        for r in roots:
            walk(r, Matrix.Identity(4))
        return {
            "min_x": round(min(xs), 4),
            "max_x": round(max(xs), 4),
            "min_y": round(min(ys), 4),
            "max_y": round(max(ys), 4),
            "min_z": round(min(zs), 4),
            "max_z": round(max(zs), 4),
            "width_x": round(max(xs) - min(xs), 4),
            "height_y": round(max(ys) - min(ys), 4),
            "depth_z": round(max(zs) - min(zs), 4),
        }

    rest_bounds = eval_bounds()
    open_bounds = None
    close_end_bounds = None
    sweep_bounds = None
    for anim in gltf.get("animations", []):
        aname = anim.get("name", "")
        samplers = anim.get("samplers", [])
        channels = anim.get("channels", [])
        # Collect keyframe times for each channel
        max_steps = 1
        chan_data = []
        for ch in channels:
            tgt = ch["target"]
            smp = samplers[ch["sampler"]]
            times = [t[0] for t in _read_accessor_floats(gltf, bin_chunk, smp["input"])]
            vals = _read_accessor_floats(gltf, bin_chunk, smp["output"])
            max_steps = max(max_steps, len(times))
            chan_data.append((tgt["node"], tgt["path"], times, vals))

        if aname == "Open" and chan_data:
            # End frame
            ov_end = {}
            for ni, path, times, vals in chan_data:
                ov_end.setdefault(ni, {})[path] = vals[-1]
            open_bounds = eval_bounds(ov_end)

            # Full sweep across all keyframe samples
            sweep_acc = None
            for step_idx in range(max_steps):
                ov_step = {}
                for ni, path, times, vals in chan_data:
                    idx = min(step_idx, len(vals) - 1)
                    ov_step.setdefault(ni, {})[path] = vals[idx]
                fb = eval_bounds(ov_step)
                if sweep_acc is None:
                    sweep_acc = dict(fb)
                else:
                    sweep_acc["min_x"] = min(sweep_acc["min_x"], fb["min_x"])
                    sweep_acc["max_x"] = max(sweep_acc["max_x"], fb["max_x"])
                    sweep_acc["min_y"] = min(sweep_acc["min_y"], fb["min_y"])
                    sweep_acc["max_y"] = max(sweep_acc["max_y"], fb["max_y"])
                    sweep_acc["min_z"] = min(sweep_acc["min_z"], fb["min_z"])
                    sweep_acc["max_z"] = max(sweep_acc["max_z"], fb["max_z"])
            if sweep_acc is not None:
                sweep_acc["width_x"] = round(sweep_acc["max_x"] - sweep_acc["min_x"], 4)
                sweep_acc["height_y"] = round(sweep_acc["max_y"] - sweep_acc["min_y"], 4)
                sweep_acc["depth_z"] = round(sweep_acc["max_z"] - sweep_acc["min_z"], 4)
                sweep_bounds = sweep_acc
        elif aname == "Close" and chan_data:
            ov_end = {}
            for ni, path, times, vals in chan_data:
                ov_end.setdefault(ni, {})[path] = vals[-1]
            close_end_bounds = eval_bounds(ov_end)

    return {
        "rest_bounds": rest_bounds,
        "open_bounds": open_bounds,
        "sweep_bounds": sweep_bounds,
        "rest_matches_close_end": (close_end_bounds == rest_bounds) if close_end_bounds is not None else None,
    }


def verify_glb(name: str, glb_path: Path) -> dict:
    bin_stats = _measure_glb_binary(glb_path)
    rest_bounds = bin_stats["rest_bounds"]
    open_bounds = bin_stats["open_bounds"]
    sweep_bounds = bin_stats["sweep_bounds"]
    rest_matches_close_end = bin_stats["rest_matches_close_end"]

    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(glb_path))

    mesh_objs = [o for o in bpy.data.objects if o.type == "MESH"]
    total_tris = 0
    total_verts = 0
    mat_names = set()
    for mo in mesh_objs:
        mo.data.calc_loop_triangles()
        total_tris += len(mo.data.loop_triangles)
        total_verts += len(mo.data.vertices)
        for m in mo.data.materials:
            if m:
                mat_names.add(m.name)

    external_images = [img.filepath for img in bpy.data.images if img.filepath and not img.packed_file]
    depsgraph = bpy.context.evaluated_depsgraph_get()
    nodes = sorted(o.name for o in bpy.data.objects if o.name not in ("PreviewFloor", "PreviewCam"))

    # Ensure Blender preview renders CLOSED state first (apply Close end frame if animated)
    animations = []
    for obj in bpy.data.objects:
        if obj.animation_data and obj.animation_data.nla_tracks:
            for t in obj.animation_data.nla_tracks:
                animations.append(t.name)
                if t.name == "Close":
                    strip = t.strips[0]
                    obj.animation_data.action = strip.action
                    if hasattr(obj.animation_data, "action_slot") and strip.action.slots:
                        obj.animation_data.action_slot = strip.action.slots[0]
                    bpy.context.scene.frame_set(int(strip.frame_end))
                    depsgraph.update()

    previews = render_views(name, rest_bounds)

    # Also render Open end frame preview
    for obj in bpy.data.objects:
        if obj.animation_data and obj.animation_data.nla_tracks:
            for t in obj.animation_data.nla_tracks:
                if t.name == "Open":
                    strip = t.strips[0]
                    obj.animation_data.action = strip.action
                    if hasattr(obj.animation_data, "action_slot") and strip.action.slots:
                        obj.animation_data.action_slot = strip.action.slots[0]
                    bpy.context.scene.frame_set(int(strip.frame_end))
                    depsgraph.update()
                    previews.extend(render_views(name, open_bounds, suffix="open"))

    # For oak-fence, also measure post-only bounds
    post_bounds = None
    if name == "oak-fence":
        post_objs = [o for o in mesh_objs if o.name == "Post"]
        if post_objs:
            post_bounds = measure_godot_bounds(post_objs, depsgraph)

    sha = hashlib.sha256(glb_path.read_bytes()).hexdigest()
    return {
        "name": name,
        "glb": f"exports/{glb_path.name}",
        "sha256": sha,
        "vertices": total_verts,
        "triangles": total_tris,
        "materials": sorted(mat_names),
        "unpacked_external_images": external_images,
        "nodes": nodes,
        "animations": sorted(set(animations)),
        "godot_bounds_closed_or_rest": rest_bounds,
        "godot_bounds_open": open_bounds,
        "godot_bounds_animation_sweep": sweep_bounds,
        "rest_matches_close_end": rest_matches_close_end,
        "godot_bounds_post_only": post_bounds,
        "previews": previews,
    }


def main() -> None:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    EXPORT_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    clear_scene()
    collections = build_all_blocks()

    blend_path = SOURCE_DIR / "blocks-v1.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

    for name, col in collections.items():
        export_collection_glb(col, EXPORT_DIR / f"{name}.glb")

    verified = {}
    for name in collections.keys():
        verified[name] = verify_glb(name, EXPORT_DIR / f"{name}.glb")

    # Build block-models.json
    metadata_extra = {
        "oak-slab": {
            "namespaced_id": "minecraft:oak_slab",
            "anchor": "bottom-center (0, 0, 0) in cell",
            "support_requirement": "none (self-supporting half block)",
            "occupancy_cells": [[0, 0, 0]],
            "clearance_cells": [],
        },
        "oak-stairs": {
            "namespaced_id": "minecraft:oak_stairs",
            "anchor": "bottom-center (0, 0, 0), ascends toward +Z (front step faces -Z)",
            "support_requirement": "none (self-supporting)",
            "occupancy_cells": [[0, 0, 0]],
            "clearance_cells": [],
        },
        "oak-door": {
            "namespaced_id": "minecraft:oak_door",
            "anchor": "bottom-center (0, 0, 0) of lower cell; DoorHinge at (-0.40625, 0, -0.40625)",
            "support_requirement": "requires solid block under [0, -1, 0]",
            "occupancy_cells": [[0, 0, 0], [0, 1, 0]],
            "clearance_cells": [[-1, 0, 0], [-1, 1, 0], [0, 0, -1], [0, 1, -1], [0, 0, 1], [0, 1, 1]],
            "clearance_note": "Closed (rest) and Open endpoints fit strictly within [0,0,0] and [0,1,0] (0.0m -X overhang at rest/open). Mid-swing rotation sweeps ~0.039m into -X/-Z around the hinge corner and ~0.005m into +Z at the latch edge.",
        },
        "oak-fence": {
            "namespaced_id": "minecraft:oak_fence",
            "anchor": "bottom-center (0, 0, 0); toggle North/South/East/West branches by neighbor",
            "support_requirement": "none (self-supporting)",
            "occupancy_cells": [[0, 0, 0]],
            "clearance_cells": [],
            "geometry_height_note": "Geometry height is 1.0 m; unjumpable 1.5m behavior requires future game-side collision/physics implementation.",
        },
        "wooden-chest": {
            "namespaced_id": "minecraft:chest",
            "anchor": "bottom-center (0, 0, 0); LidHinge at (0, 0.5625, +0.4375); front faces -Z",
            "support_requirement": "none (self-supporting)",
            "occupancy_cells": [[0, 0, 0]],
            "clearance_cells": [[0, 1, 0], [0, 0, 1], [0, 1, 1]],
            "clearance_note": "Opening the hinged lid swings it upward into [0,1,0] (y=1.4775m) and backward into rear +Z cells [0,0,1] and [0,1,1] (z=0.7575m).",
        },
    }

    block_models = {
        "project": "JOSCRAFT",
        "target_engine": "Godot 4.7.2 Mono",
        "authorship": "Original procedural models, UVs, pivots, and animations created by Antigravity using JOSCRAFT oak-planks-v1.png and oak-bark-v1.png.",
        "coordinate_convention": {"units": "meters", "up": "+Y", "forward": "-Z", "cell_xz": "[-0.5, 0.5]"},
        "measurement_method": "Direct GLB binary chunk evaluation (nodes, mesh POSITION accessors, and Open/Close animation keyframes across rest, endpoints, and full sweep) plus Blender 5.2.2 GLB reimport for triangle/material/preview verification (not a Godot runtime physics test).",
        "blocks": {},
    }
    for name, v in verified.items():
        block_models["blocks"][name] = {**metadata_extra[name], **v}

    (BASE_DIR / "block-models.json").write_text(json.dumps(block_models, indent=2), encoding="utf-8")

    # Build conservative collision-boxes.json in Godot coordinates (center, size)
    collision_boxes = {
        "coordinate_convention": "Godot (meters, +Y up, -Z forward, cell X/Z in [-0.5, 0.5])",
        "blocks": {
            "minecraft:oak_slab": {
                "default": [
                    {"center": [0.0, 0.25, 0.0], "size": [1.0, 0.5, 1.0]}
                ]
            },
            "minecraft:oak_stairs": {
                "default": [
                    {"center": [0.0, 0.25, 0.0], "size": [1.0, 0.5, 1.0]},
                    {"center": [0.0, 0.75, 0.25], "size": [1.0, 0.5, 0.5]},
                ]
            },
            "minecraft:oak_door": {
                "closed": [
                    {"center": [0.0, 1.0, -0.40625], "size": [1.0, 2.0, 0.1875]}
                ],
                "open": [
                    {"center": [-0.40625, 1.0, 0.0], "size": [0.1875, 2.0, 1.0]}
                ],
            },
            "minecraft:oak_fence": {
                "note": "Post geometry height is 1.0m (<=1.5m). Combine Post with active neighbor branches.",
                "post": [{"center": [0.0, 0.5, 0.0], "size": [0.25, 1.0, 0.25]}],
                "North": [{"center": [0.0, 0.565, -0.3125], "size": [0.12, 0.57, 0.375]}],
                "South": [{"center": [0.0, 0.565, 0.3125], "size": [0.12, 0.57, 0.375]}],
                "East": [{"center": [0.3125, 0.565, 0.0], "size": [0.375, 0.57, 0.12]}],
                "West": [{"center": [-0.3125, 0.565, 0.0], "size": [0.375, 0.57, 0.12]}],
            },
            "minecraft:chest": {
                "closed": [
                    {"name": "ChestBase", "center": [0.0, 0.28125, 0.0], "size": [0.89, 0.5625, 0.875]},
                    {"name": "ChestLid", "center": [0.0, 0.6775, -0.02], "size": [0.89, 0.41, 0.915]},
                ],
                "open": [
                    {"name": "ChestBase", "center": [0.0, 0.28125, 0.0], "size": [0.89, 0.5625, 0.875]},
                    {"name": "ChestLid_Open", "center": [0.0, 1.02, 0.5525], "size": [0.89, 0.915, 0.41]},
                ],
            },
        },
    }
    (BASE_DIR / "collision-boxes.json").write_text(json.dumps(collision_boxes, indent=2), encoding="utf-8")
    print("DONE: blocks-v1 generated and verified.")


if __name__ == "__main__":
    main()
