"""METINAR-013 — Venomous Spider build pipeline (original MetinAR asset).

blender -b --factory-startup --python spider_build.py -- <stage> <out_dir>
  greybox : model + rig, untextured studio renders + counts (checkpoint)
  full    : model, rig, LODs, baked 1K textures, animations, FBX export, renders, report
"""
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import render_util as ru  # noqa: E402
import spider_model as sm  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = argv[0] if argv else "greybox"
OUT = os.path.abspath(argv[1] if len(argv) > 1 else "out")
os.makedirs(OUT, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_bounds(obj):
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))


def greybox():
    reset()
    mesh = sm.build_mesh()
    rig = sm.build_armature()
    sm.bind(mesh, rig)
    grey = bpy.data.materials.new("M_Grey")
    grey.use_nodes = True
    grey.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.40, 0.44, 1)
    grey.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.6
    mesh.data.materials.append(grey)
    rig.hide_render = True
    bx, by, bz = mesh_bounds(mesh)
    info = {
        "triangles_lod0": sm.triangle_count(mesh),
        "vertices": len(mesh.data.vertices),
        "deform_bones": len(sm.DEFORM_BONES),
        "span_x_m": round(bx[1] - bx[0], 3),
        "length_y_m": round(by[1] - by[0], 3),
        "height_m": round(bz[1], 3),
        "min_z_m": round(bz[0], 4),
    }
    ru.setup_scene(samples=32)
    ru.add_ground()
    ru.add_span_marker(1.3, y=0.78)
    ru.add_lights()
    ru.render(ru.camera("Cam34", (1.9, -2.3, 1.45)), os.path.join(OUT, "greybox_34.png"))
    ru.render(ru.camera("CamTop", (0, 0.1, 4.0), target=(0, 0.1, 0), ortho_scale=2.3), os.path.join(OUT, "greybox_top.png"))
    ru.render(ru.camera("CamSide", (3.5, 0, 0.35), target=(0, 0, 0.35), ortho_scale=1.7), os.path.join(OUT, "greybox_side.png"))
    with open(os.path.join(OUT, "greybox.json"), "w") as f:
        json.dump(info, f, indent=2)
    print("GREYBOX", json.dumps(info))


if STAGE == "greybox":
    greybox()
