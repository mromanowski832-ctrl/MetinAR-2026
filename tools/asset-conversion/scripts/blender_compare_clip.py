"""Compare the DEFORMED mesh of two FBX clips (reference vs converted), frame by frame.

Run: blender -b --factory-startup --python blender_compare_clip.py -- REF.fbx TEST.fbx [OFFSET]
For every frame of the reference, each deformed reference vertex is matched to the nearest
deformed test vertex (KD-tree, so vertex order/splitting does not matter). Reports the max
distance in metres, and the frame offset that minimises it (searched in -2..+2 unless given).
Bone-axis conventions do not affect this metric; only the visible result does.
"""
from __future__ import annotations

import json
import os
import sys

import bpy  # type: ignore
from mathutils.kdtree import KDTree  # type: ignore

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _blender_common import reset_scene, script_args  # noqa: E402


def frames_of(path):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    sc = bpy.context.scene
    arm = next(o for o in sc.objects if o.type == "ARMATURE")
    s, e = (int(round(v)) for v in arm.animation_data.action.frame_range)
    meshes = [o for o in sc.objects if o.type == "MESH"]
    out = {}
    dg = bpy.context.evaluated_depsgraph_get()
    for f in range(s, e + 1):
        sc.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        pts = []
        for m in meshes:
            ev = m.evaluated_get(dg)
            me = ev.to_mesh()
            pts += [ev.matrix_world @ v.co for v in me.vertices]
            ev.to_mesh_clear()
        out[f - s] = pts
    return out


def max_dist(a, b):
    kd = KDTree(len(b))
    for i, p in enumerate(b):
        kd.insert(p, i)
    kd.balance()
    return max(kd.find(p)[2] for p in a)


def main():
    args = script_args()
    ref, tst = frames_of(args[0]), frames_of(args[1])
    offsets = [int(args[2])] if len(args) > 2 else range(-2, 3)
    best = None
    for off in offsets:
        common = [f for f in ref if f + off in tst]
        if not common:
            continue
        d = max(max_dist(ref[f], tst[f + off]) for f in common)
        if best is None or d < best[1]:
            best = (off, d, len(common))
    print("COMPARE " + json.dumps({"ref_frames": len(ref), "test_frames": len(tst), "best_offset": best[0],
                                   "max_vertex_error_m": round(best[1], 5), "frames_compared": best[2]}))


if __name__ == "__main__":
    main()
