"""
Original low-poly / stylized blocky quadruped cat companion generator for JOSCRAFT (Godot 4.7.2 Mono).
Generates source/cat-companion.blend, exports/cat-companion.glb, previews/ (preview, side, animations sheet),
and manifest.json after full GLB re-import verification.

Author: Antigravity (original procedural geometry, rig, vertex palette, and animations)
Blender: 5.2+
"""

import hashlib
import json
import math
from pathlib import Path

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

BASE_DIR = Path(r"D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-cat")
SOURCE_DIR = BASE_DIR / "source"
EXPORT_DIR = BASE_DIR / "exports"
PREVIEW_DIR = BASE_DIR / "previews"


def hex_to_srgb_rgba(hex_str: str) -> tuple[float, float, float, float]:
    h = hex_str.lstrip("#")
    r = int(h[0:2], 16) / 255.0
    g = int(h[2:4], 16) / 255.0
    b = int(h[4:6], 16) / 255.0
    return (r, g, b, 1.0)


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def create_palette_material(name: str, roughness: float = 0.84) -> bpy.types.Material:
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (300, 0)

    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (0, 0)
    bsdf.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.25

    vcol = nt.nodes.new("ShaderNodeVertexColor")
    vcol.location = (-260, 0)
    vcol.layer_name = "CatPalette"

    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class PartBuilder:
    """Accumulates beveled blocky/low-poly parts with per-face vertex colors and bone weights."""

    def __init__(self, name: str):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("CatPalette")
        self.pending_parts: list[dict] = []

    def add_box(
        self,
        size: tuple[float, float, float],
        center: tuple[float, float, float],
        color_hex: str,
        bone: str,
        bevel: float = 0.014,
        segments: int = 1,
        rot_deg: tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> None:
        # Rotate 180 deg around Z so Blender +Y is forward (maps to Godot -Z forward on glTF export).
        cx, cy, cz = center
        rx, ry, rz = rot_deg
        self.pending_parts.append(
            {
                "kind": "box",
                "size": size,
                "center": (-cx, -cy, cz),
                "color": hex_to_srgb_rgba(color_hex),
                "bone": bone,
                "bevel": bevel,
                "segments": segments,
                "rot_deg": (-rx, -ry, rz),
            }
        )

    def add_ear_prism(
        self,
        width: float,
        depth: float,
        height: float,
        center: tuple[float, float, float],
        color_hex: str,
        bone: str,
        rot_deg: tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> None:
        """Triangular feline ear prism (tapered tip) with subtle bevel."""
        cx, cy, cz = center
        rx, ry, rz = rot_deg
        self.pending_parts.append(
            {
                "kind": "ear",
                "size": (width, depth, height),
                "center": (-cx, -cy, cz),
                "color": hex_to_srgb_rgba(color_hex),
                "bone": bone,
                "bevel": 0.006,
                "segments": 1,
                "rot_deg": (-rx, -ry, rz),
            }
        )

    def build_mesh(self, material: bpy.types.Material) -> bpy.types.Object:
        vert_bone_map: list[tuple[int, str]] = []

        for spec in self.pending_parts:
            sub = bmesh.new()
            sx, sy, sz = spec["size"]
            bmesh.ops.create_cube(sub, size=1.0)
            for v in sub.verts:
                v.co.x *= sx
                v.co.y *= sy
                v.co.z *= sz
                if spec["kind"] == "ear" and v.co.z > 0:
                    # Taper top into a cute rounded triangle
                    v.co.x *= 0.28
                    v.co.y *= 0.45

            bev = min(spec["bevel"], min(sx, sy, sz) * 0.25)
            if bev > 0.0005:
                bmesh.ops.bevel(
                    sub,
                    geom=list(sub.edges),
                    offset=bev,
                    segments=spec["segments"],
                    profile=0.5,
                    affect="EDGES",
                    clamp_overlap=True,
                )

            rot = Euler(tuple(math.radians(a) for a in spec["rot_deg"]), "XYZ").to_matrix().to_4x4()
            trans = Matrix.Translation(Vector(spec["center"]))
            bmesh.ops.transform(sub, matrix=trans @ rot, verts=list(sub.verts))

            v_map = {}
            for sv in sub.verts:
                nv = self.bm.verts.new(sv.co)
                v_map[sv] = nv
            self.bm.verts.ensure_lookup_table()
            self.bm.verts.index_update()

            col = spec["color"]
            bone = spec["bone"]
            for sf in sub.faces:
                nf = self.bm.faces.new([v_map[v] for v in sf.verts])
                nf.smooth = False
                for loop in nf.loops:
                    loop[self.color_layer] = col
                for v in nf.verts:
                    vert_bone_map.append((v.index, bone))

            sub.free()

        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))

        mesh = bpy.data.meshes.new(f"{self.name}_Mesh")
        self.bm.to_mesh(mesh)
        self.bm.free()

        if "CatPalette" in mesh.color_attributes:
            mesh.color_attributes.active_color = mesh.color_attributes["CatPalette"]

        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.data.materials.append(material)

        vg_cache: dict[str, bpy.types.VertexGroup] = {}
        v_to_bone: dict[int, str] = {}
        for v_idx, bone_name in vert_bone_map:
            v_to_bone[v_idx] = bone_name

        for v_idx, bone_name in v_to_bone.items():
            if bone_name not in vg_cache:
                vg_cache[bone_name] = obj.vertex_groups.new(name=bone_name)
            vg_cache[bone_name].add([v_idx], 1.0, "REPLACE")

        return obj


def build_armature(name: str, bone_defs: list[dict]) -> bpy.types.Object:
    arm_data = bpy.data.armatures.new(f"{name}_Armature")
    arm_obj = bpy.data.objects.new(f"{name}_Rig", arm_data)
    bpy.context.collection.objects.link(arm_obj)

    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")

    ebones = {}
    for bd in bone_defs:
        eb = arm_data.edit_bones.new(bd["name"])
        hx, hy, hz = bd["head"]
        tx, ty, tz = bd["tail"]
        eb.head = Vector((-hx, -hy, hz))
        eb.tail = Vector((-tx, -ty, tz))
        eb.roll = 0.0
        if bd.get("parent"):
            eb.parent = ebones[bd["parent"]]
            eb.use_connect = False
        ebones[bd["name"]] = eb

    bpy.ops.object.mode_set(mode="OBJECT")
    return arm_obj


def attach_mesh_to_armature(mesh_obj: bpy.types.Object, arm_obj: bpy.types.Object) -> None:
    mesh_obj.parent = arm_obj
    mod = mesh_obj.modifiers.new(name="Armature", type="ARMATURE")
    mod.object = arm_obj
    mod.use_vertex_groups = True


def set_pose_keys(
    arm_obj: bpy.types.Object,
    action_name: str,
    frame_end: int,
    keyframes: dict[int, dict[str, dict]],
    loop: bool = True,
) -> bpy.types.Action:
    if not arm_obj.animation_data:
        arm_obj.animation_data_create()

    action = bpy.data.actions.new(name=action_name)
    action.use_frame_range = True
    action.frame_start = 0
    action.frame_end = frame_end
    if hasattr(action, "use_cyclic"):
        action.use_cyclic = loop

    arm_obj.animation_data.action = action
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="POSE")

    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)

    for frame in sorted(keyframes.keys()):
        bpy.context.scene.frame_set(frame)
        for pb in arm_obj.pose.bones:
            pb.rotation_euler = (0.0, 0.0, 0.0)
            pb.location = (0.0, 0.0, 0.0)

        for bone_name, transforms in keyframes[frame].items():
            pb = arm_obj.pose.bones.get(bone_name)
            if not pb:
                continue
            if "rot" in transforms:
                rx, ry, rz = transforms["rot"]
                pb.rotation_euler = (math.radians(-rx), math.radians(-ry), math.radians(rz))
                pb.keyframe_insert(data_path="rotation_euler", frame=frame)
            if "loc" in transforms:
                lx, ly, lz = transforms["loc"]
                pb.location = Vector((-lx, -ly, lz))
                pb.keyframe_insert(data_path="location", frame=frame)

    bpy.ops.object.mode_set(mode="OBJECT")

    track = arm_obj.animation_data.nla_tracks.new()
    track.name = action_name
    strip = track.strips.new(action_name, start=0, action=action)
    strip.name = action_name
    arm_obj.animation_data.action = None
    return action


def build_cat():
    # Target: height incl. ears ~0.75m, width <= 0.45m, depth incl. tail ~1.10m
    # Ginger tabby cat: warm orange fur, subtle darker tabby stripes, cream muzzle/chest/paws,
    # pink nose & inner ears, bright green eyes.
    ginger = "#E3843B"
    stripe = "#B3551D"
    cream = "#F5ECDA"
    pink = "#E6A2A4"
    green_eye = "#46B856"
    pupil = "#16241B"

    bones = [
        {"name": "root", "head": (0, 0, 0), "tail": (0, 0, 0.15), "parent": None},
        {"name": "body", "head": (0, 0.02, 0.31), "tail": (0, -0.22, 0.31), "parent": "root"},
        {"name": "head", "head": (0, -0.24, 0.42), "tail": (0, -0.28, 0.62), "parent": "body"},
        {"name": "ear_L", "head": (-0.10, -0.26, 0.62), "tail": (-0.11, -0.26, 0.75), "parent": "head"},
        {"name": "ear_R", "head": (0.10, -0.26, 0.62), "tail": (0.11, -0.26, 0.75), "parent": "head"},
        {"name": "tail_base", "head": (0, 0.25, 0.36), "tail": (0, 0.44, 0.48), "parent": "body"},
        {"name": "tail_tip", "head": (0, 0.44, 0.48), "tail": (0, 0.64, 0.58), "parent": "tail_base"},
        {"name": "leg_FL", "head": (-0.10, -0.15, 0.24), "tail": (-0.10, -0.15, 0.0), "parent": "body"},
        {"name": "leg_FR", "head": (0.10, -0.15, 0.24), "tail": (0.10, -0.15, 0.0), "parent": "body"},
        {"name": "leg_BL", "head": (-0.10, 0.17, 0.24), "tail": (-0.10, 0.17, 0.0), "parent": "body"},
        {"name": "leg_BR", "head": (0.10, 0.17, 0.24), "tail": (0.10, 0.17, 0.0), "parent": "body"},
    ]

    pb = PartBuilder("cat_companion")

    # Sleek feline body + cream chest/belly
    pb.add_box((0.28, 0.50, 0.23), (0.0, 0.01, 0.32), ginger, "body", bevel=0.018)
    pb.add_box((0.20, 0.38, 0.06), (0.0, 0.0, 0.21), cream, "body", bevel=0.012)
    pb.add_box((0.22, 0.10, 0.19), (0.0, -0.21, 0.33), cream, "body", bevel=0.014)

    # Subtle tabby stripes across the back & flanks
    for sy in (-0.10, 0.02, 0.14):
        pb.add_box((0.295, 0.045, 0.11), (0.0, sy, 0.385), stripe, "body", bevel=0.006)

    # Rounder feline head + cheek/muzzle puffs + tabby forehead 'M' / stripes
    pb.add_box((0.30, 0.25, 0.24), (0.0, -0.27, 0.50), ginger, "head", bevel=0.020)
    pb.add_box((0.20, 0.09, 0.10), (0.0, -0.40, 0.44), cream, "head", bevel=0.012)
    pb.add_box((0.32, 0.12, 0.08), (0.0, -0.29, 0.44), cream, "head", bevel=0.010)
    # Cute pink nose
    pb.add_box((0.055, 0.035, 0.04), (0.0, -0.445, 0.475), pink, "head", bevel=0.006)
    # Bright green cat eyes + dark vertical pupils
    pb.add_box((0.065, 0.025, 0.055), (-0.085, -0.395, 0.525), green_eye, "head", bevel=0.004)
    pb.add_box((0.065, 0.025, 0.055), (0.085, -0.395, 0.525), green_eye, "head", bevel=0.004)
    pb.add_box((0.024, 0.028, 0.042), (-0.085, -0.402, 0.525), pupil, "head", bevel=0.002)
    pb.add_box((0.024, 0.028, 0.042), (0.085, -0.402, 0.525), pupil, "head", bevel=0.002)
    # Forehead tabby stripes
    pb.add_box((0.03, 0.08, 0.02), (-0.05, -0.32, 0.62), stripe, "head", bevel=0.003)
    pb.add_box((0.03, 0.08, 0.02), (0.0, -0.33, 0.62), stripe, "head", bevel=0.003)
    pb.add_box((0.03, 0.08, 0.02), (0.05, -0.32, 0.62), stripe, "head", bevel=0.003)

    # Triangular feline ears (total height ~ 0.745m)
    pb.add_ear_prism(0.095, 0.075, 0.14, (-0.10, -0.26, 0.68), ginger, "ear_L", rot_deg=(0, 8, 0))
    pb.add_ear_prism(0.095, 0.075, 0.14, (0.10, -0.26, 0.68), ginger, "ear_R", rot_deg=(0, -8, 0))
    pb.add_ear_prism(0.05, 0.02, 0.085, (-0.10, -0.29, 0.665), pink, "ear_L", rot_deg=(0, 8, 0))
    pb.add_ear_prism(0.05, 0.02, 0.085, (0.10, -0.29, 0.665), pink, "ear_R", rot_deg=(0, -8, 0))

    # Long graceful two-segment tail with tabby rings (reaches ~+0.64m => total depth ~1.10m)
    pb.add_box((0.085, 0.24, 0.085), (0.0, 0.35, 0.42), ginger, "tail_base", bevel=0.014, rot_deg=(28, 0, 0))
    pb.add_box((0.09, 0.06, 0.09), (0.0, 0.36, 0.425), stripe, "tail_base", bevel=0.010, rot_deg=(28, 0, 0))
    pb.add_box((0.08, 0.24, 0.08), (0.0, 0.54, 0.53), ginger, "tail_tip", bevel=0.014, rot_deg=(18, 0, 0))
    pb.add_box((0.082, 0.08, 0.082), (0.0, 0.62, 0.555), cream, "tail_tip", bevel=0.012, rot_deg=(18, 0, 0))

    # 4 slender feline legs with distinct cream paws touching Z = 0.0
    for bname, lx, ly in [
        ("leg_FL", -0.10, -0.15),
        ("leg_FR", 0.10, -0.15),
        ("leg_BL", -0.10, 0.17),
        ("leg_BR", 0.10, 0.17),
    ]:
        pb.add_box((0.085, 0.09, 0.18), (lx, ly, 0.14), ginger, bname, bevel=0.012)
        pb.add_box((0.095, 0.115, 0.055), (lx, ly - 0.01, 0.0275), cream, bname, bevel=0.009)

    arm_obj = build_armature("cat_companion", bones)
    mat = create_palette_material("cat_Palette")
    mesh_obj = pb.build_mesh(mat)
    attach_mesh_to_armature(mesh_obj, arm_obj)

    # Animations: Idle, Walk, Run, Jump, Sit, Hurt, Purr
    set_pose_keys(
        arm_obj,
        "Idle",
        48,
        {
            0: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail_base": {"rot": (0, 0, -10)}, "tail_tip": {"rot": (6, 0, -12)}},
            12: {"body": {"loc": (0, 0, 0.008)}, "head": {"rot": (2, 0, 4)}, "tail_base": {"rot": (4, 0, 10)}, "tail_tip": {"rot": (12, 0, 14)}, "ear_L": {"rot": (6, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail_base": {"rot": (0, 0, -10)}, "tail_tip": {"rot": (6, 0, -12)}},
            36: {"body": {"loc": (0, 0, 0.008)}, "head": {"rot": (-2, 0, -4)}, "tail_base": {"rot": (4, 0, 10)}, "tail_tip": {"rot": (12, 0, 14)}, "ear_R": {"rot": (6, 0, 0)}},
            48: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail_base": {"rot": (0, 0, -10)}, "tail_tip": {"rot": (6, 0, -12)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Walk",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.015)}, "leg_FL": {"rot": (24, 0, 0)}, "leg_BR": {"rot": (24, 0, 0)}, "leg_FR": {"rot": (-20, 0, 0)}, "leg_BL": {"rot": (-20, 0, 0)}, "tail_base": {"rot": (0, 0, -8)}, "tail_tip": {"rot": (8, 0, -10)}},
            8: {"body": {"loc": (0, 0, 0.022)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "tail_base": {"rot": (4, 0, 0)}, "tail_tip": {"rot": (10, 0, 0)}},
            16: {"body": {"loc": (0, 0, 0.015)}, "leg_FL": {"rot": (-20, 0, 0)}, "leg_BR": {"rot": (-20, 0, 0)}, "leg_FR": {"rot": (24, 0, 0)}, "leg_BL": {"rot": (24, 0, 0)}, "tail_base": {"rot": (0, 0, 8)}, "tail_tip": {"rot": (8, 0, 10)}},
            24: {"body": {"loc": (0, 0, 0.022)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "tail_base": {"rot": (4, 0, 0)}, "tail_tip": {"rot": (10, 0, 0)}},
            32: {"body": {"loc": (0, 0, 0.015)}, "leg_FL": {"rot": (24, 0, 0)}, "leg_BR": {"rot": (24, 0, 0)}, "leg_FR": {"rot": (-20, 0, 0)}, "leg_BL": {"rot": (-20, 0, 0)}, "tail_base": {"rot": (0, 0, -8)}, "tail_tip": {"rot": (8, 0, -10)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Run",
        24,
        {
            0: {"body": {"loc": (0, 0, 0.032), "rot": (10, 0, 0)}, "leg_FL": {"rot": (36, 0, 0)}, "leg_FR": {"rot": (32, 0, 0)}, "leg_BL": {"rot": (-32, 0, 0)}, "leg_BR": {"rot": (-36, 0, 0)}, "tail_base": {"rot": (-14, 0, 0)}, "tail_tip": {"rot": (-8, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.08), "rot": (0, 0, 0)}, "leg_FL": {"rot": (10, 0, 0)}, "leg_FR": {"rot": (10, 0, 0)}, "leg_BL": {"rot": (-10, 0, 0)}, "leg_BR": {"rot": (-10, 0, 0)}, "tail_base": {"rot": (-6, 0, 0)}},
            12: {"body": {"loc": (0, 0, 0.032), "rot": (-10, 0, 0)}, "leg_FL": {"rot": (-32, 0, 0)}, "leg_FR": {"rot": (-36, 0, 0)}, "leg_BL": {"rot": (36, 0, 0)}, "leg_BR": {"rot": (32, 0, 0)}, "tail_base": {"rot": (6, 0, 0)}, "tail_tip": {"rot": (10, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.06), "rot": (0, 0, 0)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.032), "rot": (10, 0, 0)}, "leg_FL": {"rot": (36, 0, 0)}, "leg_FR": {"rot": (32, 0, 0)}, "leg_BL": {"rot": (-32, 0, 0)}, "leg_BR": {"rot": (-36, 0, 0)}, "tail_base": {"rot": (-14, 0, 0)}, "tail_tip": {"rot": (-8, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Jump",
        28,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.072), "rot": (-14, 0, 0)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (18, 0, 0)}, "leg_BR": {"rot": (18, 0, 0)}},
            14: {"body": {"loc": (0, 0, 0.22), "rot": (16, 0, 0)}, "leg_FL": {"rot": (38, 0, 0)}, "leg_FR": {"rot": (38, 0, 0)}, "leg_BL": {"rot": (-38, 0, 0)}, "leg_BR": {"rot": (-38, 0, 0)}, "tail_base": {"rot": (-18, 0, 0)}, "ear_L": {"rot": (-12, 0, 0)}, "ear_R": {"rot": (-12, 0, 0)}},
            22: {"body": {"loc": (0, 0, 0.055), "rot": (-8, 0, 0)}, "leg_FL": {"rot": (10, 0, 0)}, "leg_FR": {"rot": (10, 0, 0)}, "leg_BL": {"rot": (-10, 0, 0)}, "leg_BR": {"rot": (-10, 0, 0)}},
            28: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    # Sit: graceful upright feline sit with tail curled up, zero floor intersection
    set_pose_keys(
        arm_obj,
        "Sit",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.058), "rot": (20, 0, 0)}, "head": {"rot": (-18, 0, 0)}, "leg_FL": {"rot": (-20, 0, 0)}, "leg_FR": {"rot": (-20, 0, 0)}, "leg_BL": {"rot": (52, 0, 0)}, "leg_BR": {"rot": (52, 0, 0)}, "tail_base": {"rot": (-18, 0, -14)}, "tail_tip": {"rot": (10, 0, -18)}},
            16: {"body": {"loc": (0, 0, 0.062), "rot": (20, 0, 0)}, "head": {"rot": (-16, 0, 5)}, "leg_FL": {"rot": (-20, 0, 0)}, "leg_FR": {"rot": (-20, 0, 0)}, "leg_BL": {"rot": (52, 0, 0)}, "leg_BR": {"rot": (52, 0, 0)}, "tail_base": {"rot": (-18, 0, 14)}, "tail_tip": {"rot": (10, 0, 18)}},
            32: {"body": {"loc": (0, 0, 0.058), "rot": (20, 0, 0)}, "head": {"rot": (-18, 0, 0)}, "leg_FL": {"rot": (-20, 0, 0)}, "leg_FR": {"rot": (-20, 0, 0)}, "leg_BL": {"rot": (52, 0, 0)}, "leg_BR": {"rot": (52, 0, 0)}, "tail_base": {"rot": (-18, 0, -14)}, "tail_tip": {"rot": (10, 0, -18)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Hurt",
        18,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            5: {"body": {"loc": (0, 0.03, 0.045), "rot": (-12, 0, 10)}, "head": {"rot": (-14, 0, -12)}, "ear_L": {"rot": (-24, 0, 0)}, "ear_R": {"rot": (-24, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    # Purr: happy kneading/swaying loop with ears forward and upright curling tail
    set_pose_keys(
        arm_obj,
        "Purr",
        36,
        {
            0: {"body": {"loc": (0, 0, 0.008)}, "head": {"rot": (6, 0, 8)}, "leg_FL": {"rot": (8, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "tail_base": {"rot": (18, 0, -8)}, "tail_tip": {"rot": (16, 0, -12)}},
            18: {"body": {"loc": (0, 0, 0.012)}, "head": {"rot": (8, 0, -8)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (8, 0, 0)}, "tail_base": {"rot": (18, 0, 8)}, "tail_tip": {"rot": (16, 0, 12)}},
            36: {"body": {"loc": (0, 0, 0.008)}, "head": {"rot": (6, 0, 8)}, "leg_FL": {"rot": (8, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "tail_base": {"rot": (18, 0, -8)}, "tail_tip": {"rot": (16, 0, -12)}},
        },
        loop=True,
    )

    return arm_obj, mesh_obj


def export_glb(glb_path: Path) -> None:
    bpy.ops.export_scene.gltf(
        filepath=str(glb_path),
        export_format="GLB",
        export_yup=True,
        export_apply=False,
        export_materials="EXPORT",
        export_attributes=True,
        export_animations=True,
        export_animation_mode="NLA_TRACKS",
        export_nla_strips=True,
        export_nla_strips_merged_animation_name="",
        export_optimize_animation_size=False,
        export_skins=True,
    )


def stitch_sheet(panel_paths: list[Path], out_path: Path, cols: int = 3, rows: int = 2) -> None:
    imgs = [bpy.data.images.load(str(p)) for p in panel_paths]
    pw, ph = imgs[0].size[0], imgs[0].size[1]
    sheet = bpy.data.images.new("AnimSheet", width=pw * cols, height=ph * rows, alpha=True)
    buf = [1.0] * (pw * cols * ph * rows * 4)

    for idx, img in enumerate(imgs):
        col = idx % cols
        row = rows - 1 - (idx // cols)
        src = list(img.pixels)
        for y in range(ph):
            dst_y = row * ph + y
            src_row_start = y * pw * 4
            dst_row_start = (dst_y * (pw * cols) + col * pw) * 4
            buf[dst_row_start : dst_row_start + pw * 4] = src[src_row_start : src_row_start + pw * 4]

    sheet.pixels.foreach_set(buf)
    sheet.filepath_raw = str(out_path)
    sheet.file_format = "PNG"
    sheet.save()


def verify_glb_and_render(glb_path: Path) -> dict:
    clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(glb_path))

    arm_obj = None
    for o in bpy.data.objects:
        if o.type == "ARMATURE":
            arm_obj = o
            break
    mesh_objs = [o for o in bpy.data.objects if o.type == "MESH" and (arm_obj is None or o.parent == arm_obj)]

    total_tris = 0
    total_verts = 0
    mat_names = set()
    vcol_names = set()
    for mo in mesh_objs:
        mo.data.calc_loop_triangles()
        total_tris += len(mo.data.loop_triangles)
        total_verts += len(mo.data.vertices)
        for m in mo.data.materials:
            if m:
                mat_names.add(m.name)
        for ca in mo.data.color_attributes:
            vcol_names.add(ca.name)

    external_images = [img.filepath for img in bpy.data.images if img.filepath]
    depsgraph = bpy.context.evaluated_depsgraph_get()

    def measure_current_godot_bounds() -> dict:
        xs, ys, zs = [], [], []
        for mo in mesh_objs:
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

    clip_metrics = {}
    animations = []
    tracks_by_name = {}
    if arm_obj and arm_obj.animation_data:
        tracks = list(arm_obj.animation_data.nla_tracks)
        for t in tracks:
            t.mute = True
            tracks_by_name[t.name] = t
        for t in tracks:
            strip = t.strips[0]
            arm_obj.animation_data.action = strip.action
            if hasattr(arm_obj.animation_data, "action_slot") and strip.action.slots:
                arm_obj.animation_data.action_slot = strip.action.slots[0]
            f_start = int(strip.frame_start)
            f_end = int(strip.frame_end)
            min_y_clip = 999.0
            max_y_clip = -999.0
            max_w_clip = 0.0
            max_d_clip = 0.0
            for f in range(f_start, f_end + 1):
                bpy.context.scene.frame_set(f)
                depsgraph.update()
                b = measure_current_godot_bounds()
                min_y_clip = min(min_y_clip, b["min_y"])
                max_y_clip = max(max_y_clip, b["max_y"])
                max_w_clip = max(max_w_clip, b["width_x"])
                max_d_clip = max(max_d_clip, b["depth_z"])
            clip_metrics[t.name] = {
                "frames": [f_start, f_end],
                "lowest_y": round(min_y_clip, 4),
                "highest_y": round(max_y_clip, 4),
                "max_width_x": round(max_w_clip, 4),
                "max_depth_z": round(max_d_clip, 4),
            }
            animations.append(t.name)

        # Reset to Idle frame 1 for rest/idle preview
        if "Idle" in tracks_by_name:
            strip = tracks_by_name["Idle"].strips[0]
            arm_obj.animation_data.action = strip.action
            if hasattr(arm_obj.animation_data, "action_slot") and strip.action.slots:
                arm_obj.animation_data.action_slot = strip.action.slots[0]
            bpy.context.scene.frame_set(int(strip.frame_start))
            depsgraph.update()

    rest_bounds = measure_current_godot_bounds()

    # Setup Workbench renderer & floor plane
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.image_settings.file_format = "PNG"

    bpy.ops.mesh.primitive_plane_add(size=2.4, location=(0, 0, -0.001))
    ground = bpy.context.active_object
    ground.name = "PreviewFloor"
    gmat = bpy.data.materials.new("FloorMat")
    gmat.use_nodes = True
    ground.data.materials.append(gmat)

    h = rest_bounds["height_y"]
    cx = (rest_bounds["min_x"] + rest_bounds["max_x"]) * 0.5
    cz_blender = h * 0.45
    cy_blender = -(rest_bounds["min_z"] + rest_bounds["max_z"]) * 0.5

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    def point_camera(loc: tuple[float, float, float], target: tuple[float, float, float]) -> None:
        cam_obj.location = Vector(loc)
        direction = Vector(target) - cam_obj.location
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    dist = max(1.5, h * 2.1)
    # 3/4 view (front is +Y in Blender, mapping to -Z in Godot)
    point_camera((cx + dist * 0.72, cy_blender + dist * 0.82, cz_blender + dist * 0.38), (cx, cy_blender, cz_blender))
    scene.render.filepath = str(PREVIEW_DIR / "cat-preview.png")
    bpy.ops.render.render(write_still=True)

    # Orthographic side view for floor contact verification
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(1.3, h * 1.65)
    point_camera((cx + 3.0, cy_blender, cz_blender), (cx, cy_blender, cz_blender))
    scene.render.filepath = str(PREVIEW_DIR / "cat-side.png")
    bpy.ops.render.render(write_still=True)

    # Render 6-panel animation sheet (Idle, Walk, Run, Jump peak, Sit, Purr)
    cam_data.type = "PERSP"
    point_camera((cx + dist * 0.78, cy_blender + dist * 0.78, cz_blender + dist * 0.36), (cx, cy_blender, cz_blender))
    scene.render.resolution_x = 480
    scene.render.resolution_y = 360
    panel_specs = [
        ("Idle", 1),
        ("Walk", 1),
        ("Run", 1),
        ("Jump", 15),
        ("Sit", 1),
        ("Purr", 10),
    ]
    panel_paths = []
    for cname, frame in panel_specs:
        t = tracks_by_name[cname]
        strip = t.strips[0]
        arm_obj.animation_data.action = strip.action
        if hasattr(arm_obj.animation_data, "action_slot") and strip.action.slots:
            arm_obj.animation_data.action_slot = strip.action.slots[0]
        bpy.context.scene.frame_set(frame)
        depsgraph.update()
        p = PREVIEW_DIR / f"_tmp_{cname.lower()}.png"
        scene.render.filepath = str(p)
        bpy.ops.render.render(write_still=True)
        panel_paths.append(p)

    stitch_sheet(panel_paths, PREVIEW_DIR / "cat-animations.png", cols=3, rows=2)
    for p in panel_paths:
        p.unlink(missing_ok=True)

    sha = hashlib.sha256(glb_path.read_bytes()).hexdigest()
    return {
        "name": "cat_companion",
        "relative_source": "source/cat-companion.blend",
        "relative_export": "exports/cat-companion.glb",
        "relative_preview": "previews/cat-preview.png",
        "relative_side_preview": "previews/cat-side.png",
        "relative_animations_preview": "previews/cat-animations.png",
        "sha256_glb": sha,
        "vertices": total_verts,
        "triangles": total_tris,
        "materials": sorted(mat_names),
        "color_attributes": sorted(vcol_names),
        "external_images": external_images,
        "rest_bounds_godot": rest_bounds,
        "animations": animations,
        "clip_metrics_godot": clip_metrics,
    }


def main() -> None:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    EXPORT_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    clear_scene()
    build_cat()

    blend_path = SOURCE_DIR / "cat-companion.blend"
    glb_path = EXPORT_DIR / "cat-companion.glb"
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
    export_glb(glb_path)

    cat_info = verify_glb_and_render(glb_path)
    manifest = {
        "project": "JOSCRAFT",
        "target_engine": "Godot 4.7.2 Mono",
        "authorship": "Original procedural low-poly quadruped cat companion, rig, vertex palette, and animations created by Antigravity (no third-party or copyrighted assets).",
        "coordinate_convention": {
            "units": "meters",
            "up": "+Y",
            "forward": "-Z",
            "origin": "floor under paws (Y=0)",
        },
        "creature": cat_info,
    }

    manifest_path = BASE_DIR / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"DONE: wrote {manifest_path}")


if __name__ == "__main__":
    main()
