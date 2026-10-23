"""#91 motion preview (fast, preview materials, no texture bake, no finger fit): portrait FPP frames per clip.

blender -b --factory-startup --python art-source/FppHandsSword/fpp_motion_preview.py -- <out_dir> [state,state,...] [frames_per_clip]

FPP_BUCKLER_MODEL=<dir containing PR #63 buckler_model.py> shows the referenced buckler (PR #63, not part of this
branch; read-only) on B_Socket_ShieldL in every buckler state. FPP_SWORD_1H=B previews the second 1H sword (SM_Weapon_FppSword02).
"""
import os
import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import fpp_anim as A  # noqa: E402
import fpp_build as B  # noqa: E402  (stage "none" -> only helpers)
import fpp_combo as K  # noqa: E402
import fpp_contract as C  # noqa: E402
import fpp_gear as G  # noqa: E402
import fpp_model as M  # noqa: E402
import fpp_render as R  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else "/tmp/fpp-motion")
STATES = argv[1].split(",") if len(argv) > 1 and argv[1] != "all" else list(A.CLIPS)
N = int(argv[2]) if len(argv) > 2 else 7
os.makedirs(OUT, exist_ok=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = C.FPS
    sword, _ = (G.build_sword_b if os.environ.get("FPP_SWORD_1H") == "B" else G.build_sword)()  # FPP_SWORD_1H=B -> SM_Weapon_FppSword02
    gsword, _ = G.build_greatsword()
    glove, sleeve = G.build_arms_parts()
    glove.data.materials.append(B.preview_material("M_PrevGlove", (0.10, 0.07, 0.05), 0.0, 0.55))
    sleeve.data.materials.append(B.preview_material("M_PrevArms", (0.30, 0.10, 0.08), 0.0, 0.7))
    bpy.ops.object.select_all(action="DESELECT")
    glove.select_set(True)
    sleeve.select_set(True)
    bpy.context.view_layer.objects.active = glove
    bpy.ops.object.join()
    arms = glove
    arms.name = "SK_Player_FppArms"
    rig = M.build_armature()
    M.bind(arms, rig)
    sword.data.materials.append(B.preview_material("M_PrevSteel", (0.55, 0.58, 0.62), 0.9, 0.3))
    gsword.data.materials.append(B.preview_material("M_PrevSteel2", (0.50, 0.52, 0.58), 0.9, 0.3))
    buckler = None
    bdir = os.environ.get("FPP_BUCKLER_MODEL")
    if bdir:
        import math
        from mathutils import Matrix
        sys.path.insert(0, bdir)
        import buckler_model  # PR #63 (reference only)
        buckler = buckler_model.build()
        buckler.data.transform(Matrix.Rotation(math.pi, 4, "Z"))  # as PR #63 buckler_build (front -> Unity +Z)
        buckler.data.materials.clear()
        buckler.data.materials.append(B.preview_material("M_PrevBuckler", (0.35, 0.22, 0.12), 0.0, 0.6))
    bpy.context.view_layer.update()
    actions, stats, _ = A.build_actions(rig, None, STATES, fit=False)
    R.setup(samples=8)
    R.lights()
    cam = R.fpp_camera()
    sc = bpy.context.scene
    for st in STATES:
        act = actions[st]
        frames = int(act.frame_range[1])
        two = K.weapon_of(st) == "2H"
        use, other = (gsword, sword) if two else (sword, gsword)
        other.hide_render = True
        use.hide_render = False
        for i in range(N):
            f = 1 + round((frames - 1) * i / (N - 1))
            A.rest_pose(rig)
            rig.animation_data.action = act
            sc.frame_set(f)
            B.attach_sword_world(use, rig)
            if buckler is not None:
                buckler.hide_render = not K.has_buckler(st)
                buckler.matrix_world = B.socket_world(rig, "B_Socket_ShieldL")
            R.render(cam, os.path.join(OUT, "%s_%02d_f%03d.png" % (st, i, f - 1)), (270, 600))
    print("STATS", stats)


main()
