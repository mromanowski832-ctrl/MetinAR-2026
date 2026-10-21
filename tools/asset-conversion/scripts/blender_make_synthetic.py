"""Create a SYNTHETIC test set (no third-party content): rigged mesh + 3 clips + texture.

Outputs (OUT_DIR):
  synthetic_model.gr2            mesh + skeleton + weights (via io_scene_gr2 raw exporter)
  synthetic_<clip>.gr2           one animation per file (Metin2 convention), clips: wait, run, attack
  synthetic_ref_<clip>.fbx       Blender-native reference FBX per clip (ground truth for comparison)
  synthetic.msm / synthetic_*.msa  text scripts in the Metin2 script grammar
  synthetic_texture.png          generated texture; DDS is produced by the shell runner

Run: blender -b --factory-startup --python blender_make_synthetic.py -- OUT_DIR
"""
from __future__ import annotations

import math
import os
import sys

import bpy  # type: ignore

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _blender_common import enable_gr2_addon, export_fbx, reset_scene, script_args  # noqa: E402

FPS = 30
CLIPS = {"wait": 30, "run": 20, "attack": 24}


def _rows(m):
    return tuple(v for row in m for v in row)


def _cols(m):
    return tuple(m[r][c] for c in range(4) for r in range(4))


def _write_granny_bind_props(arm) -> None:
    """Give the hand-made rig the bind data a real Granny exporter would write
    (parent-relative local transform + inverse world bind matrix), so the raw GR2
    writer of io_scene_gr2 produces a structurally valid skeleton. Bone frames are
    world-aligned (identity orientation); Blender bone axes are mapped via gr2_axis_remap."""
    from mathutils import Matrix  # type: ignore

    world = {b.name: Matrix.Translation(b.head_local) for b in arm.data.bones}
    for b in arm.data.bones:
        local = (world[b.parent.name].inverted() @ world[b.name]) if b.parent else world[b.name]
        pb = arm.pose.bones[b.name]
        pb["gr2_transform_flags"] = 1  # HasPosition
        pb["gr2_local_position"] = tuple(local.translation)
        pb["gr2_local_orientation_xyzw"] = (0.0, 0.0, 0.0, 1.0)
        b["gr2_inverse_world_transform"] = _cols(world[b.name].inverted())
        b["gr2_granny_rest_local"] = _rows(local)
        blender_local = (b.parent.matrix_local.inverted() @ b.matrix_local) if b.parent else b.matrix_local.copy()
        b["gr2_axis_remap"] = _rows(local.inverted() @ blender_local)


def build(out_dir: str) -> None:
    reset_scene()
    version = enable_gr2_addon()
    scene = bpy.context.scene
    scene.render.fps = FPS
    os.makedirs(out_dir, exist_ok=True)

    # texture (procedural checker with a binary-alpha hole)
    size = 64
    img = bpy.data.images.new("synthetic_texture", size, size, alpha=True)
    px = []
    for y in range(size):
        for x in range(size):
            c = 0.9 if ((x // 8 + y // 8) % 2) else 0.2
            a = 0.0 if (24 <= x < 40 and 24 <= y < 40) else 1.0
            px += [c, 0.3, 1.0 - c, a]
    img.pixels = px
    img.filepath_raw = os.path.join(out_dir, "synthetic_texture.png")
    img.file_format = "PNG"
    img.save()

    # armature: B_Root -> B_Body -> B_Head (Z-up like Metin2 data)
    arm_data = bpy.data.armatures.new("SyntheticSkeleton")
    arm = bpy.data.objects.new("SyntheticSkeleton", arm_data)
    scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    root = eb.new("B_Root"); root.head = (0, 0, 0); root.tail = (0, 0, 0.2)
    body = eb.new("B_Body"); body.head = (0, 0, 0.2); body.tail = (0, 0, 0.8); body.parent = root
    head = eb.new("B_Head"); head.head = (0, 0, 0.8); head.tail = (0, 0, 1.2); head.parent = body
    bpy.ops.object.mode_set(mode="OBJECT")

    _write_granny_bind_props(arm)

    # mesh: subdivided column 0..1.2 m with UVs and a material using the texture
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.25, depth=1.2, location=(0, 0, 0.6))
    mesh = bpy.context.active_object
    mesh.name = "SyntheticBody"
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.subdivide(number_cuts=3)
    bpy.ops.mesh.quads_convert_to_tris()
    bpy.ops.uv.smart_project()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    mat = bpy.data.materials.new("synthetic_material")
    mat.use_nodes = True
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    mesh.data.materials.append(mat)

    # weights by height
    groups = {n: mesh.vertex_groups.new(name=n) for n in ("B_Root", "B_Body", "B_Head")}
    for v in mesh.data.vertices:
        z = v.co.z
        if z < 0.2:
            groups["B_Root"].add([v.index], 1.0, "REPLACE")
        elif z < 0.8:
            t = (z - 0.2) / 0.6
            groups["B_Body"].add([v.index], 1.0 - 0.5 * t, "REPLACE")
            groups["B_Head"].add([v.index], 0.5 * t, "REPLACE")
        else:
            groups["B_Head"].add([v.index], 1.0, "REPLACE")
    mesh.parent = arm
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm

    # model gr2 (no action active)
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True); arm.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.export_scene.gr2_native(filepath=os.path.join(out_dir, "synthetic_model.gr2"),
                                    selected_only=True, export_compression="RAW")

    # clips
    arm.animation_data_create()
    for name, frames in CLIPS.items():
        act = bpy.data.actions.new(name)
        arm.animation_data.action = act
        pb = arm.pose.bones
        for f in range(0, frames + 1, 2):
            ph = 2 * math.pi * f / frames
            if name == "wait":
                pb["B_Body"].rotation_mode = "XYZ"; pb["B_Body"].rotation_euler = (0, 0.1 * math.sin(ph), 0)
                pb["B_Body"].keyframe_insert("rotation_euler", frame=f + 1)
            elif name == "run":
                pb["B_Root"].location = (0, 0.05 * math.sin(2 * ph), 0)
                pb["B_Root"].keyframe_insert("location", frame=f + 1)
                pb["B_Head"].rotation_mode = "XYZ"; pb["B_Head"].rotation_euler = (0.3 * math.sin(2 * ph), 0, 0)
                pb["B_Head"].keyframe_insert("rotation_euler", frame=f + 1)
            else:
                pb["B_Body"].rotation_mode = "XYZ"; pb["B_Body"].rotation_euler = (0.8 * math.sin(ph / 2), 0, 0)
                pb["B_Body"].keyframe_insert("rotation_euler", frame=f + 1)
        act.frame_range = (1, frames + 1)
        scene.frame_start, scene.frame_end = 1, frames + 1
        bpy.ops.object.select_all(action="DESELECT")
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.export_scene.gr2_native(filepath=os.path.join(out_dir, f"synthetic_{name}.gr2"),
                                        selected_only=True, export_compression="RAW")
        export_fbx(os.path.join(out_dir, f"synthetic_ref_{name}.fbx"), [arm, mesh], with_animation=True)
        with open(os.path.join(out_dir, f"synthetic_{name}.msa"), "w", encoding="utf-8") as fh:
            fh.write(f'ScriptType               MotionData\n\nMotionFileName           "d:/ymir work/synthetic/synthetic_{name}.gr2"\n'
                     f"MotionDuration           {frames / FPS:.6f}\nAccumulation             0.00 0.00 0.00\n")
    with open(os.path.join(out_dir, "synthetic.msm"), "w", encoding="utf-8") as fh:
        fh.write('ScriptType               CharacterModelScript\n\nBaseModelFileName        "d:/ymir work/synthetic/synthetic_model.gr2"\n'
                 "Group ModelData\n{\n    Group Texture00\n    {\n        FileName \"d:/ymir work/synthetic/synthetic_texture.dds\"\n    }\n}\n")
    print(f"SYNTHETIC_OK addon={version} out={out_dir}")


if __name__ == "__main__":
    build(script_args()[0])
