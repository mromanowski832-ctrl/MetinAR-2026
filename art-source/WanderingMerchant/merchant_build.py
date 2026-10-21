"""METINAR-016 — Wandering Merchant build pipeline (original MetinAR NPC asset).

blender -b --factory-startup --python merchant_build.py -- <stage> <out_dir>
  greybox : model + rig, untextured studio renders + counts (checkpoint)
  full    : model, rig, LODs, baked 1K textures, animations, FBX export, renders, report
"""
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import merchant_model as mm  # noqa: E402
import render_util as ru  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = argv[0] if argv else "greybox"
OUT = os.path.abspath(argv[1] if len(argv) > 1 else "out")
os.makedirs(OUT, exist_ok=True)
ASSET_ID = "WanderingMerchant"


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_bounds(obj):
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    return (min(xs), max(xs)), (min(ys), max(ys)), (min(zs), max(zs))


def studio_cams():
    # the merchant faces Blender +Y, so the "front" camera sits at +Y
    return [
        ("hero_34", ru.camera("Hero", (2.3, 3.3, 1.75), target=(0, 0, 0.95), lens=50)),
        ("front", ru.camera("Front", (0, 4.2, 1.0), target=(0, 0, 0.92), lens=50)),
        ("side", ru.camera("Side", (-4.2, 0, 1.0), target=(0, 0, 0.92), lens=50)),
        ("back_34", ru.camera("Back", (-2.4, -3.2, 1.8), target=(0, 0, 1.0), lens=50)),
    ]


def greybox():
    reset()
    mesh = mm.build_mesh()
    rig = mm.build_armature()
    mm.bind(mesh, rig)
    grey = bpy.data.materials.new("M_Grey")
    grey.use_nodes = True
    grey.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.45, 0.44, 0.42, 1)
    mesh.data.materials.append(grey)
    rig.hide_render = True
    bx, by, bz = mesh_bounds(mesh)
    info = {"triangles_lod0": mm.triangle_count(mesh), "vertices": len(mesh.data.vertices), "deform_bones": len(mm.DEFORM_BONES),
            "width_x_m": round(bx[1] - bx[0], 3), "depth_y_m": round(by[1] - by[0], 3), "top_z_m": round(bz[1], 3), "min_z_m": round(bz[0], 4)}
    ru.setup_scene(samples=24, res=(900, 1100))
    ru.add_ground()
    ru.add_lights()
    for name, cam in studio_cams():
        ru.render(cam, os.path.join(OUT, "greybox_%s.png" % name))
    with open(os.path.join(OUT, "greybox.json"), "w") as f:
        json.dump(info, f, indent=2)
    print("GREYBOX", json.dumps(info))


if STAGE == "greybox":
    greybox()


# ---------------------------------------------------------------------------------------------- full build
import merchant_anim as ma  # noqa: E402
import merchant_texture as mt  # noqa: E402
import bake_util as bu  # noqa: E402

LOD_RATIOS = {1: 0.5, 2: 0.25}


def make_lod(src, level):
    obj = src.copy()
    obj.data = src.data.copy()
    obj.name = obj.data.name = "SK_Npc_%s_LOD%d" % (ASSET_ID, level)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = LOD_RATIOS[level]
    mod.use_symmetry = True
    mod.symmetry_axis = "X"
    mod.use_collapse_triangulate = True
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def rest_pose(rig):
    rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()


FBX_COMMON = dict(
    use_selection=True, use_armature_deform_only=True, add_leaf_bones=False,
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    use_space_transform=True, primary_bone_axis="Y", secondary_bone_axis="X",
    path_mode="STRIP", embed_textures=False, use_custom_props=False,
)


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def export_model(rig, lods, path):
    rest_pose(rig)
    select_only([rig] + lods)
    bpy.ops.export_scene.fbx(filepath=path, object_types={"ARMATURE", "MESH"}, bake_anim=False,
                             mesh_smooth_type="OFF", use_triangles=True, use_tspace=False,
                             use_mesh_modifiers=False, **FBX_COMMON)


def export_clip(rig, action, last, path):
    rest_pose(rig)
    rig.animation_data.action = action
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, last
    select_only([rig])
    bpy.ops.export_scene.fbx(filepath=path, object_types={"ARMATURE"}, bake_anim=True,
                             bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0, **FBX_COMMON)


POSE_SHOTS = [("Idle", 19), ("Walk", 8), ("Walk", 23), ("TurnLeft", 16), ("Talk", 16), ("Interact", 20), ("Gesture", 25)]


def full():
    reset()
    sc = bpy.context.scene
    sc.render.fps = ma.FPS
    asset = os.path.join(OUT, ASSET_ID)
    models, anims, texdir, renders = (os.path.join(asset, d) for d in ("Models", "Animations", "Textures", "renders"))
    for d in (models, anims, texdir, renders):
        os.makedirs(d, exist_ok=True)

    lod0 = mm.build_mesh()
    bu.unwrap(lod0)
    paths = mt.make_textures(lod0, texdir)
    lods = [lod0, make_lod(lod0, 1), make_lod(lod0, 2)]
    rig = mm.build_armature()
    mat = bu.final_material("M_" + ASSET_ID, paths, emission_strength=2.0)
    for o in lods:
        mm.bind(o, rig)
        o.data.materials.clear()
        o.data.materials.append(mat)
    actions = ma.build_actions(rig)

    report = {
        "triangles": {o.name: mm.triangle_count(o) for o in lods},
        "vertices": {o.name: len(o.data.vertices) for o in lods},
        "deform_bones": len(mm.DEFORM_BONES),
        "bone_names": mm.DEFORM_BONES,
        "materials": 1,
        "texture_size": bu.SIZE,
        "textures": sorted(os.path.basename(p) for p in paths.values()),
        "fps": ma.FPS,
        "clips": {},
    }
    t0 = report["triangles"][lod0.name]
    report["lod_ratios"] = {o.name: round(report["triangles"][o.name] / t0, 3) for o in lods}
    bx, by, bz = mesh_bounds(lod0)
    report["rest_bounds_m"] = {"width_x": round(bx[1] - bx[0], 3), "depth_y": round(by[1] - by[0], 3), "top_z": round(bz[1], 3), "min_z": round(bz[0], 4)}
    for state, act in actions.items():
        last, loop = ma.CLIPS[state]
        lowest = ma.min_deformed_z(lod0, rig, act, last)
        report["clips"]["A_%s_%s" % (ASSET_ID, state)] = {"frames": last, "seconds": round((last - 1) / ma.FPS, 3), "loop": loop,
                                                         "min_deformed_z_m": round(lowest, 4)}

    export_model(rig, lods, os.path.join(models, "SK_Npc_%s.fbx" % ASSET_ID))
    for state, act in actions.items():
        export_clip(rig, act, ma.CLIPS[state][0], os.path.join(anims, "A_%s_%s.fbx" % (ASSET_ID, state)))
    rest_pose(rig)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, ASSET_ID + "_work.blend"))

    # ------------------------------------------------------------------ renders
    rig.hide_render = True
    ru.setup_scene(samples=40, res=(1000, 1200))
    ru.add_ground()
    ru.add_lights()
    lod1, lod2 = lods[1], lods[2]
    lod1.hide_render = lod2.hide_render = True
    for name, cam in studio_cams():
        ru.render(cam, os.path.join(renders, name + ".png"))

    for o, x in ((lod0, -1.6), (lod1, 0.0), (lod2, 1.6)):
        o.hide_render = False
        o.parent = None
        o.modifiers["Armature"].show_render = False
        o.location.x = x
    sc.render.resolution_x, sc.render.resolution_y = 1600, 900
    ru.render(ru.camera("Lods", (0, 6.2, 1.5), target=(0, 0, 0.95), lens=40), os.path.join(renders, "lods.png"))
    for o in lods:
        o.location.x = 0
        o.parent = rig
        o.modifiers["Armature"].show_render = True
    lod1.hide_render = lod2.hide_render = True

    sc.render.resolution_x, sc.render.resolution_y = 800, 1000
    cam = ru.camera("Pose", (2.2, 3.4, 1.6), target=(0, 0, 0.95), lens=50)
    for state, frame in POSE_SHOTS:
        rest_pose(rig)
        rig.animation_data.action = actions[state]
        sc.frame_set(frame)
        ru.render(cam, os.path.join(renders, "pose_%s_f%02d.png" % (state, frame)))
    rest_pose(rig)

    with open(os.path.join(asset, "build_report.json"), "w") as f:
        json.dump(report, f, indent=2)
    print("REPORT", json.dumps(report))


if STAGE == "full":
    full()
