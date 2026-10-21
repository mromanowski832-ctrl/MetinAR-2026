"""Convert one Metin2-style set: model .gr2 (+ clip .gr2 files) -> Unity-ready FBX files.

Run: blender -b --factory-startup --python blender_gr2_to_fbx.py -- \
        --model MODEL.gr2 --id AssetId --category Mob --out OUT_DIR \
        [--clip State=clip.gr2 ...] [--scale 0.01] [--textures DIR]

Outputs: SK_<Category>_<Id>.fbx (mesh + skeleton + weights, no takes),
         A_<Id>_<State>.fbx (skeleton + mesh, one baked take each), convert_report.json.
Never writes outside --out.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import bpy  # type: ignore

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _blender_common import enable_gr2_addon, export_fbx, reset_scene, script_args  # noqa: E402


def parse() -> argparse.Namespace:
    p = argparse.ArgumentParser()
    p.add_argument("--model", required=True)
    p.add_argument("--id", required=True)
    p.add_argument("--category", default="Mob", choices=["Mob", "Boss", "Npc", "Weapon"])
    p.add_argument("--out", required=True)
    p.add_argument("--clip", action="append", default=[], help="State=path.gr2")
    p.add_argument("--scale", type=float, default=1.0, help="Metin2 data is in cm -> use 0.01 for metres")
    p.add_argument("--textures", default="")
    return p.parse_args(script_args())


def main() -> None:
    a = parse()
    os.makedirs(a.out, exist_ok=True)
    reset_scene()
    addon_version = enable_gr2_addon()
    scene = bpy.context.scene
    scene.render.fps = 30
    bpy.ops.import_scene.gr2_native(filepath=a.model, import_mode="MODEL", scale=a.scale,
                                    import_animations=False, load_textures=bool(a.textures),
                                    texture_search_roots=a.textures)
    arms = [o for o in scene.objects if o.type == "ARMATURE"]
    meshes = [o for o in scene.objects if o.type == "MESH"]
    report: dict = {"tool": {"blender": bpy.app.version_string, "io_scene_gr2": addon_version},
                    "model": a.model, "id": a.id, "category": a.category, "scale": a.scale,
                    "armatures": len(arms), "meshes": len(meshes), "clips": [], "errors": []}
    if len(arms) != 1:
        report["errors"].append(f"expected exactly 1 armature, got {len(arms)}")
    arm = arms[0] if arms else None
    tris = 0
    for m in meshes:
        if arm is not None:
            m.parent = arm  # FBX skin binding needs the mesh under the armature
            m.matrix_parent_inverse = arm.matrix_world.inverted()
        m.data.calc_loop_triangles()
        tris += len(m.data.loop_triangles)
    report.update(triangles=tris, bones=len(arm.data.bones) if arm else 0,
                  materials=sorted({s.material.name for m in meshes for s in m.material_slots if s.material}),
                  vertex_groups=sorted({g.name for m in meshes for g in m.vertex_groups}))
    model_fbx = os.path.join(a.out, f"SK_{a.category}_{a.id}.fbx")
    export_fbx(model_fbx, ([arm] if arm else []) + meshes, with_animation=False)
    report["model_fbx"] = model_fbx
    for spec in a.clip:
        state, path = spec.split("=", 1)
        before = set(bpy.data.actions)
        bpy.ops.object.select_all(action="DESELECT")
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.import_scene.gr2_native(filepath=path, import_mode="ANIMATION_TO_SELECTED", scale=a.scale)
        new = [act for act in bpy.data.actions if act not in before]
        if not new:
            report["errors"].append(f"{state}: no action imported from {path}")
            continue
        act = new[0]
        arm.animation_data.action = act
        start, end = act.frame_range
        scene.frame_start, scene.frame_end = int(start), int(end)
        clip_fbx = os.path.join(a.out, f"A_{a.id}_{state}.fbx")
        export_fbx(clip_fbx, [arm] + meshes, with_animation=True)
        report["clips"].append({"state": state, "source": path, "action": act.name,
                                "frames": [start, end], "fcurves": len(act.fcurves),
                                "duration_s": (end - start) / scene.render.fps, "fbx": clip_fbx})
    with open(os.path.join(a.out, "convert_report.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=2)
    print("CONVERT_DONE errors=%d" % len(report["errors"]))


if __name__ == "__main__":
    main()
