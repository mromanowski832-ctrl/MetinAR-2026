"""METINAR-016 — Wandering Merchant build pipeline (original MetinAR NPC, Unity Humanoid).

blender -b --factory-startup --python merchant_build.py -- <stage> <out_dir>
  preview : model + LODs + rig + textures + clips, quick low-sample renders (iteration checkpoint, no export)
  full    : model (LOD0/1/2), rig, 1K textures, 6 clips, FBX export, .blend, studio renders, build_report.json
"""
import json
import math
import os
import sys

import bpy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import merchant_anim as ma  # noqa: E402
import merchant_model as mm  # noqa: E402
import merchant_texture as mt  # noqa: E402
import render_util as ru  # noqa: E402

ID = "WanderingMerchant"
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = argv[0] if argv else "preview"
OUT = os.path.abspath(argv[1] if len(argv) > 1 else "out")
os.makedirs(OUT, exist_ok=True)

FBX_COMMON = dict(
    use_selection=True, use_armature_deform_only=True, add_leaf_bones=False,
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    use_space_transform=True, primary_bone_axis="Y", secondary_bone_axis="X",
    path_mode="STRIP", embed_textures=False, use_custom_props=False,
)
# Pose shots used for the Blender renders and mirrored by the Unity check: (state, frame)
POSES = [("Idle", 23), ("Walk", 1), ("Walk", 10), ("TurnLeft", 16), ("TurnRight", 31), ("Talk", 9), ("Talk", 24),
         ("Interact", 30)]
LOD_RATIOS = {1: 0.5, 2: 0.25}


def reset_factory():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_bounds(obj):
    cs = [v.co for v in obj.data.vertices]
    return [(min(c[i] for c in cs), max(c[i] for c in cs)) for i in range(3)]


def make_lod(src, level):
    obj = src.copy()
    obj.data = src.data.copy()
    obj.name = obj.data.name = "SK_Npc_%s_LOD%d" % (ID, level)
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


def build(texdir):
    sc = bpy.context.scene
    sc.render.fps = ma.FPS
    lod0 = mm.build_mesh("SK_Npc_%s_LOD0" % ID)
    lods = [lod0, make_lod(lod0, 1), make_lod(lod0, 2)]
    rig = mm.build_armature()
    paths = mt.make_textures(texdir, "T_" + ID)
    mat = mt.final_material(paths, "M_" + ID)
    for o in lods:
        mm.bind(o, rig)
        o.data.materials.clear()
        o.data.materials.append(mat)
    actions = ma.build_actions(rig)
    return lods, rig, actions, paths


def rest_pose(rig):
    rig.animation_data.action = None
    ma.reset(rig)
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()


def select_only(objs):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]


def turn_for_unity(rig, meshes):
    """Rotate rest geometry + rest bones 180 deg about Z (involution). Unity's FBX import with Bake Axis Conversion
    maps Blender +Y to Unity +Z, so a character modelled facing Blender -Y must face +Y in the exported file
    (METINAR-009 section 3, verified in METINAR-013). Object transforms stay identity."""
    from mathutils import Matrix
    rz = Matrix.Rotation(math.pi, 4, "Z")
    rest_pose(rig)
    for o in meshes:
        o.data.transform(rz)
        o.data.update()
    bpy.context.view_layer.objects.active = rig
    rig.data.transform(rz)
    bpy.context.view_layer.update()


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
    rig.animation_data.action = None


def renders(lods, rig, actions, outdir, samples, res, full_set):
    os.makedirs(outdir, exist_ok=True)
    mesh = lods[0]
    rig.hide_render = True
    for o in lods[1:]:
        o.hide_render = True
    ru.setup_scene(samples=samples, res=res)
    ru.add_ground()
    ru.add_lights()
    marker = ru.add_height_marker(1.75)
    rest_pose(rig)
    ru.render(ru.camera("RestFront", (0, -4.2, 1.05), target=(0, 0, 0.95), ortho_scale=2.2), os.path.join(outdir, "rest_front.png"))
    for m in marker:
        m.hide_render = True
    hero = ru.camera("Hero", (1.9, -3.4, 1.6), target=(0, 0, 0.95), lens=50)
    rig.animation_data.action = actions["Idle"]
    bpy.context.scene.frame_set(1)
    ru.render(hero, os.path.join(outdir, "hero_34.png"))
    if full_set:
        ru.render(ru.camera("Front", (0, -3.9, 1.2), target=(0, 0, 0.95), lens=50), os.path.join(outdir, "front.png"))
        ru.render(ru.camera("Side", (3.9, 0, 1.2), target=(0, 0, 0.95), lens=50), os.path.join(outdir, "side.png"))
        ru.render(ru.camera("Back", (-1.6, 3.6, 1.5), target=(0, 0, 0.95), lens=50), os.path.join(outdir, "back_34.png"))
        ru.render(ru.camera("Face", (0.35, -1.2, 1.68), target=(0, 0, 1.62), lens=70), os.path.join(outdir, "face.png"))
    if full_set:
        for o, x in zip(lods, (-1.9, 0.0, 1.9)):
            o.hide_render = False
            o.parent = None
            o.modifiers["Armature"].show_render = False
            o.location.x = x
        ru.render(ru.camera("Lods", (0, -7.5, 1.6), target=(0, 0, 0.95), lens=35), os.path.join(outdir, "lods.png"))
        for o in lods:
            o.location.x = 0
            o.parent = rig
            o.modifiers["Armature"].show_render = True
        for o in lods[1:]:
            o.hide_render = True
    cam = ru.camera("Pose", (2.4, -3.0, 1.5), target=(0, 0, 0.9), lens=50)
    for state, frame in POSES:
        rig.animation_data.action = actions[state]
        bpy.context.scene.frame_set(frame)
        ru.render(cam, os.path.join(outdir, "pose_%s_f%02d.png" % (state, frame)))
    rest_pose(rig)


def report(lods, rig, actions):
    mesh = lods[0]
    bx, by, bz = mesh_bounds(mesh)
    t0 = mm.triangle_count(mesh)
    rep = {
        "asset_id": ID,
        "triangles": {o.name: mm.triangle_count(o) for o in lods},
        "lod_ratios": {o.name: round(mm.triangle_count(o) / t0, 3) for o in lods},
        "vertices": {o.name: len(o.data.vertices) for o in lods},
        "deform_bones": len(mm.DEFORM_BONES),
        "bone_names": mm.DEFORM_BONES,
        "materials": len(mesh.data.materials),
        "texture_size": mt.SIZE,
        "rest_bounds_m": {"width_x": round(bx[1] - bx[0], 3), "depth_y": round(by[1] - by[0], 3),
                          "height_z": round(bz[1], 3), "min_z": round(bz[0], 4)},
        "walk_speed_mps": round(ma.WALK_SPEED_MPS, 3),
        "clips": {},
    }
    for state, act in actions.items():
        last, loop = ma.CLIPS[state]
        rep["clips"]["A_%s_%s" % (ID, state)] = {"frames": last, "seconds": round((last - 1) / ma.FPS, 3), "loop": loop,
                                                  "min_deformed_z_m": round(ma.min_deformed_z(mesh, rig, act, last), 4)}
    return rep


def preview():
    reset_factory()
    lods, rig, actions, _ = build(os.path.join(OUT, "Textures"))
    rep = report(lods, rig, actions)
    renders(lods, rig, actions, os.path.join(OUT, "preview"), samples=12, res=(640, 480), full_set=True)
    print("REPORT", json.dumps(rep))


def full():
    reset_factory()
    asset = os.path.join(OUT, ID)
    models, anims, texdir, rdir = (os.path.join(asset, d) for d in ("Models", "Animations", "Textures", "renders"))
    for d in (models, anims, texdir, rdir):
        os.makedirs(d, exist_ok=True)
    lods, rig, actions, _ = build(texdir)
    rep = report(lods, rig, actions)
    turn_for_unity(rig, lods)
    export_model(rig, lods, os.path.join(models, "SK_Npc_%s.fbx" % ID))
    for state, act in actions.items():
        export_clip(rig, act, ma.CLIPS[state][0], os.path.join(anims, "A_%s_%s.fbx" % (ID, state)))
    turn_for_unity(rig, lods)
    rest_pose(rig)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, ID + "_work.blend"))
    renders(lods, rig, actions, rdir, samples=48, res=(1280, 960), full_set=True)
    with open(os.path.join(asset, "build_report.json"), "w") as f:
        json.dump(rep, f, indent=2)
    print("REPORT", json.dumps(rep))


{"preview": preview, "full": full}[STAGE]()
