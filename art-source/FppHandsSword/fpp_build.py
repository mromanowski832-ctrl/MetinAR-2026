"""METINAR-FPP-ART-001 — build entry point.

blender -b --factory-startup --python art-source/FppHandsSword/fpp_build.py -- <stage> <out_dir>
stages: checkpoint (geometry + rest rig + preview renders), full (textures, clips, FBX export, renders, report)
"""
import json
import math
import os
import sys

import bpy

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import fpp_contract as C  # noqa: E402
import fpp_mesh as fm  # noqa: E402
import fpp_model as M  # noqa: E402
import fpp_render as R  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
STAGE = argv[0] if argv else "checkpoint"
OUT = os.path.abspath(argv[1] if len(argv) > 1 else "/tmp/fpp-out")


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = C.FPS


def preview_material(name, color, metallic=0.0, rough=0.5):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Metallic"].default_value = metallic
    p.inputs["Roughness"].default_value = rough
    return mat


def socket_world(rig, name):
    """Contract (grip) frame of a socket bone in Blender space (bone matrix without C.NODE_FIX)."""
    return rig.matrix_world @ rig.pose.bones[name].matrix @ C.NODE_FIX_INV


def attach_sword_world(sword, rig):
    # the bone matrix IS the Blender-space grip frame (see fpp_model.build_armature), so the sword's
    # identity-local frame goes exactly there
    # identity-local frame goes exactly there; the model is drawn at C.SWORD_SCALE around the grip (as nested in
    # PF_Player_FppArms)
    sword.parent = None
    sword.matrix_world = socket_world(rig, "B_Socket_Sword") @ C.SWORD_SCALE_MATRIX


def checkpoint():
    reset()
    os.makedirs(OUT, exist_ok=True)
    sword, sockets = M.build_sword()
    arms = M.build_arms()
    rig = M.build_armature()
    M.bind(arms, rig)
    bpy.context.view_layer.update()
    attach_sword_world(sword, rig)
    sword.data.materials.append(preview_material("M_PrevSteel", (0.55, 0.58, 0.62), 0.9, 0.3))
    arms.data.materials.append(preview_material("M_PrevGlove", (0.10, 0.07, 0.05), 0.0, 0.55))
    report = {
        "triangles": {"SK_Player_FppArms": fm.triangle_count(arms), "SM_Weapon_FppSword": fm.triangle_count(sword)},
        "vertices": {"SK_Player_FppArms": len(arms.data.vertices), "SM_Weapon_FppSword": len(sword.data.vertices)},
        "bones": len(rig.data.bones),
        "shoulders_unity": {k: [round(v, 3) for v in M.SHOULDERS[k][0]] for k in M.SHOULDERS},
        "shoulder_reach_max_min": {k: [round(M.SHOULDERS[k][1], 3), round(M.SHOULDERS[k][2], 3)] for k in M.SHOULDERS},
    }
    if os.environ.get("FPP_NO_RENDER"):
        with open(os.path.join(OUT, "build_report_91.json"), "w") as fh:
            json.dump(report, fh, indent=2)
        return
    R.setup(samples=24)
    R.lights()
    R.render(R.fpp_camera(), os.path.join(OUT, "cp_fpp_rest.png"), R.A31_PORTRAIT_RES)
    R.render(R.camera("Side", (0.9, -0.35, 0.25), (0.05, 0.35, -0.2), 40), os.path.join(OUT, "cp_side.png"), (1000, 750))
    R.render(R.camera("Grip", (0.42, 0.18, -0.02), (0.18, 0.45, -0.19), 70), os.path.join(OUT, "cp_grip.png"), (1000, 750))
    with open(os.path.join(OUT, "checkpoint_report.json"), "w") as f:
        json.dump(report, f, indent=2)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "fpp_checkpoint.blend"))
    print("REPORT", json.dumps(report))


if STAGE == "checkpoint":
    checkpoint()


# ---------------------------------------------------------------- full build
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


def vertex_sets(arms):
    names = {g.index: g.name for g in arms.vertex_groups}
    sets = {1: [], -1: []}
    for v in arms.data.vertices:
        w = {1: 0.0, -1: 0.0}
        for g in v.groups:
            n = names[g.group]
            if n.startswith("B_Hand_") or any(n.startswith("B_" + f) for f in ("Index", "Middle", "Ring", "Pinky", "Thumb")):
                w[1 if n.endswith("_R") else -1] += g.weight
        for side in (1, -1):
            if w[side] >= 0.5:
                sets[side].append(v.index)
    return sets


def sword_contacts(arms, rig, actions, step=1):
    """Deformed hand vertices expressed in sword-model units (RightGrip frame / SWORD_SCALE) for every `step`-th frame."""
    from mathutils import Vector
    sets = vertex_sets(arms)
    result = {}
    for state, act in actions.items():
        rig.animation_data.action = act
        frames = int(act.frame_range[1])
        worst = {"hilt_min_clearance_m": {1: 9.0, -1: 9.0}, "penetrating_vertices": 0, "nearest_camera_m": 9.0}
        for f in range(1, frames + 1, step):
            bpy.context.scene.frame_set(f)
            dg = bpy.context.evaluated_depsgraph_get()
            ev = arms.evaluated_get(dg)
            me = ev.to_mesh()
            inv = (socket_world(rig, "B_Socket_Sword") @ C.SWORD_SCALE_MATRIX).inverted()  # -> sword-model units
            grip_l = C.left_grip_blend("Aura", (f - 1) / C.FPS) if state == "AuraActivate" else 0.0
            for side in (1, -1):
                for i in sets[side]:
                    wpos = ev.matrix_world @ me.vertices[i].co
                    lu = C.S @ (inv @ wpos)  # Unity sword-local
                    r = math.hypot(lu.x, lu.z)
                    if M.HILT_BOTTOM <= lu.y <= M.HILT_TOP and r < 0.06 and (side > 0 or grip_l >= 0.5):
                        worst["hilt_min_clearance_m"][side] = min(worst["hilt_min_clearance_m"][side],
                                                                  (r - M.HILT_RADIUS) * C.SWORD_SCALE)  # metres
                    inside = (M.HILT_BOTTOM <= lu.y <= M.HILT_TOP and r < M.HILT_RADIUS) or \
                             (abs(lu.x) < 0.11 and abs(lu.y - M.GUARD_Y) < 0.010 and abs(lu.z) < 0.012) or \
                             (0.13 <= lu.y <= 0.80 and abs(lu.x) < 0.026 and abs(lu.z) < 0.004) or \
                             (math.hypot(lu.x, lu.z) < 0.026 and abs(lu.y - M.POMMEL_Y) < 0.018)
                    worst["penetrating_vertices"] += int(inside)
            for v in me.vertices:
                wp = ev.matrix_world @ v.co
                if wp.y > 0:  # in front of the camera (Blender +Y = Unity +Z)
                    worst["nearest_camera_m"] = min(worst["nearest_camera_m"], wp.length)
            ev.to_mesh_clear()
        worst["hilt_min_clearance_m"] = {("R" if k > 0 else "L"): round(v, 4) for k, v in worst["hilt_min_clearance_m"].items()}
        worst["nearest_camera_m"] = round(worst["nearest_camera_m"], 3)
        result[state] = worst
    return result


def socket_samples(rig, actions):
    """Max deviation of the baked sockets from the PR #32 pose functions (Blender side of the check)."""
    out = {}
    for state, act in actions.items():
        code_action = {"Idle": None, "Attack": "Attack", "AuraActivate": "Aura"}[state]
        if code_action is None:
            continue
        rig.animation_data.action = act
        dp = dr = dpl = drl = 0.0
        for f in range(1, int(act.frame_range[1]) + 1):
            bpy.context.scene.frame_set(f)
            t = (f - 1) / C.FPS
            for bone, fn, store in (("B_Socket_Sword", C.sword_pose, "R"), ("B_Socket_GripL", C.left_hand_pose, "L")):
                m = socket_world(rig, bone)
                pos, rot = fn(code_action, t)
                e = C.to_blender_matrix(pos, rot)
                d_pos = (m.translation - e.translation).length
                d_rot = math.degrees(m.to_quaternion().rotation_difference(e.to_quaternion()).angle)
                if store == "R":
                    dp, dr = max(dp, d_pos), max(dr, d_rot)
                else:
                    dpl, drl = max(dpl, d_pos), max(drl, d_rot)
        out[state] = {"right_pos_m": round(dp, 5), "right_rot_deg": round(dr, 3), "left_pos_m": round(dpl, 5), "left_rot_deg": round(drl, 3)}
    return out


def hilt_left_clearance(rig, act):
    """Min distance (m) from the baked B_Socket_GripL to the baked drawn hilt segment over the Attack clip."""
    from mathutils import Vector
    rig.animation_data.action = act
    a_m = Vector((0.0, C.HILT_SEGMENT_MODEL_Y[0], 0.0)) * C.SWORD_SCALE
    b_m = Vector((0.0, C.HILT_SEGMENT_MODEL_Y[1], 0.0)) * C.SWORD_SCALE
    best = (9.0, 0.0)
    for f in range(1, int(act.frame_range[1]) + 1):
        bpy.context.scene.frame_set(f)
        g = socket_world(rig, "B_Socket_Sword")
        lp = socket_world(rig, "B_Socket_GripL").translation
        a, b = g @ C.to_blender_point(a_m), g @ C.to_blender_point(b_m)
        ab = b - a
        k = min(1.0, max(0.0, (lp - a).dot(ab) / ab.dot(ab)))
        d = (a + ab * k - lp).length
        if d < best[0]:
            best = (d, (f - 1) / C.FPS)
    return {"min_m": round(best[0], 4), "at_s": round(best[1], 2), "required_m": C.MIN_HILT_LEFT_CLEARANCE_M,
            "contract_min_m": round(min(C.hilt_left_distance("Attack", f / 1000.0) for f in range(721)), 4)}


def full():
    reset()
    root = os.path.join(OUT, "Assets")
    arms_dir = os.path.join(root, "Characters", "Player", "FppArms")
    sword_dir = os.path.join(root, "Weapons", "FppSword")
    renders = os.path.join(OUT, "renders")
    for d in (renders, os.path.join(arms_dir, "Models"), os.path.join(arms_dir, "Animations"), os.path.join(sword_dir, "Models")):
        os.makedirs(d, exist_ok=True)
    import fpp_anim as A
    import fpp_texture as T

    sword, sockets = M.build_sword()
    arms = M.build_arms()
    T.unwrap(sword)
    T.unwrap(arms)
    sword_tex = T.make_textures(sword, "FppSword", os.path.join(sword_dir, "Textures"), T.paint_sword, pos_scale=1.0 / 0.95,
                                bump=(0.35, 0.0015))
    arms_tex = T.make_textures(arms, "FppArms", os.path.join(arms_dir, "Textures"), T.paint_arms, pos_scale=1.0 / 0.8,
                               bump=(0.45, 0.0015))
    m_sword = T.final_material("M_FppSword", sword_tex, emission_strength=2.0)
    m_arms = T.final_material("M_FppArms", arms_tex)
    sword.data.materials.append(m_sword)
    arms.data.materials.append(m_arms)

    rig = M.build_armature()
    M.bind(arms, rig)
    bpy.context.view_layer.update()
    actions, clip_stats, signs = A.build_actions(rig, arms)

    report = {
        "triangles": {"SK_Player_FppArms": fm.triangle_count(arms), "SM_Weapon_FppSword": fm.triangle_count(sword)},
        "vertices": {"SK_Player_FppArms": len(arms.data.vertices), "SM_Weapon_FppSword": len(sword.data.vertices)},
        "bones": len(rig.data.bones), "bone_names": [b.name for b in rig.data.bones],
        "materials": {"SK_Player_FppArms": 1, "SM_Weapon_FppSword": 1}, "texture_size": T.SIZE,
        "textures": {"FppArms": sorted(arms_tex), "FppSword": sorted(sword_tex)},
        "contract_id": C.CONTRACT_ID, "sword_scale": C.SWORD_SCALE,
        "contract_unity": {"rest": [list(C.REST_POSITION), list(C.REST_EULER)], "aura_hold": [list(C.AURA_POSE[0]), list(C.AURA_POSE[1])],
                           "left_rest": [list(C.LEFT_REST), list(C.LEFT_REST_EULER)],
                           "hit_blade_base": list(C.BLADE_BASE), "hit_blade_tip": list(C.BLADE_TIP), "left_grip": list(C.LEFT_GRIP_OFFSET)},
        "sword_sockets_unity_model": {k: list(v) for k, v in M.SWORD_SOCKETS.items()},
        "shoulders_unity": {("R" if k > 0 else "L"): [round(v, 3) for v in M.SHOULDERS[k][0]] for k in M.SHOULDERS},
        "upper_forearm_len_m": [M.UPPER_LEN, M.FORE_LEN], "hand_yaw_deg": M.HAND_YAW_DEG,
        "fps": C.FPS, "clips": clip_stats,
    }
    report["socket_vs_pr32_blender"] = socket_samples(rig, actions)
    report["hand_sword_contact"] = sword_contacts(arms, rig, actions)
    report["attack_hilt_left_socket"] = hilt_left_clearance(rig, actions["Attack"])
    report["left_attack_dodge"] = {"offset": list(C.LEFT_ATTACK_DODGE), "times_s": list(C.LEFT_ATTACK_DODGE_TIMES)}
    report["grip_tighten_m"] = round(M.GRIP_TIGHTEN, 5)

    # ---- export
    A.rest_pose(rig)
    rig.animation_data.action = None
    A.rest_pose(rig)
    select_only([rig, arms])
    bpy.ops.export_scene.fbx(filepath=os.path.join(arms_dir, "Models", "SK_Player_FppArms.fbx"), object_types={"ARMATURE", "MESH"},
                             bake_anim=False, mesh_smooth_type="OFF", use_triangles=True, use_tspace=False, use_mesh_modifiers=False,
                             **FBX_COMMON)
    sc = bpy.context.scene
    for state, act in actions.items():
        A.rest_pose(rig)
        rig.animation_data.action = act
        sc.frame_start, sc.frame_end = 1, int(act.frame_range[1])
        select_only([rig])
        bpy.ops.export_scene.fbx(filepath=os.path.join(arms_dir, "Animations", "A_FppArms_%s.fbx" % state), object_types={"ARMATURE"},
                                 bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                                 bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                                 bake_anim_simplify_factor=0.0, **FBX_COMMON)
    rig.animation_data.action = None
    A.rest_pose(rig)
    select_only([sword] + sockets)
    bpy.ops.export_scene.fbx(filepath=os.path.join(sword_dir, "Models", "SM_Weapon_FppSword.fbx"), object_types={"MESH", "EMPTY"},
                             bake_anim=False, mesh_smooth_type="OFF", use_triangles=True, use_tspace=False, use_mesh_modifiers=False,
                             **FBX_COMMON)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "FppHandsSword_work.blend"))

    # ---- renders (preview only): sword follows the socket bone per frame
    R.setup(samples=28)
    R.lights()
    cam = R.fpp_camera()
    cam_land = R.fpp_camera("FPP_Landscape", vfov=34.6)
    side = R.camera("Side", (0.95, -0.30, 0.20), (0.02, 0.40, -0.15), 38)
    close = R.camera("Grip", (0.31, 0.15, -0.01), (0.08, 0.44, -0.16), 65)
    shots = [("Idle", 1), ("Attack", 19), ("Attack", 31), ("Attack", 43), ("Attack", 61), ("AuraActivate", 40), ("AuraActivate", 66),
             ("AuraActivate", 101)]
    for state, f in shots:
        A.rest_pose(rig)
        rig.animation_data.action = actions[state]
        sc.frame_set(f)
        attach_sword_world(sword, rig)
        tag = "%s_f%03d" % (state, f - 1)
        R.render(cam, os.path.join(renders, "fpp_portrait_%s.png" % tag), R.A31_PORTRAIT_RES)
        R.render(cam_land, os.path.join(renders, "fpp_landscape_%s.png" % tag), (1200, 540))
        R.render(side, os.path.join(renders, "side_%s.png" % tag), (960, 720))
    for state, f in (("Idle", 1), ("AuraActivate", 66)):
        rig.animation_data.action = actions[state]
        sc.frame_set(f)
        attach_sword_world(sword, rig)
        R.render(close, os.path.join(renders, "grip_%s_f%03d.png" % (state, f - 1)), (960, 720))

    with open(os.path.join(OUT, "build_report.json"), "w") as fh:
        json.dump(report, fh, indent=2)
    print("REPORT", json.dumps(report))


if STAGE == "full":
    full()


# ---------------------------------------------------------------- #91 final build (gear arms, 3 weapons, all clips)
WEAPONS_91 = [  # (folder/asset id, builder, painter scale)
    ("FppSword", "build_sword"),
    ("FppSword02", "build_sword_b"),
    ("FppGreatsword", "build_greatsword"),
]
STATE_WEAPON_91 = {"1H": "SM_Weapon_FppSword", "2H": "SM_Weapon_FppGreatsword"}
BUDGET_PLAYER = {"triangles": 10000, "bones": 40, "materials": 2, "texture": 1024}  # AssetPipelineRules Player
BUDGET_WEAPON = {"triangles": 3000, "bones": 0, "materials": 1, "texture": 1024}    # AssetPipelineRules Weapon


def left_gripping(state, t):
    import fpp_combo as K
    if state in ("AuraActivate", K.AURA_1H):
        return C.left_grip_blend("Aura", t) >= 0.5
    if K.weapon_of(state) == "2H":
        return True
    return False


def contacts_91(arms, rig, sword_objs, actions):
    """Per clip: hand vertices inside the weapon proxies (both hands), and the left-socket distance to the drawn hilt
    on frames where the left hand is NOT on the hilt (left_grip_blend == 0 / buckler hand / V3 Attack)."""
    import fpp_anim as A
    import fpp_combo as K
    import fpp_gear as G
    from mathutils import Vector
    sets = A.hand_vertex_sets(arms)
    out = {}
    for state, act in actions.items():
        weapon = STATE_WEAPON_91[K.weapon_of(state)] if state not in ("Idle", "Attack", "AuraActivate") else "SM_Weapon_FppSword"
        hb, ht, _ = G.PROXIES[weapon]["hilt"]
        A.rest_pose(rig)
        rig.animation_data.action = act
        pen = {"R": 0, "L": 0}
        free = (9.0, None)
        for f in range(1, int(act.frame_range[1]) + 1):
            t = (f - 1) / C.FPS
            bpy.context.scene.frame_set(f)
            g = socket_world(rig, "B_Socket_Sword") @ C.SWORD_SCALE_MATRIX
            inv = g.inverted()
            ev = arms.evaluated_get(bpy.context.evaluated_depsgraph_get())
            me = ev.to_mesh()
            for side, key in ((1, "R"), (-1, "L")):
                for i in sets[side]:
                    if G.proxy_hit(weapon, C.S @ (inv @ (ev.matrix_world @ me.vertices[i].co))):
                        pen[key] += 1
            ev.to_mesh_clear()
            if not left_gripping(state, t) and (state not in ("AuraActivate", K.AURA_1H) or C.left_grip_blend("Aura", t) <= 1e-6):
                a = g @ C.to_blender_point(Vector((0.0, hb, 0.0)))
                b = g @ C.to_blender_point(Vector((0.0, ht, 0.0)))
                lp = socket_world(rig, "B_Socket_GripL").translation
                ab = b - a
                k = min(1.0, max(0.0, (lp - a).dot(ab) / ab.dot(ab)))
                d = (a + ab * k - lp).length
                if d < free[0]:
                    free = (d, round(t, 2))
        out[state] = {"weapon": weapon, "penetrating_vertices": pen,
                      "left_free_min_hilt_distance_m": None if free[1] is None else round(free[0], 4),
                      "left_free_min_at_s": free[1]}
    return out


def final91():
    import fpp_anim as A
    import fpp_buckler as BK
    import fpp_combo as K
    import fpp_gear as G
    import fpp_texture as T
    reset()
    root = os.path.join(OUT, "Assets")
    arms_dir = os.path.join(root, "Characters", "Player", "FppArms")
    renders = os.path.join(OUT, "renders")
    for d in (renders, os.path.join(arms_dir, "Models"), os.path.join(arms_dir, "Animations")):
        os.makedirs(d, exist_ok=True)
    report = {"issue": "#91", "contract_id": C.CONTRACT_ID, "sword_scale": C.SWORD_SCALE, "fps": C.FPS,
              "triangles": {}, "vertices": {}, "materials": {}, "textures": {}, "budget": {}}

    # ---- weapons: build, texture, export one at a time (socket empty names must be unique in the scene)
    swords = {}
    for wid, fn in WEAPONS_91:
        wdir = os.path.join(root, "Weapons", wid)
        os.makedirs(os.path.join(wdir, "Models"), exist_ok=True)
        obj, empties = getattr(G, fn)()
        T.unwrap(obj)
        tex = T.make_textures(obj, wid, os.path.join(wdir, "Textures"), T.paint_sword, pos_scale=1.0 / 0.95, bump=(0.35, 0.0015))
        obj.data.materials.append(T.final_material("M_" + wid, tex, emission_strength=2.0))
        select_only([obj] + empties)
        bpy.ops.export_scene.fbx(filepath=os.path.join(wdir, "Models", obj.name + ".fbx"), object_types={"MESH", "EMPTY"},
                                 bake_anim=False, mesh_smooth_type="OFF", use_triangles=True, use_tspace=False,
                                 use_mesh_modifiers=False, **FBX_COMMON)
        for e in empties:
            e.name = e.name + "__" + wid
        swords[obj.name] = obj
        tris = fm.triangle_count(obj)
        report["triangles"][obj.name] = tris
        report["vertices"][obj.name] = len(obj.data.vertices)
        report["materials"][obj.name] = 1
        report["textures"][wid] = sorted(os.path.basename(p) for p in tex.values())
        report["budget"][obj.name] = {"triangles": [tris, BUDGET_WEAPON["triangles"]], "materials": [1, 1],
                                      "texture": [T.SIZE, BUDGET_WEAPON["texture"]],
                                      "ok": tris <= BUDGET_WEAPON["triangles"]}

    # ---- arms: glove + sleeve textured separately, joined into one skinned mesh with two material slots
    glove, sleeve = G.build_arms_parts()
    T.unwrap(glove)
    T.unwrap(sleeve)
    gtex = T.make_textures(glove, "FppArms_" + G.GLOVE_ID, os.path.join(arms_dir, "Textures"), T.paint_arms, pos_scale=1.0 / 0.8,
                           bump=(0.45, 0.0015))
    stex = T.make_textures(sleeve, "FppArms", os.path.join(arms_dir, "Textures"), T.paint_arms, pos_scale=1.0 / 0.8,
                           bump=(0.45, 0.0015))
    glove.data.materials.append(T.final_material("M_FppArms_" + G.GLOVE_ID, gtex, emission_strength=1.5))
    sleeve.data.materials.append(T.final_material("M_FppArms", stex))
    tri_glove, tri_sleeve = fm.triangle_count(glove), fm.triangle_count(sleeve)
    select_only([glove, sleeve])
    bpy.ops.object.join()
    arms = glove
    arms.name = "SK_Player_FppArms"
    arms.data.name = "SK_Player_FppArms"
    rig = M.build_armature()
    M.bind(arms, rig)
    bpy.context.view_layer.update()
    tris = fm.triangle_count(arms)
    report["triangles"]["SK_Player_FppArms"] = tris
    report["triangles_parts"] = {"Glove_" + G.GLOVE_ID: tri_glove, "Sleeve": tri_sleeve}
    report["vertices"]["SK_Player_FppArms"] = len(arms.data.vertices)
    report["materials"]["SK_Player_FppArms"] = [m.name for m in arms.data.materials]
    report["textures"]["FppArms"] = sorted(os.path.basename(p) for p in list(gtex.values()) + list(stex.values()))
    report["bones"] = len(rig.data.bones)
    report["bone_names"] = [b.name for b in rig.data.bones]
    report["budget"]["SK_Player_FppArms"] = {"triangles": [tris, BUDGET_PLAYER["triangles"]], "bones": [len(rig.data.bones), BUDGET_PLAYER["bones"]],
                                             "materials": [len(arms.data.materials), BUDGET_PLAYER["materials"]],
                                             "texture": [T.SIZE, BUDGET_PLAYER["texture"]],
                                             "ok": tris <= BUDGET_PLAYER["triangles"] and len(rig.data.bones) <= BUDGET_PLAYER["bones"]
                                             and len(arms.data.materials) <= BUDGET_PLAYER["materials"]}

    # ---- clips
    actions, clip_stats, signs = A.build_actions(rig, arms)
    report["clips"] = clip_stats
    report["socket_vs_pr32_blender"] = socket_samples(rig, {k: actions[k] for k in ("Idle", "Attack", "AuraActivate")})
    report["attack_hilt_left_socket"] = hilt_left_clearance(rig, actions["Attack"])
    report["contacts"] = contacts_91(arms, rig, swords, actions)
    sword1 = swords["SM_Weapon_FppSword"]
    bstates = [s for s in K.all_states() if K.has_buckler(s)]
    import fpp_render as Rr
    asp = Rr.A31_PORTRAIT_RES[0] / Rr.A31_PORTRAIT_RES[1]
    buck = {}
    for margin in (0.0, 0.02):
        r = BK.check_states(rig, arms, sword1, actions, bstates, attach_sword_world, margin, Rr.A31_PORTRAIT_VFOV, asp)
        for st in r["states"].values():
            st.pop("hit_frames", None)
        buck["margin_%dmm" % round(margin * 1000)] = r
    report["buckler_check"] = buck

    # ---- clip reference (Unity builder/check + #82 proposal). JsonUtility layout (lists, no dicts):
    # per clip the baked socket frames in prefab-root (= AR camera) Unity space, every frame, for the Unity
    # import check (FppArtAssetCheck compares the imported clips against these: <= 2 mm / 1 deg).
    def unity_frame(m):
        p = C.S @ m.translation
        q = (C.S @ m.to_3x3() @ C.S).to_quaternion()
        return [round(p.x, 5), round(p.y, 5), round(p.z, 5), round(q.x, 6), round(q.y, 6), round(q.z, 6), round(q.w, 6)]

    ref = {"contract_id": C.CONTRACT_ID, "fps": C.FPS,
           "buckler_asset": "PF_Weapon_StarterBuckler (PR #63, referenced, not part of this branch); child of B_Socket_ShieldL, identity, scale 1",
           "clips": []}
    v3 = ("Idle", "Attack", "AuraActivate")
    for st, s in clip_stats.items():
        act = actions[st]
        A.rest_pose(rig)
        rig.animation_data.action = act
        samples = []
        for f in range(1, int(act.frame_range[1]) + 1):
            sc0 = bpy.context.scene
            sc0.frame_set(f)
            samples.append({"t": round((f - 1) / C.FPS, 4), "sword": unity_frame(socket_world(rig, "B_Socket_Sword")),
                            "gripL": unity_frame(socket_world(rig, "B_Socket_GripL")),
                            "shieldL": unity_frame(socket_world(rig, "B_Socket_ShieldL"))})
        ref["clips"].append({"state": st, "file": "A_FppArms_%s.fbx" % st, "frames": s["frames"], "seconds": s["seconds"],
                             "loop": s["loop"], "weapon": s["weapon"],
                             "buckler": (st not in v3) and K.has_buckler(st),
                             "leftGrips": st in ("AuraActivate", K.AURA_1H) or (st not in v3 and K.weapon_of(st) == "2H"),
                             "events": [{"id": e, "time": round(t, 4)} for e, t in s["events"]],
                             "samples": samples})
    with open(os.path.join(OUT, "clip_reference.json"), "w") as fh:
        json.dump(ref, fh, separators=(",", ":"))

    # ---- export arms + clips
    A.rest_pose(rig)
    rig.animation_data.action = None
    A.rest_pose(rig)
    select_only([rig, arms])
    bpy.ops.export_scene.fbx(filepath=os.path.join(arms_dir, "Models", "SK_Player_FppArms.fbx"), object_types={"ARMATURE", "MESH"},
                             bake_anim=False, mesh_smooth_type="OFF", use_triangles=True, use_tspace=False, use_mesh_modifiers=False,
                             **FBX_COMMON)
    sc = bpy.context.scene
    for state, act in actions.items():
        A.rest_pose(rig)
        rig.animation_data.action = act
        sc.frame_start, sc.frame_end = 1, int(act.frame_range[1])
        select_only([rig])
        bpy.ops.export_scene.fbx(filepath=os.path.join(arms_dir, "Animations", "A_FppArms_%s.fbx" % state), object_types={"ARMATURE"},
                                 bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                                 bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
                                 bake_anim_simplify_factor=0.0, **FBX_COMMON)
    rig.animation_data.action = None
    A.rest_pose(rig)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "FppHandsSword91_work.blend"))

    # ---- renders (preview): weapon per clip, optional #63 buckler reference on B_Socket_ShieldL
    buckler = None
    bdir = os.environ.get("FPP_BUCKLER_MODEL")
    if bdir:
        from mathutils import Matrix
        sys.path.insert(0, bdir)
        import buckler_model
        buckler = buckler_model.build()
        buckler.data.transform(Matrix.Rotation(math.pi, 4, "Z"))
        buckler.data.materials.clear()
        buckler.data.materials.append(preview_material("M_PrevBuckler_ref63", (0.33, 0.21, 0.11), 0.0, 0.6))
    if os.environ.get("FPP_NO_RENDER"):
        with open(os.path.join(OUT, "build_report_91.json"), "w") as fh:
            json.dump(report, fh, indent=2)
        return
    R.setup(samples=24)
    R.lights()
    cam = R.fpp_camera()
    shots = []
    for st, s in clip_stats.items():
        n = 3 if s["frames"] > 40 else 2
        shots += [(st, 1 + round((s["frames"] - 1) * i / (n - 1))) for i in range(n)]
    for state, f in shots:
        A.rest_pose(rig)
        rig.animation_data.action = actions[state]
        sc.frame_set(f)
        two = K.weapon_of(state) == "2H" and state not in ("Idle", "Attack", "AuraActivate")
        for name, o in swords.items():
            o.hide_render = name != ("SM_Weapon_FppGreatsword" if two else "SM_Weapon_FppSword")
        attach_sword_world(swords["SM_Weapon_FppGreatsword" if two else "SM_Weapon_FppSword"], rig)
        if buckler is not None:
            buckler.hide_render = state in ("Idle", "Attack", "AuraActivate") or not K.has_buckler(state)
            buckler.matrix_world = socket_world(rig, "B_Socket_ShieldL")
        R.render(cam, os.path.join(renders, "fpp_portrait_%s_f%03d.png" % (state, f - 1)), R.A31_PORTRAIT_RES)
    # second sword: idle + one attack frame
    for state, f in (("ShieldIdle", 1), ("Attack1H_03", 31)):
        A.rest_pose(rig)
        rig.animation_data.action = actions[state]
        sc.frame_set(f)
        for name, o in swords.items():
            o.hide_render = name != "SM_Weapon_FppSword02"
        attach_sword_world(swords["SM_Weapon_FppSword02"], rig)
        if buckler is not None:
            buckler.hide_render = False
            buckler.matrix_world = socket_world(rig, "B_Socket_ShieldL")
        R.render(cam, os.path.join(renders, "fpp_portrait_Sword02_%s_f%03d.png" % (state, f - 1)), R.A31_PORTRAIT_RES)

    with open(os.path.join(OUT, "build_report_91.json"), "w") as fh:
        json.dump(report, fh, indent=2)
    print("REPORT91", json.dumps({k: report[k] for k in ("triangles", "budget", "bones")}))


if STAGE == "final91":
    final91()
