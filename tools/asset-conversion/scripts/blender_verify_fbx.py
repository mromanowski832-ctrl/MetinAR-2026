"""Re-import produced FBX files in a clean Blender scene and check them.

Run: blender -b --factory-startup --python blender_verify_fbx.py -- REPORT.json FILE.fbx [...]
Checks: one armature, skinned mesh with vertex groups matching bones, triangle count,
take/frame range for clip files, mesh bounds (metres, pivot at ground +-2 cm).
"""
from __future__ import annotations

import json
import os
import sys

import bpy  # type: ignore
from mathutils import Vector  # type: ignore

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _blender_common import reset_scene, script_args  # noqa: E402


def check(path: str) -> dict:
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    sc = bpy.context.scene
    arms = [o for o in sc.objects if o.type == "ARMATURE"]
    meshes = [o for o in sc.objects if o.type == "MESH"]
    r: dict = {"file": os.path.basename(path), "armatures": len(arms), "meshes": len(meshes), "errors": []}
    if len(arms) != 1:
        r["errors"].append("armature count != 1")
    bones = set(arms[0].data.bones.keys()) if arms else set()
    r["bones"] = sorted(bones)
    tris, zmin, zmax = 0, 1e9, -1e9
    for m in meshes:
        m.data.calc_loop_triangles()
        tris += len(m.data.loop_triangles)
        groups = {g.name for g in m.vertex_groups}
        if not groups:
            r["errors"].append(f"{m.name}: no skin weights")
        elif not groups <= bones:
            r["errors"].append(f"{m.name}: weight groups without bones {sorted(groups - bones)}")
        if not any(md.type == "ARMATURE" for md in m.modifiers):
            r["errors"].append(f"{m.name}: not bound to armature")
        for c in m.bound_box:
            w = m.matrix_world @ Vector(c)
            zmin, zmax = min(zmin, w.z), max(zmax, w.z)
        r["uv_layers"] = len(m.data.uv_layers)
        r["materials"] = [s.material.name for s in m.material_slots if s.material]
    r["triangles"] = tris
    r["height_m"] = round(zmax - zmin, 4) if meshes else 0
    r["ground_offset_m"] = round(zmin, 4) if meshes else 0  # clips: value at the scene's current frame (root motion allowed)
    if meshes and abs(zmin) > 0.02 and not os.path.basename(path).startswith("A_"):
        r["errors"].append(f"pivot not at ground (min z {zmin:.3f} m)")
    acts = list(bpy.data.actions)
    r["actions"] = [{"name": a.name, "frames": list(a.frame_range), "fcurves": len(a.fcurves)} for a in acts]
    if os.path.basename(path).startswith("A_") and not any(len(a.fcurves) for a in acts):
        r["errors"].append("clip file has no animated curves")
    return r


def main() -> None:
    args = script_args()
    out, files = args[0], args[1:]
    results = [check(f) for f in files]
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(results, fh, indent=2)
    bad = sum(len(r["errors"]) for r in results)
    print(f"VERIFY_DONE files={len(results)} errors={bad}")


if __name__ == "__main__":
    main()
