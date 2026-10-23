"""#91 / K6 — buckler (Lipowy Puklerz, PR #63 SM_Weapon_StarterBuckler @ 1dd895a) clipping check.

The buckler asset belongs to PR #63 and is NOT part of this branch (referenced, not modified). For the check its
solid is rebuilt analytically from the #63 dimensions (buckler_model.py: R_SHIELD 0.170, R_EDGE 0.172, rim band
+0.009, BACK_D 0.035, THICK 0.012, DOME 0.018, BOSS_R 0.064, BOSS_H 0.048, handle tube r 0.0135 on HANDLE_PATH),
in the B_Socket_ShieldL frame (= #63 pivot: fist-grip centre, handle +Y, front +Z, Unity metres). The analytic solid
is slightly larger than the mesh (rim/plank margins), so it is conservative.

Checks per frame of every buckler state (K.has_buckler):
- right arm + right hand vertices, sword vertices and dense blade/hilt samples inside the buckler  -> must be 0
- left forearm / upper-arm vertices inside the buckler                                            -> must be 0
- left hand vertices inside planks/boss/rim (the fist sits behind the planks, around the handle)  -> must be 0
- left hand vs handle tube only while the buckler is parked on the forearm (Aura 1H)               -> must be 0
"""
import math

import bpy
from mathutils import Vector

import fpp_combo as K
import fpp_contract as C
import fpp_gear as G

R_SHIELD, R_EDGE, RIM_OUT = 0.170, 0.172, 0.009
BACK_D, THICK, DOME = 0.035, 0.012, 0.018
BOSS_R, BOSS_H = 0.064, 0.048
HANDLE_R = 0.0135
SOURCE = "PR #63 feat/032-starter-buckler-mesh @ 1dd895a (art-source/StarterBuckler/buckler_model.py dimensions)"


def d_front(r):
    return BACK_D + THICK + DOME * (1.0 - (min(r, R_SHIELD) / R_SHIELD) ** 2)


def d_back(r):
    return BACK_D + DOME * (1.0 - (min(r, R_SHIELD) / R_SHIELD) ** 2)


# handle centre line in the socket frame (Unity: y along the handle, z = distance in front of the grip)
HANDLE_PATH = [Vector((0.0, y, d)) for y, d in ((-0.082, d_back(0.08) + 0.004), (-0.068, 0.018), (-0.042, 0.0), (0.0, 0.0),
                                                (0.042, 0.0), (0.068, 0.018), (0.082, d_back(0.08) + 0.004))]


def _seg_dist(p, a, b):
    ab = b - a
    k = min(1.0, max(0.0, (p - a).dot(ab) / ab.dot(ab)))
    return (a + ab * k - p).length


def part_at(lu, margin=0.0):
    """Buckler part containing the socket-local Unity point lu ('planks', 'rim', 'boss', 'handle') or None."""
    r = math.hypot(lu.x, lu.y)
    d = lu.z
    if r <= R_EDGE + RIM_OUT + margin and d_back(r) - 0.004 - margin <= d <= d_front(r) + 0.004 + margin:
        return "planks" if r <= R_SHIELD else "rim"
    if r <= BOSS_R + 0.012 + margin:
        base = d_front(BOSS_R)
        top = base + BOSS_H * math.sqrt(max(0.0, 1.0 - (r / (BOSS_R + 0.012)) ** 2))
        if d_front(r) - margin <= d <= top + margin:
            return "boss"
    if abs(lu.y) <= 0.09 and d <= d_back(0.08) + 0.01:
        if min(_seg_dist(lu, HANDLE_PATH[i], HANDLE_PATH[i + 1]) for i in range(len(HANDLE_PATH) - 1)) < HANDLE_R + margin:
            return "handle"
    return None


def _socket_inv(rig):
    """Blender world -> socket-local Unity (metres)."""
    m = rig.matrix_world @ rig.pose.bones["B_Socket_ShieldL"].matrix @ C.NODE_FIX_INV
    inv = m.inverted()
    return lambda pb: C.S @ (inv @ pb)


def sword_samples(weapon):
    """Dense points on the weapon proxies (model-local Unity, scale 1): blade faces/edges, hilt, guard, pommel."""
    p = G.PROXIES[weapon]
    pts = []
    by0, by1, bx, bz = p["blade"]
    n = int((by1 - by0) / 0.008)
    for i in range(n + 1):
        y = by0 + (by1 - by0) * i / n
        for x in (-bx, -bx * 0.5, 0.0, bx * 0.5, bx):
            for z in (-bz, bz):
                pts.append(Vector((x, y, z)))
    hb, ht, hr = p["hilt"]
    for i in range(13):
        y = hb + (ht - hb) * i / 12
        for k in range(8):
            a = 2 * math.pi * k / 8
            pts.append(Vector((hr * math.cos(a), y, hr * math.sin(a))))
    gx, gy0, gy1, gz = p["guard"]
    for i in range(15):
        x = -gx + 2 * gx * i / 14
        for y in (gy0, gy1 if abs(x) < 0.04 else gy0 + 0.02):
            for z in (-gz, gz):
                pts.append(Vector((x, y, z)))
    py0, py1, pr = p["pommel"]
    for y in (py0, py1):
        for k in range(8):
            a = 2 * math.pi * k / 8
            pts.append(Vector((pr * math.cos(a), y, pr * math.sin(a))))
    return pts


def vertex_classes(arms):
    """{'R': [...], 'L_hand': [...], 'L_arm': [...]} vertex indices by dominant bone side/part."""
    names = {g.index: g.name for g in arms.vertex_groups}
    out = {"R": [], "L_hand": [], "L_arm": []}
    for v in arms.data.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        if best is None:
            continue
        n = names[best.group]
        if n.endswith("_R"):
            out["R"].append(v.index)
        elif n.endswith("_L"):
            out["L_arm" if ("Forearm" in n or "UpperArm" in n) else "L_hand"].append(v.index)
    return out


def check_frame(rig, arms, sword, weapon, classes, hang, margin=0.0):
    to_s = _socket_inv(rig)
    ev = arms.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    mw = ev.matrix_world
    res = {"right_arm": 0, "left_arm": 0, "left_hand_planks": 0, "left_hand_handle": 0, "sword": 0}
    for i in classes["R"]:
        if part_at(to_s(mw @ me.vertices[i].co), margin):
            res["right_arm"] += 1
    for i in classes["L_arm"]:
        if part_at(to_s(mw @ me.vertices[i].co), margin):
            res["left_arm"] += 1
    for i in classes["L_hand"]:
        part = part_at(to_s(mw @ me.vertices[i].co), margin)
        if part in ("planks", "rim", "boss"):
            res["left_hand_planks"] += 1
        elif part == "handle" and hang > 0.5:
            res["left_hand_handle"] += 1
    ev.to_mesh_clear()
    smw = sword.matrix_world
    for v in sword.data.vertices:
        if part_at(to_s(smw @ v.co), margin):
            res["sword"] += 1
    for lu in sword_samples(weapon):
        if part_at(to_s(smw @ (C.S @ lu)), margin):
            res["sword"] += 1
    return res


CENTRE_BOX = 0.20  # central screen box, fraction of the half-extent in both axes (portrait framing: keep clear)


def centre_blocked(rig, vfov_deg, aspect, n=5):
    """Fraction of rays through the central screen box (camera at the origin, Unity +Z forward) hitting the buckler."""
    to_s = _socket_inv(rig)
    ty = math.tan(math.radians(vfov_deg) / 2)
    tx = ty * aspect
    hit = 0
    for i in range(n):
        for j in range(n):
            nx = CENTRE_BOX * (-1 + 2 * i / (n - 1))
            ny = CENTRE_BOX * (-1 + 2 * j / (n - 1))
            d = Vector((nx * tx, ny * ty, 1.0))
            if any(part_at(to_s(C.S @ (d * (0.10 + 0.005 * k)))) for k in range(180)):
                hit += 1
    return hit / (n * n)


def check_states(rig, arms, sword, actions, states, attach, margin=0.0, vfov=None, aspect=None):
    """Every frame of every buckler state; attach(sword, rig) places the sword on its socket. Returns a report dict."""
    import fpp_anim as A
    classes = vertex_classes(arms)
    sc = bpy.context.scene
    report = {"source": SOURCE, "margin_m": margin, "states": {}}
    for st in states:
        if not K.has_buckler(st):
            continue
        act = actions[st]
        A.rest_pose(rig)
        rig.animation_data.action = act
        frames = int(round(K.seconds(st) * C.FPS))
        tot = {}
        worst = {}
        per = []
        centre = (0.0, None)
        for f in range(frames + 1):
            sc.frame_set(f + 1)
            attach(sword, rig)
            bpy.context.view_layer.update()
            r = check_frame(rig, arms, sword, "SM_Weapon_FppSword", classes, K.shield_hang(st, f / C.FPS), margin)
            if vfov:
                cb = centre_blocked(rig, vfov, aspect)
                if cb > centre[0]:
                    centre = (cb, round(f / C.FPS, 2))
            if any(r.values()):
                per.append((f, {k: v for k, v in r.items() if v}))
            for k, v in r.items():
                tot[k] = tot.get(k, 0) + v
                if v and k not in worst:
                    worst[k] = round(f / C.FPS, 2)
        report["states"][st] = {"frames": frames + 1, "hits": tot, "first_hit_s": worst,
                                "errors": sum(tot.values()), "hit_frames": per,
                                "centre_box_blocked_max": round(centre[0], 3), "centre_box_blocked_at_s": centre[1]}
    report["errors"] = sum(s["errors"] for s in report["states"].values())
    return report
