"""METINAR-032 — Lipowy Puklerz (starter_buckler) build: mesh, 512 textures, FBX export, preview renders, report.

blender -b --factory-startup --python art-source/StarterBuckler/buckler_build.py -- <out_dir>
Output: <out_dir>/StarterBuckler/{Models,Textures,renders}, build_report.json, StarterBuckler_work.blend
Deterministic: no random seeds, no external files.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import buckler_model as M  # noqa: E402
import buckler_texture as T  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "out")
ASSET_ID = "StarterBuckler"
MODEL_NAME = "SM_Weapon_" + ASSET_ID

# Unity's FBX import (Bake Axis Conversion) maps an empty with identity Blender rotation to Euler(90,0,0)
# (measured in Unity 6000.6.4f1 for METINAR-FPP-ART-001). Sockets carry +90 deg about X so that they import
# with the same frame as the model (identity local rotation); WeaponStarterBuckler asset check verifies it.
SOCKET_FIX = Matrix.Rotation(math.radians(90.0), 4, "X")
TURN = Matrix.Rotation(math.pi, 4, "Z")  # front view (-Y) -> +Y = Unity +Z

FBX_COMMON = dict(
    use_selection=True, add_leaf_bones=False, axis_forward="-Z", axis_up="Y", apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL", use_space_transform=True, path_mode="STRIP", embed_textures=False,
    use_custom_props=False,
)


def bounds(obj):
    vs = [obj.matrix_world @ v.co for v in obj.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return lo, hi


def to_unity(v):
    return [round(v.x, 4), round(v.z, 4), round(v.y, 4)]


def setup_render(res=(1000, 1000), samples=48):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Studio")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.32, 0.34, 0.37, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    sc.world = world
    for kind, loc, rot, energy, size in (("SUN", (1, -2, 3), (50, 10, 30), 2.0, 0), ("AREA", (-1.0, -0.8, 0.6), (60, 0, -55), 30, 1.2),
                                         ("AREA", (0.6, 1.0, 0.5), (-65, 0, 160), 35, 1.0)):
        d = bpy.data.lights.new(kind, kind)
        d.energy = energy
        if kind == "AREA":
            d.size = size
        o = bpy.data.objects.new(kind, d)
        o.location = loc
        o.rotation_euler = [math.radians(a) for a in rot]
        sc.collection.objects.link(o)


def camera(name, loc, target=(0, -0.05, 0), lens=60):
    d = bpy.data.cameras.new(name)
    d.lens = lens
    d.clip_start = 0.01
    o = bpy.data.objects.new(name, d)
    bpy.context.scene.collection.objects.link(o)
    o.location = Vector(loc)
    o.rotation_euler = (Vector(target) - o.location).to_track_quat("-Z", "Y").to_euler()
    return o


def render(cam, path):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    asset = os.path.join(OUT, ASSET_ID)
    models, texdir, renders = (os.path.join(asset, d) for d in ("Models", "Textures", "renders"))
    for d in (models, texdir, renders):
        os.makedirs(d, exist_ok=True)

    obj = M.build(MODEL_NAME)
    paths = T.make_textures(texdir, ASSET_ID)
    mat = T.preview_material("M_" + ASSET_ID, paths)
    obj.data.materials.append(mat)
    socks = []
    for name, loc in M.sockets():
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 0.03
        bpy.context.scene.collection.objects.link(e)
        e.parent = obj
        e.matrix_parent_inverse = Matrix.Identity(4)
        e.matrix_basis = Matrix.Translation(loc) @ SOCKET_FIX
        socks.append(e)

    lo, hi = bounds(obj)
    report = {
        "asset": ASSET_ID, "item_id": "starter_buckler", "name_pl": "Lipowy Puklerz",
        "triangles": M.triangle_count(obj), "vertices": len(obj.data.vertices), "materials": len(obj.data.materials),
        "bones": 0, "lods": 1, "texture_size": T.SIZE, "textures": sorted(os.path.basename(p) for p in paths.values()),
        "budget_spec_section_8": {"triangles_max": 1500, "bones": 0, "materials": 1, "texture": 512, "lod": "none"},
        "diameter_m": round(hi.x - lo.x, 4),
    }

    # export orientation: front towards Unity +Z
    obj.data.transform(TURN)
    obj.data.update()
    for e in socks:
        e.matrix_basis = Matrix.Translation(TURN @ e.matrix_basis.translation) @ SOCKET_FIX
    bpy.context.view_layer.update()
    lo, hi = bounds(obj)
    report["bounds_unity_m"] = {"min": to_unity(lo), "max": to_unity(hi), "size": to_unity(hi - lo)}
    report["pivot"] = "centre of the grip (fist), handle along Unity +Y, front face towards Unity +Z"
    report["sockets_unity_m"] = {e.name: to_unity(e.matrix_basis.translation) for e in socks}
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for e in socks:
        e.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(models, MODEL_NAME + ".fbx"), object_types={"MESH", "EMPTY"}, bake_anim=False,
                             mesh_smooth_type="OFF", use_triangles=True, use_tspace=False, use_mesh_modifiers=False, **FBX_COMMON)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, ASSET_ID + "_work.blend"))

    # ---- preview renders (model in export orientation: front towards Blender +Y)
    setup_render()
    shots = [("front", (0, 0.75, 0.05), (0, 0, 0)), ("hero_34", (0.45, 0.55, 0.3), (0, 0.02, 0)),
             ("back_34", (-0.42, -0.5, 0.25), (0, 0, 0)), ("side", (0.75, 0.0, 0.0), (0, 0.04, 0))]
    for name, loc, target in shots:
        render(camera(name, loc, target, lens=50), os.path.join(renders, name + ".png"))
    with open(os.path.join(asset, "build_report.json"), "w") as f:
        json.dump(report, f, indent=2)
    print("REPORT", json.dumps(report))


main()
