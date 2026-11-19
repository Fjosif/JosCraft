"""
Original low-poly / stylized blocky creature generator for JOSCRAFT (Godot 4.7.2 Mono).
Generates dog, rabbit, fox, and zombie .blend sources and .glb exports,
verifies deformed bounds & floor contact (Y=0) across all animation clips,
renders previews, and writes manifest.json.

Author: Antigravity (original procedural geometry, rig, and animations)
Blender: 5.2+
"""

import hashlib
import json
import math
import os
from pathlib import Path

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

BASE_DIR = Path(r"D:\Projekty\Hradeti\JOSCRAFT\collaboration\incoming\antigravity-creatures")
SOURCE_DIR = BASE_DIR / "source"
EXPORT_DIR = BASE_DIR / "exports"
PREVIEW_DIR = BASE_DIR / "previews"


def hex_to_srgb_rgba(hex_str: str) -> tuple[float, float, float, float]:
    h = hex_str.lstrip("#")
    r = int(h[0:2], 16) / 255.0
    g = int(h[2:4], 16) / 255.0
    b = int(h[4:6], 16) / 255.0
    return (r, g, b, 1.0)


def srgb_to_linear(c: float) -> float:
    if c <= 0.04045:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def hex_to_linear_rgba(hex_str: str) -> tuple[float, float, float, float]:
    r, g, b, a = hex_to_srgb_rgba(hex_str)
    return (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), a)


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
    vcol.layer_name = "CreaturePalette"

    nt.links.new(vcol.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


class PartBuilder:
    """Accumulates beveled blocky parts with per-face vertex colors and bone weights."""

    def __init__(self, name: str):
        self.name = name
        self.bm = bmesh.new()
        self.color_layer = self.bm.loops.layers.color.new("CreaturePalette")
        self.face_bones: list[tuple[bmesh.types.BMFace, str]] = []
        self.pending_boxes: list[dict] = []

    def add_box(
        self,
        size: tuple[float, float, float],
        center: tuple[float, float, float],
        color_hex: str,
        bone: str,
        bevel: float = 0.016,
        segments: int = 1,
        rot_deg: tuple[float, float, float] = (0.0, 0.0, 0.0),
    ) -> None:
        # bmesh loops.layers.color stores sRGB byte values directly.
        cx, cy, cz = center
        rx, ry, rz = rot_deg
        self.pending_boxes.append(
            {
                "size": size,
                "center": (-cx, -cy, cz),
                "color": hex_to_srgb_rgba(color_hex),
                "bone": bone,
                "bevel": bevel,
                "segments": segments,
                "rot_deg": (-rx, -ry, rz),
            }
        )

    def build_mesh(self, material: bpy.types.Material) -> bpy.types.Object:
        # Build each box in its own bmesh so bevel never merges unintended parts,
        # then merge into the final bmesh with exact vertex group tagging.
        vert_bone_map: list[tuple[int, str]] = []

        for spec in self.pending_boxes:
            sub = bmesh.new()
            sx, sy, sz = spec["size"]
            bmesh.ops.create_cube(sub, size=1.0)
            for v in sub.verts:
                v.co.x *= sx
                v.co.y *= sy
                v.co.z *= sz

            bev = min(spec["bevel"], min(sx, sy, sz) * 0.28)
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

            # Copy into main bmesh
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

        # Ensure BYTE_COLOR or FLOAT_COLOR attribute is active
        if "CreaturePalette" in mesh.color_attributes:
            mesh.color_attributes.active_color = mesh.color_attributes["CreaturePalette"]

        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.collection.objects.link(obj)
        obj.data.materials.append(material)

        # Assign vertex groups
        vg_cache: dict[str, bpy.types.VertexGroup] = {}
        # Use deduplicated dict of vert_idx -> bone
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
    """
    keyframes: {frame: {bone_name: {"rot": (rx_deg, ry_deg, rz_deg), "loc": (lx, ly, lz)}}}
    """
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

    # Reset all pose bones first
    for pb in arm_obj.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0.0, 0.0, 0.0)
        pb.location = (0.0, 0.0, 0.0)

    for frame in sorted(keyframes.keys()):
        bpy.context.scene.frame_set(frame)
        # Reset before applying this frame's pose so omitted bones stay neutral if keyed
        for pb in arm_obj.pose.bones:
            pb.rotation_euler = (0.0, 0.0, 0.0)
            pb.location = (0.0, 0.0, 0.0)

        frame_data = keyframes[frame]
        for bone_name, transforms in frame_data.items():
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

    # Push to NLA track for clean glTF multi-clip export
    track = arm_obj.animation_data.nla_tracks.new()
    track.name = action_name
    strip = track.strips.new(action_name, start=0, action=action)
    strip.name = action_name
    arm_obj.animation_data.action = None
    return action


# ---------------------------------------------------------------------------
# Creature Definitions
# Note: In Blender, -Y is forward (maps to Godot -Z), +Z is up (maps to Godot +Y).
# Feet bottoms are placed strictly at Z = 0.0.
# ---------------------------------------------------------------------------

def build_dog():
    # Target: height ~1.02m, footprint <= 0.6 x 1.1m
    # Colors: grey-brown coat, cream muzzle/belly/paws, dark ears/nose/eyes
    coat = "#9B8F7E"
    coat_dark = "#6E6152"
    cream = "#E6DEC8"
    nose = "#24272D"
    inner_ear = "#D2A094"

    bones = [
        {"name": "root", "head": (0, 0, 0), "tail": (0, 0, 0.2), "parent": None},
        {"name": "body", "head": (0, 0.04, 0.44), "tail": (0, -0.25, 0.44), "parent": "root"},
        {"name": "head", "head": (0, -0.26, 0.56), "tail": (0, -0.34, 0.86), "parent": "body"},
        {"name": "ear_L", "head": (-0.16, -0.28, 0.84), "tail": (-0.22, -0.28, 0.66), "parent": "head"},
        {"name": "ear_R", "head": (0.16, -0.28, 0.84), "tail": (0.22, -0.28, 0.66), "parent": "head"},
        {"name": "tail", "head": (0, 0.30, 0.52), "tail": (0, 0.50, 0.72), "parent": "body"},
        {"name": "leg_FL", "head": (-0.14, -0.18, 0.34), "tail": (-0.14, -0.18, 0.0), "parent": "body"},
        {"name": "leg_FR", "head": (0.14, -0.18, 0.34), "tail": (0.14, -0.18, 0.0), "parent": "body"},
        {"name": "leg_BL", "head": (-0.14, 0.20, 0.34), "tail": (-0.14, 0.20, 0.0), "parent": "body"},
        {"name": "leg_BR", "head": (0.14, 0.20, 0.34), "tail": (0.14, 0.20, 0.0), "parent": "body"},
    ]

    pb = PartBuilder("dog")
    # Torso & chest
    pb.add_box((0.40, 0.62, 0.34), (0.0, 0.02, 0.46), coat, "body", bevel=0.022)
    pb.add_box((0.30, 0.48, 0.08), (0.0, 0.01, 0.32), cream, "body", bevel=0.015)
    pb.add_box((0.34, 0.14, 0.28), (0.0, -0.24, 0.48), cream, "body", bevel=0.018)

    # Head & expressive snout
    pb.add_box((0.36, 0.34, 0.34), (0.0, -0.32, 0.72), coat, "head", bevel=0.022)
    pb.add_box((0.22, 0.22, 0.16), (0.0, -0.54, 0.64), cream, "head", bevel=0.016)
    pb.add_box((0.10, 0.07, 0.07), (0.0, -0.64, 0.69), nose, "head", bevel=0.01)
    # Eyes
    pb.add_box((0.055, 0.03, 0.065), (-0.11, -0.49, 0.77), nose, "head", bevel=0.005)
    pb.add_box((0.055, 0.03, 0.065), (0.11, -0.49, 0.77), nose, "head", bevel=0.005)
    # Cute brow spots
    pb.add_box((0.065, 0.03, 0.035), (-0.11, -0.485, 0.82), cream, "head", bevel=0.005)
    pb.add_box((0.065, 0.03, 0.035), (0.11, -0.485, 0.82), cream, "head", bevel=0.005)

    # Floppy/semi-perky ears (top at ~1.01m)
    pb.add_box((0.08, 0.14, 0.22), (-0.18, -0.28, 0.89), coat_dark, "ear_L", bevel=0.012, rot_deg=(0, 12, 0))
    pb.add_box((0.08, 0.14, 0.22), (0.18, -0.28, 0.89), coat_dark, "ear_R", bevel=0.012, rot_deg=(0, -12, 0))
    pb.add_box((0.04, 0.08, 0.13), (-0.16, -0.31, 0.88), inner_ear, "ear_L", bevel=0.006, rot_deg=(0, 12, 0))
    pb.add_box((0.04, 0.08, 0.13), (0.16, -0.31, 0.88), inner_ear, "ear_R", bevel=0.006, rot_deg=(0, -12, 0))

    # Tail (curled up cheerfully, compact enough to stay within 1.1m footprint)
    pb.add_box((0.11, 0.20, 0.11), (0.0, 0.28, 0.61), coat_dark, "tail", bevel=0.015, rot_deg=(45, 0, 0))

    # 4 legs with paws touching Z = 0.0
    for bname, lx, ly in [
        ("leg_FL", -0.14, -0.18),
        ("leg_FR", 0.14, -0.18),
        ("leg_BL", -0.14, 0.20),
        ("leg_BR", 0.14, 0.20),
    ]:
        pb.add_box((0.12, 0.13, 0.24), (lx, ly, 0.20), coat, bname, bevel=0.014)
        pb.add_box((0.13, 0.15, 0.08), (lx, ly - 0.01, 0.04), cream, bname, bevel=0.012)

    arm_obj = build_armature("dog", bones)
    mat = create_palette_material("dog_Palette")
    mesh_obj = pb.build_mesh(mat)
    attach_mesh_to_armature(mesh_obj, arm_obj)

    # Animations (exact names: Idle, Walk, Run, Jump, Sit, Hurt)
    set_pose_keys(
        arm_obj,
        "Idle",
        48,
        {
            0: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, -14)}, "ear_L": {"rot": (0, 0, 0)}, "ear_R": {"rot": (0, 0, 0)}},
            12: {"body": {"loc": (0, 0, 0.012)}, "head": {"rot": (3, 0, 4)}, "tail": {"rot": (4, 0, 14)}, "ear_L": {"rot": (4, 0, 0)}, "ear_R": {"rot": (-2, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, -14)}, "ear_L": {"rot": (0, 0, 0)}, "ear_R": {"rot": (0, 0, 0)}},
            36: {"body": {"loc": (0, 0, 0.012)}, "head": {"rot": (-2, 0, -4)}, "tail": {"rot": (4, 0, 14)}, "ear_L": {"rot": (-2, 0, 0)}, "ear_R": {"rot": (4, 0, 0)}},
            48: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, -14)}, "ear_L": {"rot": (0, 0, 0)}, "ear_R": {"rot": (0, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Walk",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.015)}, "leg_FL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (-18, 0, 0)}, "tail": {"rot": (0, 0, -10)}},
            8: {"body": {"loc": (0, 0, 0.025)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "tail": {"rot": (5, 0, 0)}},
            16: {"body": {"loc": (0, 0, 0.015)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_BR": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (22, 0, 0)}, "leg_BL": {"rot": (22, 0, 0)}, "tail": {"rot": (0, 0, 10)}},
            24: {"body": {"loc": (0, 0, 0.025)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "tail": {"rot": (5, 0, 0)}},
            32: {"body": {"loc": (0, 0, 0.015)}, "leg_FL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (-18, 0, 0)}, "tail": {"rot": (0, 0, -10)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Run",
        24,
        {
            0: {"body": {"loc": (0, 0, 0.03), "rot": (8, 0, 0)}, "leg_FL": {"rot": (34, 0, 0)}, "leg_FR": {"rot": (30, 0, 0)}, "leg_BL": {"rot": (-30, 0, 0)}, "leg_BR": {"rot": (-34, 0, 0)}, "head": {"rot": (-6, 0, 0)}, "tail": {"rot": (-10, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.09), "rot": (0, 0, 0)}, "leg_FL": {"rot": (12, 0, 0)}, "leg_FR": {"rot": (12, 0, 0)}, "leg_BL": {"rot": (-12, 0, 0)}, "leg_BR": {"rot": (-12, 0, 0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (8, 0, 0)}},
            12: {"body": {"loc": (0, 0, 0.03), "rot": (-8, 0, 0)}, "leg_FL": {"rot": (-30, 0, 0)}, "leg_FR": {"rot": (-34, 0, 0)}, "leg_BL": {"rot": (34, 0, 0)}, "leg_BR": {"rot": (30, 0, 0)}, "head": {"rot": (6, 0, 0)}, "tail": {"rot": (12, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.07), "rot": (0, 0, 0)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.03), "rot": (8, 0, 0)}, "leg_FL": {"rot": (34, 0, 0)}, "leg_FR": {"rot": (30, 0, 0)}, "leg_BL": {"rot": (-30, 0, 0)}, "leg_BR": {"rot": (-34, 0, 0)}, "head": {"rot": (-6, 0, 0)}, "tail": {"rot": (-10, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Jump",
        28,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.065), "rot": (-12, 0, 0)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (18, 0, 0)}, "leg_BR": {"rot": (18, 0, 0)}},
            14: {"body": {"loc": (0, 0, 0.22), "rot": (14, 0, 0)}, "leg_FL": {"rot": (35, 0, 0)}, "leg_FR": {"rot": (35, 0, 0)}, "leg_BL": {"rot": (-35, 0, 0)}, "leg_BR": {"rot": (-35, 0, 0)}, "ear_L": {"rot": (-15, 0, 0)}, "ear_R": {"rot": (-15, 0, 0)}},
            22: {"body": {"loc": (0, 0, 0.055), "rot": (-8, 0, 0)}, "leg_FL": {"rot": (10, 0, 0)}, "leg_FR": {"rot": (10, 0, 0)}, "leg_BL": {"rot": (-10, 0, 0)}, "leg_BR": {"rot": (-10, 0, 0)}},
            28: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    # Sit: body tilts back (+18 deg X), raised so haunches & paws never intersect Y=0
    set_pose_keys(
        arm_obj,
        "Sit",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.068), "rot": (18, 0, 0)}, "head": {"rot": (-16, 0, 0)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (52, 0, 0)}, "leg_BR": {"rot": (52, 0, 0)}, "tail": {"rot": (-15, 0, -14)}},
            16: {"body": {"loc": (0, 0, 0.072), "rot": (18, 0, 0)}, "head": {"rot": (-14, 0, 4)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (52, 0, 0)}, "leg_BR": {"rot": (52, 0, 0)}, "tail": {"rot": (-15, 0, 14)}},
            32: {"body": {"loc": (0, 0, 0.068), "rot": (18, 0, 0)}, "head": {"rot": (-16, 0, 0)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (52, 0, 0)}, "leg_BR": {"rot": (52, 0, 0)}, "tail": {"rot": (-15, 0, -14)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Hurt",
        18,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            5: {"body": {"loc": (0, 0.04, 0.04), "rot": (-10, 0, 8)}, "head": {"rot": (-14, 0, -12)}, "ear_L": {"rot": (-20, 0, 0)}, "ear_R": {"rot": (-20, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    return arm_obj, mesh_obj


def build_rabbit():
    # Target: total height incl ears <= 0.95m, footprint <= 0.6 x 0.9m
    # Beige fur, long ears, strong hind legs, fluffy cream tail
    fur = "#C6B398"
    fur_dark = "#A49176"
    cream = "#EFE8D8"
    pink = "#E3A7A6"
    eye = "#20242A"

    bones = [
        {"name": "root", "head": (0, 0, 0), "tail": (0, 0, 0.2), "parent": None},
        {"name": "body", "head": (0, 0.05, 0.28), "tail": (0, -0.18, 0.34), "parent": "root"},
        {"name": "head", "head": (0, -0.20, 0.42), "tail": (0, -0.25, 0.64), "parent": "body"},
        {"name": "ear_L", "head": (-0.09, -0.22, 0.64), "tail": (-0.11, -0.20, 0.92), "parent": "head"},
        {"name": "ear_R", "head": (0.09, -0.22, 0.64), "tail": (0.11, -0.20, 0.92), "parent": "head"},
        {"name": "tail", "head": (0, 0.26, 0.26), "tail": (0, 0.38, 0.32), "parent": "body"},
        {"name": "leg_FL", "head": (-0.11, -0.15, 0.22), "tail": (-0.11, -0.15, 0.0), "parent": "body"},
        {"name": "leg_FR", "head": (0.11, -0.15, 0.22), "tail": (0.11, -0.15, 0.0), "parent": "body"},
        {"name": "leg_BL", "head": (-0.14, 0.14, 0.24), "tail": (-0.14, 0.14, 0.0), "parent": "body"},
        {"name": "leg_BR", "head": (0.14, 0.14, 0.24), "tail": (0.14, 0.14, 0.0), "parent": "body"},
    ]

    pb = PartBuilder("rabbit")
    # Compact rounded body
    pb.add_box((0.34, 0.46, 0.30), (0.0, 0.02, 0.31), fur, "body", bevel=0.024)
    pb.add_box((0.24, 0.34, 0.08), (0.0, 0.00, 0.19), cream, "body", bevel=0.015)

    # Head & cheeks
    pb.add_box((0.30, 0.28, 0.28), (0.0, -0.23, 0.50), fur, "head", bevel=0.022)
    pb.add_box((0.20, 0.12, 0.12), (0.0, -0.37, 0.45), cream, "head", bevel=0.014)
    pb.add_box((0.06, 0.04, 0.045), (0.0, -0.43, 0.48), pink, "head", bevel=0.006)
    pb.add_box((0.05, 0.03, 0.055), (-0.09, -0.37, 0.53), eye, "head", bevel=0.005)
    pb.add_box((0.05, 0.03, 0.055), (0.09, -0.37, 0.53), eye, "head", bevel=0.005)

    # Long rabbit ears (max Z ~ 0.92m <= 0.95m)
    pb.add_box((0.085, 0.075, 0.30), (-0.095, -0.20, 0.77), fur, "ear_L", bevel=0.012, rot_deg=(-6, 5, 0))
    pb.add_box((0.085, 0.075, 0.30), (0.095, -0.20, 0.77), fur, "ear_R", bevel=0.012, rot_deg=(-6, -5, 0))
    pb.add_box((0.045, 0.02, 0.21), (-0.095, -0.235, 0.76), pink, "ear_L", bevel=0.005, rot_deg=(-6, 5, 0))
    pb.add_box((0.045, 0.02, 0.21), (0.095, -0.235, 0.76), pink, "ear_R", bevel=0.005, rot_deg=(-6, -5, 0))

    # Fluffy tail
    pb.add_box((0.14, 0.14, 0.14), (0.0, 0.29, 0.27), cream, "tail", bevel=0.02)

    # Front paws (smaller, touching Z=0)
    for bname, lx in [("leg_FL", -0.11), ("leg_FR", 0.11)]:
        pb.add_box((0.09, 0.10, 0.18), (lx, -0.15, 0.09), fur, bname, bevel=0.012)
        pb.add_box((0.10, 0.12, 0.05), (lx, -0.16, 0.025), cream, bname, bevel=0.008)

    # Strong hind legs (haunch + long foot touching Z=0)
    for bname, lx in [("leg_BL", -0.15), ("leg_BR", 0.15)]:
        pb.add_box((0.12, 0.20, 0.20), (lx, 0.14, 0.14), fur_dark, bname, bevel=0.016)
        pb.add_box((0.11, 0.24, 0.06), (lx, 0.09, 0.03), cream, bname, bevel=0.01)

    arm_obj = build_armature("rabbit", bones)
    mat = create_palette_material("rabbit_Palette")
    mesh_obj = pb.build_mesh(mat)
    attach_mesh_to_armature(mesh_obj, arm_obj)

    set_pose_keys(
        arm_obj,
        "Idle",
        48,
        {
            0: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "ear_L": {"rot": (0, 0, 0)}, "ear_R": {"rot": (0, 0, 0)}},
            12: {"body": {"loc": (0, 0, 0.01)}, "head": {"rot": (4, 0, 3)}, "ear_L": {"rot": (8, 0, 0)}, "ear_R": {"rot": (-4, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "ear_L": {"rot": (0, 0, 0)}, "ear_R": {"rot": (0, 0, 0)}},
            36: {"body": {"loc": (0, 0, 0.01)}, "head": {"rot": (-3, 0, -3)}, "ear_L": {"rot": (-4, 0, 0)}, "ear_R": {"rot": (8, 0, 0)}},
            48: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "ear_L": {"rot": (0, 0, 0)}, "ear_R": {"rot": (0, 0, 0)}},
        },
        loop=True,
    )

    # Hop-walk
    set_pose_keys(
        arm_obj,
        "Walk",
        24,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.09), "rot": (12, 0, 0)}, "leg_FL": {"rot": (28, 0, 0)}, "leg_FR": {"rot": (28, 0, 0)}, "leg_BL": {"rot": (-28, 0, 0)}, "leg_BR": {"rot": (-28, 0, 0)}, "ear_L": {"rot": (-12, 0, 0)}, "ear_R": {"rot": (-12, 0, 0)}},
            14: {"body": {"loc": (0, 0, 0.06), "rot": (-6, 0, 0)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (18, 0, 0)}, "leg_BR": {"rot": (18, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Run",
        18,
        {
            0: {"body": {"loc": (0, 0, 0.055), "rot": (-8, 0, 0)}, "leg_FL": {"rot": (-22, 0, 0)}, "leg_FR": {"rot": (-22, 0, 0)}, "leg_BL": {"rot": (24, 0, 0)}, "leg_BR": {"rot": (24, 0, 0)}},
            8: {"body": {"loc": (0, 0, 0.14), "rot": (16, 0, 0)}, "leg_FL": {"rot": (38, 0, 0)}, "leg_FR": {"rot": (38, 0, 0)}, "leg_BL": {"rot": (-38, 0, 0)}, "leg_BR": {"rot": (-38, 0, 0)}, "ear_L": {"rot": (-20, 0, 0)}, "ear_R": {"rot": (-20, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.055), "rot": (-8, 0, 0)}, "leg_FL": {"rot": (-22, 0, 0)}, "leg_FR": {"rot": (-22, 0, 0)}, "leg_BL": {"rot": (24, 0, 0)}, "leg_BR": {"rot": (24, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Jump",
        24,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            5: {"body": {"loc": (0, 0, 0.03), "rot": (-10, 0, 0)}},
            12: {"body": {"loc": (0, 0, 0.24), "rot": (16, 0, 0)}, "leg_FL": {"rot": (40, 0, 0)}, "leg_FR": {"rot": (40, 0, 0)}, "leg_BL": {"rot": (-40, 0, 0)}, "leg_BR": {"rot": (-40, 0, 0)}, "ear_L": {"rot": (-22, 0, 0)}, "ear_R": {"rot": (-22, 0, 0)}},
            20: {"body": {"loc": (0, 0, 0.03), "rot": (-6, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    set_pose_keys(
        arm_obj,
        "Sit",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.062), "rot": (16, 0, 0)}, "head": {"rot": (-14, 0, 0)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "ear_L": {"rot": (5, 0, 0)}, "ear_R": {"rot": (-3, 0, 0)}},
            16: {"body": {"loc": (0, 0, 0.066), "rot": (16, 0, 0)}, "head": {"rot": (-12, 0, 5)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "ear_L": {"rot": (-3, 0, 0)}, "ear_R": {"rot": (5, 0, 0)}},
            32: {"body": {"loc": (0, 0, 0.062), "rot": (16, 0, 0)}, "head": {"rot": (-14, 0, 0)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "ear_L": {"rot": (5, 0, 0)}, "ear_R": {"rot": (-3, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Hurt",
        18,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            5: {"body": {"loc": (0, 0.03, 0.04), "rot": (-10, 0, 10)}, "head": {"rot": (-12, 0, -10)}, "ear_L": {"rot": (-25, 0, 0)}, "ear_R": {"rot": (-25, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    return arm_obj, mesh_obj


def build_fox():
    # Target: height <= 1.05m, footprint <= 0.6 x 1.4m
    # Orange fur, dark paws, cream muzzle, bushy tail with white tip
    orange = "#DF7830"
    orange_dark = "#B95A1F"
    cream = "#F3EAD8"
    white = "#F8F5EE"
    dark = "#2D2726"
    eye = "#1B2026"

    bones = [
        {"name": "root", "head": (0, 0, 0), "tail": (0, 0, 0.2), "parent": None},
        {"name": "body", "head": (0, 0.04, 0.42), "tail": (0, -0.25, 0.42), "parent": "root"},
        {"name": "head", "head": (0, -0.27, 0.54), "tail": (0, -0.35, 0.80), "parent": "body"},
        {"name": "ear_L", "head": (-0.13, -0.28, 0.79), "tail": (-0.14, -0.28, 0.99), "parent": "head"},
        {"name": "ear_R", "head": (0.13, -0.28, 0.79), "tail": (0.14, -0.28, 0.99), "parent": "head"},
        {"name": "tail", "head": (0, 0.33, 0.46), "tail": (0, 0.78, 0.52), "parent": "body"},
        {"name": "leg_FL", "head": (-0.13, -0.18, 0.32), "tail": (-0.13, -0.18, 0.0), "parent": "body"},
        {"name": "leg_FR", "head": (0.13, -0.18, 0.32), "tail": (0.13, -0.18, 0.0), "parent": "body"},
        {"name": "leg_BL", "head": (-0.13, 0.20, 0.32), "tail": (-0.13, 0.20, 0.0), "parent": "body"},
        {"name": "leg_BR", "head": (0.13, 0.20, 0.32), "tail": (0.13, 0.20, 0.0), "parent": "body"},
    ]

    pb = PartBuilder("fox")
    # Sleek body + cream chest/belly
    pb.add_box((0.36, 0.64, 0.30), (0.0, 0.02, 0.44), orange, "body", bevel=0.02)
    pb.add_box((0.26, 0.46, 0.07), (0.0, 0.01, 0.31), cream, "body", bevel=0.014)
    pb.add_box((0.30, 0.14, 0.24), (0.0, -0.25, 0.46), cream, "body", bevel=0.016)

    # Head + pointed cream snout + dark nose
    pb.add_box((0.36, 0.32, 0.30), (0.0, -0.32, 0.66), orange, "head", bevel=0.02)
    pb.add_box((0.38, 0.16, 0.12), (0.0, -0.34, 0.57), cream, "head", bevel=0.014)
    pb.add_box((0.18, 0.22, 0.12), (0.0, -0.53, 0.59), cream, "head", bevel=0.014)
    pb.add_box((0.08, 0.06, 0.055), (0.0, -0.63, 0.62), dark, "head", bevel=0.008)
    pb.add_box((0.05, 0.03, 0.055), (-0.11, -0.48, 0.69), eye, "head", bevel=0.005)
    pb.add_box((0.05, 0.03, 0.055), (0.11, -0.48, 0.69), eye, "head", bevel=0.005)

    # Pointed fox ears with dark tips (max Z ~ 1.00m <= 1.05m)
    pb.add_box((0.10, 0.08, 0.14), (-0.13, -0.28, 0.87), orange, "ear_L", bevel=0.01)
    pb.add_box((0.08, 0.06, 0.08), (-0.13, -0.28, 0.96), dark, "ear_L", bevel=0.008)
    pb.add_box((0.10, 0.08, 0.14), (0.13, -0.28, 0.87), orange, "ear_R", bevel=0.01)
    pb.add_box((0.08, 0.06, 0.08), (0.13, -0.28, 0.96), dark, "ear_R", bevel=0.008)

    # Bushy tail with white tip (total Y from -0.66 to +0.86 -> ~1.52? Wait: keep tail <= +0.68 so depth <= 1.35m <= 1.4m!)
    pb.add_box((0.18, 0.34, 0.18), (0.0, 0.45, 0.48), orange, "tail", bevel=0.02, rot_deg=(10, 0, 0))
    pb.add_box((0.17, 0.18, 0.17), (0.0, 0.60, 0.52), white, "tail", bevel=0.02, rot_deg=(10, 0, 0))

    # 4 legs with dark paws touching Z = 0.0
    for bname, lx, ly in [
        ("leg_FL", -0.13, -0.18),
        ("leg_FR", 0.13, -0.18),
        ("leg_BL", -0.13, 0.20),
        ("leg_BR", 0.13, 0.20),
    ]:
        pb.add_box((0.11, 0.12, 0.20), (lx, ly, 0.20), orange_dark, bname, bevel=0.012)
        pb.add_box((0.115, 0.13, 0.11), (lx, ly - 0.005, 0.055), dark, bname, bevel=0.01)

    arm_obj = build_armature("fox", bones)
    mat = create_palette_material("fox_Palette")
    mesh_obj = pb.build_mesh(mat)
    attach_mesh_to_armature(mesh_obj, arm_obj)

    set_pose_keys(
        arm_obj,
        "Idle",
        48,
        {
            0: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, -10)}},
            12: {"body": {"loc": (0, 0, 0.01)}, "head": {"rot": (3, 0, 5)}, "tail": {"rot": (5, 0, 10)}},
            24: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, -10)}},
            36: {"body": {"loc": (0, 0, 0.01)}, "head": {"rot": (-2, 0, -5)}, "tail": {"rot": (5, 0, 10)}},
            48: {"body": {"loc": (0, 0, 0.0)}, "head": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, -10)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Walk",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.0)}, "leg_FL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (-18, 0, 0)}, "tail": {"rot": (0, 0, -8)}},
            8: {"body": {"loc": (0, 0, 0.02)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "tail": {"rot": (3, 0, 0)}},
            16: {"body": {"loc": (0, 0, 0.0)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_BR": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (22, 0, 0)}, "leg_BL": {"rot": (22, 0, 0)}, "tail": {"rot": (0, 0, 8)}},
            24: {"body": {"loc": (0, 0, 0.02)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "tail": {"rot": (3, 0, 0)}},
            32: {"body": {"loc": (0, 0, 0.0)}, "leg_FL": {"rot": (22, 0, 0)}, "leg_BR": {"rot": (22, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (-18, 0, 0)}, "tail": {"rot": (0, 0, -8)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Run",
        24,
        {
            0: {"body": {"loc": (0, 0, 0.03), "rot": (8, 0, 0)}, "leg_FL": {"rot": (35, 0, 0)}, "leg_FR": {"rot": (30, 0, 0)}, "leg_BL": {"rot": (-30, 0, 0)}, "leg_BR": {"rot": (-35, 0, 0)}, "tail": {"rot": (-6, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.09), "rot": (0, 0, 0)}, "leg_FL": {"rot": (12, 0, 0)}, "leg_FR": {"rot": (12, 0, 0)}, "leg_BL": {"rot": (-12, 0, 0)}, "leg_BR": {"rot": (-12, 0, 0)}, "tail": {"rot": (4, 0, 0)}},
            12: {"body": {"loc": (0, 0, 0.03), "rot": (-8, 0, 0)}, "leg_FL": {"rot": (-30, 0, 0)}, "leg_FR": {"rot": (-35, 0, 0)}, "leg_BL": {"rot": (35, 0, 0)}, "leg_BR": {"rot": (30, 0, 0)}, "tail": {"rot": (8, 0, 0)}},
            18: {"body": {"loc": (0, 0, 0.07), "rot": (0, 0, 0)}, "leg_FL": {"rot": (0, 0, 0)}, "leg_FR": {"rot": (0, 0, 0)}, "leg_BL": {"rot": (0, 0, 0)}, "leg_BR": {"rot": (0, 0, 0)}, "tail": {"rot": (0, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.03), "rot": (8, 0, 0)}, "leg_FL": {"rot": (35, 0, 0)}, "leg_FR": {"rot": (30, 0, 0)}, "leg_BL": {"rot": (-30, 0, 0)}, "leg_BR": {"rot": (-35, 0, 0)}, "tail": {"rot": (-6, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Jump",
        28,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            6: {"body": {"loc": (0, 0, 0.065), "rot": (-12, 0, 0)}, "leg_FL": {"rot": (-16, 0, 0)}, "leg_FR": {"rot": (-16, 0, 0)}, "leg_BL": {"rot": (18, 0, 0)}, "leg_BR": {"rot": (18, 0, 0)}},
            14: {"body": {"loc": (0, 0, 0.24), "rot": (14, 0, 0)}, "leg_FL": {"rot": (35, 0, 0)}, "leg_FR": {"rot": (35, 0, 0)}, "leg_BL": {"rot": (-35, 0, 0)}, "leg_BR": {"rot": (-35, 0, 0)}},
            22: {"body": {"loc": (0, 0, 0.055), "rot": (-8, 0, 0)}},
            28: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    set_pose_keys(
        arm_obj,
        "Sit",
        32,
        {
            0: {"body": {"loc": (0, 0, 0.068), "rot": (18, 0, 0)}, "head": {"rot": (-16, 0, 0)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (50, 0, 0)}, "leg_BR": {"rot": (50, 0, 0)}, "tail": {"rot": (-10, 0, -14)}},
            16: {"body": {"loc": (0, 0, 0.072), "rot": (18, 0, 0)}, "head": {"rot": (-14, 0, 4)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (50, 0, 0)}, "leg_BR": {"rot": (50, 0, 0)}, "tail": {"rot": (-10, 0, 14)}},
            32: {"body": {"loc": (0, 0, 0.068), "rot": (18, 0, 0)}, "head": {"rot": (-16, 0, 0)}, "leg_FL": {"rot": (-18, 0, 0)}, "leg_FR": {"rot": (-18, 0, 0)}, "leg_BL": {"rot": (50, 0, 0)}, "leg_BR": {"rot": (50, 0, 0)}, "tail": {"rot": (-10, 0, -14)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Hurt",
        18,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
            5: {"body": {"loc": (0, 0.04, 0.04), "rot": (-10, 0, 8)}, "head": {"rot": (-14, 0, -12)}},
            18: {"body": {"loc": (0, 0, 0.0), "rot": (0, 0, 0)}},
        },
        loop=False,
    )

    return arm_obj, mesh_obj


def build_zombie():
    # Target: height ~2.1m, footprint <= 0.7 x 0.8m
    # Non-bloody, friendly stylized blocky zombie: green skin, blue-grey clothes
    skin = "#8DB565"
    skin_dark = "#6D904B"
    shirt = "#5C839C"
    shirt_trim = "#739BB3"
    pants = "#4B5075"
    shoes = "#303547"
    eye = "#16242C"

    bones = [
        {"name": "root", "head": (0, 0, 0), "tail": (0, 0, 0.25), "parent": None},
        {"name": "hips", "head": (0, 0, 0.78), "tail": (0, -0.2, 0.78), "parent": "root"},
        {"name": "body", "head": (0, 0, 0.82), "tail": (0, -0.2, 0.82), "parent": "hips"},
        {"name": "head", "head": (0, 0, 1.54), "tail": (0, -0.2, 1.54), "parent": "body"},
        {"name": "arm_L", "head": (-0.36, 0.0, 1.40), "tail": (-0.36, -0.52, 1.36), "parent": "body"},
        {"name": "arm_R", "head": (0.36, 0.0, 1.40), "tail": (0.36, -0.52, 1.36), "parent": "body"},
        {"name": "leg_L", "head": (-0.15, 0.0, 0.78), "tail": (-0.15, -0.2, 0.78), "parent": "hips"},
        {"name": "leg_R", "head": (0.15, 0.0, 0.78), "tail": (0.15, -0.2, 0.78), "parent": "hips"},
    ]

    pb = PartBuilder("zombie")
    # Torso (blue-grey tunic + collar accent)
    pb.add_box((0.50, 0.30, 0.72), (0.0, 0.0, 1.16), shirt, "body", bevel=0.02)
    pb.add_box((0.36, 0.31, 0.09), (0.0, -0.01, 1.47), shirt_trim, "body", bevel=0.01)

    # Head (top at 2.08m ~ 2.1m)
    pb.add_box((0.46, 0.44, 0.50), (0.0, -0.01, 1.81), skin, "head", bevel=0.025)
    # Hair / brow ridge (friendly stylized)
    pb.add_box((0.48, 0.46, 0.10), (0.0, 0.0, 2.03), skin_dark, "head", bevel=0.015)
    pb.add_box((0.12, 0.04, 0.04), (-0.11, -0.23, 1.88), skin_dark, "head", bevel=0.006, rot_deg=(0, 8, 0))
    pb.add_box((0.12, 0.04, 0.04), (0.11, -0.23, 1.88), skin_dark, "head", bevel=0.006, rot_deg=(0, -8, 0))
    # Eyes & subtle mouth
    pb.add_box((0.08, 0.03, 0.075), (-0.11, -0.23, 1.81), eye, "head", bevel=0.005)
    pb.add_box((0.08, 0.03, 0.075), (0.11, -0.23, 1.81), eye, "head", bevel=0.005)
    pb.add_box((0.14, 0.03, 0.04), (0.0, -0.23, 1.67), skin_dark, "head", bevel=0.005)

    # Outstretched arms (forward, kept within 0.7m width x 0.8m depth)
    for bname, ax in [("arm_L", -0.28), ("arm_R", 0.28)]:
        pb.add_box((0.10, 0.22, 0.16), (ax, -0.08, 1.39), shirt, bname, bevel=0.014)
        pb.add_box((0.095, 0.34, 0.15), (ax, -0.34, 1.37), skin, bname, bevel=0.014)

    # Legs (touching Z = 0.0)
    for bname, lx in [("leg_L", -0.14), ("leg_R", 0.14)]:
        pb.add_box((0.20, 0.24, 0.66), (lx, 0.0, 0.45), pants, bname, bevel=0.016)
        pb.add_box((0.21, 0.28, 0.12), (lx, -0.02, 0.06), shoes, bname, bevel=0.012)

    arm_obj = build_armature("zombie", bones)
    mat = create_palette_material("zombie_Palette")
    mesh_obj = pb.build_mesh(mat)
    attach_mesh_to_armature(mesh_obj, arm_obj)

    set_pose_keys(
        arm_obj,
        "Idle",
        48,
        {
            0: {"body": {"loc": (0, 0, 0.0), "rot": (2, 0, 0)}, "head": {"rot": (2, 0, -4)}, "arm_L": {"rot": (4, 0, 0)}, "arm_R": {"rot": (-4, 0, 0)}},
            24: {"body": {"loc": (0, 0, 0.015), "rot": (4, 0, 0)}, "head": {"rot": (-3, 0, 4)}, "arm_L": {"rot": (-4, 0, 0)}, "arm_R": {"rot": (4, 0, 0)}},
            48: {"body": {"loc": (0, 0, 0.0), "rot": (2, 0, 0)}, "head": {"rot": (2, 0, -4)}, "arm_L": {"rot": (4, 0, 0)}, "arm_R": {"rot": (-4, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Walk",
        32,
        {
            0: {"hips": {"loc": (0, 0, 0.018)}, "body": {"rot": (4, 4, 0)}, "leg_L": {"rot": (20, 0, 0)}, "leg_R": {"rot": (-18, 0, 0)}, "arm_L": {"rot": (-8, 0, 0)}, "arm_R": {"rot": (8, 0, 0)}},
            8: {"hips": {"loc": (0, 0, 0.025)}, "body": {"rot": (4, 0, 0)}, "leg_L": {"rot": (0, 0, 0)}, "leg_R": {"rot": (0, 0, 0)}, "arm_L": {"rot": (0, 0, 0)}, "arm_R": {"rot": (0, 0, 0)}},
            16: {"hips": {"loc": (0, 0, 0.018)}, "body": {"rot": (4, -4, 0)}, "leg_L": {"rot": (-18, 0, 0)}, "leg_R": {"rot": (20, 0, 0)}, "arm_L": {"rot": (8, 0, 0)}, "arm_R": {"rot": (-8, 0, 0)}},
            24: {"hips": {"loc": (0, 0, 0.025)}, "body": {"rot": (4, 0, 0)}, "leg_L": {"rot": (0, 0, 0)}, "leg_R": {"rot": (0, 0, 0)}, "arm_L": {"rot": (0, 0, 0)}, "arm_R": {"rot": (0, 0, 0)}},
            32: {"hips": {"loc": (0, 0, 0.018)}, "body": {"rot": (4, 4, 0)}, "leg_L": {"rot": (20, 0, 0)}, "leg_R": {"rot": (-18, 0, 0)}, "arm_L": {"rot": (-8, 0, 0)}, "arm_R": {"rot": (8, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Run",
        24,
        {
            0: {"hips": {"loc": (0, 0, 0.028)}, "body": {"rot": (8, 6, 0)}, "leg_L": {"rot": (30, 0, 0)}, "leg_R": {"rot": (-28, 0, 0)}, "arm_L": {"rot": (-12, 0, 0)}, "arm_R": {"rot": (12, 0, 0)}},
            6: {"hips": {"loc": (0, 0, 0.05)}, "body": {"rot": (8, 0, 0)}, "leg_L": {"rot": (0, 0, 0)}, "leg_R": {"rot": (0, 0, 0)}},
            12: {"hips": {"loc": (0, 0, 0.028)}, "body": {"rot": (8, -6, 0)}, "leg_L": {"rot": (-28, 0, 0)}, "leg_R": {"rot": (30, 0, 0)}, "arm_L": {"rot": (12, 0, 0)}, "arm_R": {"rot": (-12, 0, 0)}},
            18: {"hips": {"loc": (0, 0, 0.05)}, "body": {"rot": (8, 0, 0)}, "leg_L": {"rot": (0, 0, 0)}, "leg_R": {"rot": (0, 0, 0)}},
            24: {"hips": {"loc": (0, 0, 0.028)}, "body": {"rot": (8, 6, 0)}, "leg_L": {"rot": (30, 0, 0)}, "leg_R": {"rot": (-28, 0, 0)}, "arm_L": {"rot": (-12, 0, 0)}, "arm_R": {"rot": (12, 0, 0)}},
        },
        loop=True,
    )

    set_pose_keys(
        arm_obj,
        "Jump",
        26,
        {
            0: {"hips": {"loc": (0, 0, 0.0)}},
            5: {"hips": {"loc": (0, 0, 0.015)}, "body": {"rot": (8, 0, 0)}},
            13: {"hips": {"loc": (0, 0, 0.24)}, "body": {"rot": (-6, 0, 0)}, "arm_L": {"rot": (24, 0, 0)}, "arm_R": {"rot": (24, 0, 0)}, "leg_L": {"rot": (18, 0, 0)}, "leg_R": {"rot": (-18, 0, 0)}},
            22: {"hips": {"loc": (0, 0, 0.02)}, "body": {"rot": (6, 0, 0)}},
            26: {"hips": {"loc": (0, 0, 0.0)}},
        },
        loop=False,
    )

    set_pose_keys(
        arm_obj,
        "Attack",
        24,
        {
            0: {"body": {"rot": (0, 0, 0)}, "arm_L": {"rot": (0, 0, 0)}, "arm_R": {"rot": (0, 0, 0)}},
            8: {"body": {"rot": (-8, 0, 0)}, "arm_L": {"rot": (42, 0, 0)}, "arm_R": {"rot": (42, 0, 0)}, "head": {"rot": (-6, 0, 0)}},
            14: {"body": {"rot": (14, 0, 0)}, "arm_L": {"rot": (-28, 0, 0)}, "arm_R": (-28, 0, 0) if False else {"rot": (-28, 0, 0)}, "head": {"rot": (6, 0, 0)}},
            24: {"body": {"rot": (0, 0, 0)}, "arm_L": {"rot": (0, 0, 0)}, "arm_R": {"rot": (0, 0, 0)}},
        },
        loop=False,
    )

    set_pose_keys(
        arm_obj,
        "Hurt",
        18,
        {
            0: {"body": {"rot": (0, 0, 0)}},
            5: {"body": {"rot": (-14, 0, 8)}, "head": {"rot": (-14, 0, -10)}, "arm_L": {"rot": (22, 0, 0)}, "arm_R": {"rot": (18, 0, 0)}},
            18: {"body": {"rot": (0, 0, 0)}},
        },
        loop=False,
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


def verify_glb_and_render(name: str, glb_path: Path) -> dict:
    """Re-imports the exported GLB in a clean scene, verifies geometry & all animations, and renders previews."""
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

    # Check no external image paths
    external_images = [img.filepath for img in bpy.data.images if img.filepath]

    # Measure rest pose & each animation clip in Godot coordinates:
    # Blender (X, Y, Z) -> Godot (X, Z, -Y)
    depsgraph = bpy.context.evaluated_depsgraph_get()

    def measure_current_godot_bounds() -> dict:
        xs, ys, zs = [], [], []
        for mo in mesh_objs:
            eval_obj = mo.evaluated_get(depsgraph)
            tmp = eval_obj.to_mesh()
            mat_w = eval_obj.matrix_world
            for v in tmp.vertices:
                w = mat_w @ v.co
                # Convert Blender Z-up, -Y-forward -> Godot Y-up, -Z-forward
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
    if arm_obj and arm_obj.animation_data:
        tracks = list(arm_obj.animation_data.nla_tracks)
        for t in tracks:
            t.mute = True
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

        # Reset to Idle frame 0 for preview
        for t in tracks:
            if t.name == "Idle":
                strip = t.strips[0]
                arm_obj.animation_data.action = strip.action
                if hasattr(arm_obj.animation_data, "action_slot") and strip.action.slots:
                    arm_obj.animation_data.action_slot = strip.action.slots[0]
                bpy.context.scene.frame_set(int(strip.frame_start))
                depsgraph.update()
                break

    rest_bounds = measure_current_godot_bounds()

    # Render previews: 3/4 view + side view showing floor plane at Z=0
    render_previews(name, arm_obj, rest_bounds)

    sha = hashlib.sha256(glb_path.read_bytes()).hexdigest()
    return {
        "name": name,
        "relative_source": f"source/{name}.blend",
        "relative_export": f"exports/{name}.glb",
        "relative_preview": f"previews/{name}_preview.png",
        "relative_side_preview": f"previews/{name}_side.png",
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


def render_previews(name: str, arm_obj: bpy.types.Object | None, bounds: dict) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.display.shading.show_shadows = True
    scene.render.resolution_x = 960
    scene.render.resolution_y = 720
    scene.render.image_settings.file_format = "PNG"

    # Add subtle ground reference grid plane at Z=0
    bpy.ops.mesh.primitive_plane_add(size=2.4, location=(0, 0, -0.001))
    ground = bpy.context.active_object
    ground.name = "PreviewFloor"
    gmat = bpy.data.materials.new("FloorMat")
    gmat.use_nodes = True
    ground.data.materials.append(gmat)

    h = bounds["height_y"]
    cx = (bounds["min_x"] + bounds["max_x"]) * 0.5
    cz_blender = h * 0.45
    cy_blender = -(bounds["min_z"] + bounds["max_z"]) * 0.5

    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_obj = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    def point_camera(loc: tuple[float, float, float], target: tuple[float, float, float]) -> None:
        cam_obj.location = Vector(loc)
        direction = Vector(target) - cam_obj.location
        cam_obj.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()

    dist = max(1.8, h * 1.9)
    # 3/4 view (front is +Y in Blender, mapping to -Z in Godot)
    point_camera((cx + dist * 0.72, cy_blender + dist * 0.82, cz_blender + dist * 0.35), (cx, cy_blender, cz_blender))
    scene.render.filepath = str(PREVIEW_DIR / f"{name}_preview.png")
    bpy.ops.render.render(write_still=True)

    # Side view (orthographic-like low angle to verify foot contact at Z=0)
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(1.5, h * 1.35)
    point_camera((cx + 3.0, cy_blender, cz_blender), (cx, cy_blender, cz_blender))
    scene.render.filepath = str(PREVIEW_DIR / f"{name}_side.png")
    bpy.ops.render.render(write_still=True)


def main() -> None:
    SOURCE_DIR.mkdir(parents=True, exist_ok=True)
    EXPORT_DIR.mkdir(parents=True, exist_ok=True)
    PREVIEW_DIR.mkdir(parents=True, exist_ok=True)

    builders = {
        "dog": build_dog,
        "rabbit": build_rabbit,
        "fox": build_fox,
        "zombie": build_zombie,
    }

    manifest = {
        "project": "JOSCRAFT",
        "target_engine": "Godot 4.7.2 Mono",
        "authorship": "Original procedural low-poly models, rigs, vertex palettes, and animations created by Antigravity (no third-party or copyrighted assets).",
        "coordinate_convention": {
            "units": "meters",
            "up": "+Y",
            "forward": "-Z",
            "origin": "floor under feet (Y=0)",
        },
        "creatures": {},
    }

    for name, fn in builders.items():
        clear_scene()
        fn()
        blend_path = SOURCE_DIR / f"{name}.blend"
        glb_path = EXPORT_DIR / f"{name}.glb"
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))
        export_glb(glb_path)
        manifest["creatures"][name] = verify_glb_and_render(name, glb_path)

    manifest_path = BASE_DIR / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"DONE: wrote {manifest_path}")


if __name__ == "__main__":
    main()
