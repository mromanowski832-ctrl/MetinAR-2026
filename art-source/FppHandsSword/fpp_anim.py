"""METINAR-FPP-ART-001 — baked clips Idle / Attack / AuraActivate at 100 fps.

The right-hand socket (B_Socket_Sword) and the left-hand socket (B_Socket_GripL) follow the PR #32 pose
functions frame by frame (fpp_contract.sword_pose / left_hand_pose). Arms are solved with an analytic
two-bone IK from fixed shoulders; fingers curl/relax about the hilt axis.
"""
import math

import bpy
from mathutils import Quaternion, Vector

import fpp_combo as K
import fpp_contract as C
import fpp_gear as G
import fpp_model as M

CLIPS = {  # state -> (code action, seconds, loop)
    "Idle": ("Idle", C.IDLE_SECONDS, True),
    "Attack": ("Attack", C.ATTACK_SECONDS, False),
    "AuraActivate": ("Aura", C.AURA_CAST_SECONDS, False),
}
for _st in K.all_states():  # #91 combo / 2H clips (action name = state)
    CLIPS[_st] = (_st, K.seconds(_st), K.is_loop(_st))

WEAPON_MODEL = {"1H": "SM_Weapon_FppSword", "2H": "SM_Weapon_FppGreatsword"}


def weapon_model(state):
    return WEAPON_MODEL[K.weapon_of(state)]

# finger curl offsets (degrees, negative = open) relative to the bind pose (closed on the hilt)
LEFT_RELAXED = {"1": -24.0, "2": -28.0, "T1": -14.0, "T2": -18.0}
LEFT_OPEN = {"1": -40.0, "2": -46.0, "T1": -26.0, "T2": -30.0}

IDLE_SWAY_POS = 0.004   # m
IDLE_SWAY_ROT = 1.2     # deg


def idle_offsets(t, side):
    ph = 2.0 * math.pi * t / C.IDLE_SECONDS
    k = 1.0 if side > 0 else -0.8
    dp = Vector((0.0015 * math.sin(2 * ph) * k, IDLE_SWAY_POS * math.sin(ph), 0.0))
    dr = C.u_euler(IDLE_SWAY_ROT * math.sin(ph), 0.0, 0.6 * math.sin(2 * ph) * k)
    return dp, dr


def hand_pose(side, action, t):
    if action == "Idle":
        pos, rot = M.hand_frame(side)
        dp, dr = idle_offsets(t, side)
        return pos + dp, rot @ dr
    return M.hand_frame(side, action, t)


def finger_curl(side, action, t):
    """Returns {"1": deg, "2": deg, "T1": deg, "T2": deg} relative to the closed bind pose."""
    if side > 0:
        return {"1": 0.0, "2": 0.0, "T1": 0.0, "T2": 0.0}
    if action in K.all_states() and K.weapon_of(action) == "2H":
        # left hand on the greatsword grip the whole time; Aura-2H steps 2-3 = a short re-grip (loosen, squeeze)
        if action == "AuraActivate2H" and 0.05 <= t <= 0.35:
            o = 0.22 * math.sin(math.pi * (t - 0.05) / 0.30)
            return {k: LEFT_OPEN[k] * o for k in LEFT_OPEN}
        return {"1": 0.0, "2": 0.0, "T1": 0.0, "T2": 0.0}
    if action == K.AURA_1H:
        # buckler Aura: release the handle (open, 0.04-0.14 s) while the buckler slides onto the forearm, stay open
        # while travelling, close on the hilt with the V3 blend; on the way back open again and close on the handle
        g = C.left_grip_blend("Aura", t)
        pull = min(1.0, K.shield_hang(action, t) / 0.25)  # 1 once the hand is out of the handle
        if t <= 0.9:
            if g >= 0.8:
                o = 1.0 - C.ease((g - 0.8) / 0.2)
            else:
                o = 0.15 * C.ease(t / 0.06) + 0.85 * C.ease(pull)
        else:
            o = 0.15 * C.ease((t - 0.9) / 0.04) * (1.0 - C.ease((t - 1.10) / 0.05)) + 0.85 * C.ease(pull)
        return {k: LEFT_OPEN[k] * o for k in LEFT_OPEN}
    if K.has_buckler(action):
        # the fist stays closed on the buckler handle (bind pose = closed grip); a light squeeze while blocking
        sq = -0.08 if action.startswith("ShieldBlock") else 0.0
        return {k: LEFT_OPEN[k] * sq for k in LEFT_OPEN}
    if action in K.CLIPS:
        # 1H combo: relaxed free hand, opens a little while it swings out of the sword's way
        keys = K.CLIPS[action]["keys"]
        o = min(1.0, K._hermite(keys, t, 4).length / 0.09)
        return {k: LEFT_RELAXED[k] + (LEFT_OPEN[k] - LEFT_RELAXED[k]) * 0.6 * o for k in LEFT_OPEN}
    if action == "Aura":
        # The code blends the left-hand frame onto the hilt (ease(t/0.35)) and back (0.9-1.15 s).
        # Fingers stay fully open while the hand travels and close only in the last 20% of the blend,
        # so they wrap the hilt instead of sweeping through it.
        g = C.left_grip_blend("Aura", t)
        if g >= 0.8:
            c = C.ease((g - 0.8) / 0.2)
            return {k: LEFT_OPEN[k] * (1.0 - c) for k in LEFT_OPEN}
        if t <= 0.9:
            o = C.ease(min(1.0, t / 0.12))
        else:
            o = C.ease(g / 0.8)  # release: open -> relaxed as the hand returns
        return {k: LEFT_RELAXED[k] + (LEFT_OPEN[k] - LEFT_RELAXED[k]) * o for k in LEFT_OPEN}
    if action == "Attack":
        # the strike end pose sweeps the hilt past the resting left hand: open the fingers out of its way
        o = max(0.0, math.sin(math.pi * min(1.0, max(0.0, (t - 0.26) / 0.34))))
        return {k: LEFT_RELAXED[k] + (LEFT_OPEN[k] - LEFT_RELAXED[k]) * o for k in LEFT_OPEN}
    if action == "Idle":
        wob = 3.0 * math.sin(2.0 * math.pi * t / C.IDLE_SECONDS)
        return {k: v + wob * 0.5 for k, v in LEFT_RELAXED.items()}
    return dict(LEFT_RELAXED)


def _curl_signs(rig):
    """+1/-1 per finger bone: the local-Z rotation sign that OPENS the finger (moves its tip away from the hilt)."""
    signs = {}
    for side in (1, -1):
        pos, rot = M.hand_frame(side)
        axis_p = C.to_blender_point(pos)
        axis_d = C.to_blender_point(rot @ Vector((0, 1, 0))).normalized()

        def dist(p):
            v = p - axis_p
            return (v - axis_d * v.dot(axis_d)).length

        for name in M.bone_names(side)[3:]:
            bone = rig.data.bones[name]
            z = Vector(bone.matrix_local.col[2][:3]).normalized()
            head, tail = bone.head_local, bone.tail_local
            rotp = Quaternion(z, math.radians(5.0)) @ (tail - head) + head
            signs[name] = 1.0 if dist(rotp) > dist(tail) else -1.0
    return signs


def apply_frame(rig, action, t, signs, extra=0.0):
    """Pose the rig for (action, t) in three hierarchy passes and return the wrist gaps."""
    gaps = {}
    frames = {}
    for side in (1, -1):
        pos, rot = hand_pose(side, action, t)
        f, gap = M.arm_frames_from(side, pos, rot)
        frames.update(f)
        gaps[side] = gap
    for level in ("UpperArm", "Forearm", "Hand"):
        for name, m in frames.items():
            if level in name:
                rig.pose.bones[name].matrix = m
        bpy.context.view_layer.update()
    pose_shield_socket(rig, action, t)
    for side in (1, -1):
        s = M.side_tag(side)
        curl = finger_curl(side, action, t)
        if side < 0 and extra:
            curl = {k: v + LEFT_OPEN[k] * extra for k, v in curl.items()}
        for name in M.bone_names(side)[3:]:
            key = ("T1" if "Thumb1" in name else "T2" if "Thumb2" in name else ("1" if name.split("_")[1].endswith("1") else "2"))
            ang = math.radians(curl[key]) * -signs[name]  # negative curl = open
            rig.pose.bones[name].rotation_quaternion = Quaternion((0, 0, 1), ang)
    bpy.context.view_layer.update()
    return gaps


# buckler parked on the outer left forearm during the 1H Aura (K6 rule 5): handle bar along the forearm, resting on
# it, planks facing away from the sword; HANG_ALONG = socket distance from the wrist towards the elbow (m)
HANG_ALONG = 0.150
HANG_OUT = 0.072      # socket (handle centre) distance from the forearm bone axis: sleeve radius + handle radius


def forearm_hang_frame(rig, action, t):
    """(Unity position, Unity quaternion) of B_Socket_ShieldL parked on the left forearm in the current pose."""
    fb = rig.pose.bones["B_Forearm_L"]
    m = rig.matrix_world @ fb.matrix
    elbow_b, wrist_b = m.translation, rig.matrix_world @ fb.tail
    u = lambda v: Vector((v.x, v.z, v.y))  # Blender -> Unity point
    elbow, wrist = u(elbow_b), u(wrist_b)
    a = (wrist - elbow).normalized()
    mid = wrist - a * HANG_ALONG
    # outward = back of the left hand (the palm side faces the hilt, the pommel sticks out below the fist)
    _, hq = K.left_pose(action, t)
    n = hq @ K.HAND_BACK_LOCAL
    n = (n - a * n.dot(a)).normalized()
    return mid + n * HANG_OUT, K.shield_frame(n, -a)


HANG_PULL = 0.070      # 1) the open hand slides back out of the handle (buckler moves along its front)
HANG_SIDE = 0.210      # 2) the buckler moves sideways (away from the sword) until its rim clears hand and forearm
HANG_KEYS = (0.0, 0.25, 0.50, 1.0)


def hang_path(p0, q0, hang, w):
    """Fist -> forearm path of the buckler socket (Unity frames), w = 0..1: pull-out, side step, fold onto the arm."""
    hp, hq = hang
    front = q0 @ Vector((0.0, 0.0, 1.0))
    side = hq @ Vector((0.0, 0.0, 1.0))  # outward normal of the parked buckler
    side = (side - front * side.dot(front)).normalized()
    p1 = p0 + front * HANG_PULL
    p2 = p1 + side * HANG_SIDE
    pts = [(p0, q0), (p1, q0), (p2, q0), (hp, hq)]
    for i in range(3):
        a, b = HANG_KEYS[i], HANG_KEYS[i + 1]
        if w <= b or i == 2:
            u = C.ease((w - a) / (b - a))
            return pts[i][0].lerp(pts[i + 1][0], u), C.slerp(pts[i][1], pts[i + 1][1], u)


def pose_shield_socket(rig, action, t):
    """B_Socket_ShieldL: constant in the hand (rest) except the buckler Aura, where it is blended in world space from
    the fist to the forearm and back (continuous, no teleport)."""
    w = K.shield_hang(action, t)
    pb = rig.pose.bones["B_Socket_ShieldL"]
    pb.location, pb.rotation_quaternion = (0, 0, 0), (1, 0, 0, 0)
    if w <= 0.0:
        bpy.context.view_layer.update()
        return
    bpy.context.view_layer.update()
    cur = rig.matrix_world @ pb.matrix @ C.NODE_FIX_INV  # Blender grip frame
    q_b = cur.to_quaternion()
    S = C.S.to_3x3() if hasattr(C.S, "to_3x3") else C.S
    q_u = (S @ q_b.to_matrix() @ S).to_quaternion()
    p_u = Vector((cur.translation.x, cur.translation.z, cur.translation.y))
    p, q = hang_path(p_u, q_u, forearm_hang_frame(rig, action, t), w)
    pb.matrix = rig.matrix_world.inverted() @ C.to_blender_node_matrix(p, q)
    bpy.context.view_layer.update()


def rest_pose(rig):
    for pb in rig.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()


# ------------------------------------------------------------ hand / sword contact
def hand_vertex_sets(arms):
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


def sword_part_hit(lu):
    """Name of the sword volume containing the sword-MODEL-local Unity point lu (scale 1), or None (conservative proxies)."""
    r = math.hypot(lu.x, lu.z)
    if M.HILT_BOTTOM <= lu.y <= M.HILT_TOP and r < M.HILT_RADIUS:
        return "hilt"
    if abs(lu.x) < 0.11 and abs(lu.y - M.GUARD_Y) < 0.010 and abs(lu.z) < 0.012:
        return "guard"
    if 0.13 <= lu.y <= 0.80 and abs(lu.x) < 0.026 and abs(lu.z) < 0.004:
        return "blade"
    if r < 0.026 and abs(lu.y - M.POMMEL_Y) < 0.018:
        return "pommel"
    return None


def count_hits(arms, rig, verts, weapon="SM_Weapon_FppSword"):
    ev = arms.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ev.to_mesh()
    inv = (rig.matrix_world @ rig.pose.bones["B_Socket_Sword"].matrix @ C.NODE_FIX_INV @ C.SWORD_SCALE_MATRIX).inverted() @ ev.matrix_world
    n = 0
    for i in verts:
        if G.proxy_hit(weapon, C.S @ (inv @ me.vertices[i].co)):
            n += 1
    ev.to_mesh_clear()
    return n


FIT_CANDIDATES = [round(-1.2 + 0.1 * i, 2) for i in range(23)]  # extra opening (x LEFT_OPEN); negative closes


def fit_left_fingers(rig, arms, action, frames, signs, weapon="SM_Weapon_FppSword"):
    """Per-frame extra left-finger opening that removes hand/sword intersections, then smoothed and re-checked."""
    verts = hand_vertex_sets(arms)[-1]
    extra = [0.0] * (frames + 1)
    for f in range(frames + 1):
        t = f / C.FPS
        apply_frame(rig, action, t, signs)
        if count_hits(arms, rig, verts, weapon) == 0:
            continue
        best = None
        for d in FIT_CANDIDATES:
            apply_frame(rig, action, t, signs, d)
            cost = count_hits(arms, rig, verts, weapon) * 1000 + abs(d)
            if best is None or cost < best[0]:
                best = (cost, d)
        extra[f] = best[1]
    # smooth: hold the strongest correction within +-6 frames, then a 7-tap box filter, so every corrected
    # frame keeps at least its own value and the fingers ease in/out instead of popping
    held = [max(extra[max(0, f - 6):f + 7], key=abs) for f in range(frames + 1)]
    smooth = []
    for f in range(frames + 1):
        win = held[max(0, f - 3):f + 4]
        smooth.append(sum(win) / len(win))
    return smooth


def build_actions(rig, arms=None, states=None, fit=True):
    signs = _curl_signs(rig)
    actions, stats = {}, {}
    if rig.animation_data is None:
        rig.animation_data_create()
    for state, (action, seconds, loop) in CLIPS.items():
        if states is not None and state not in states:
            continue
        rest_pose(rig)
        act = bpy.data.actions.new("A_FppArms_" + state)
        act.use_fake_user = True
        rig.animation_data.action = act
        frames = int(round(seconds * C.FPS))
        do_fit = arms is not None and fit and not loop and (not K.has_buckler(state) or state == K.AURA_1H)
        extra = fit_left_fingers(rig, arms, action, frames, signs, weapon_model(state)) if do_fit else [0.0] * (frames + 1)
        rest_pose(rig)
        max_gap = 0.0
        for f in range(frames + 1):
            t = f / C.FPS
            gaps = apply_frame(rig, action, t, signs, extra[f])
            max_gap = max(max_gap, *gaps.values())
            for pb in rig.pose.bones:
                pb.keyframe_insert("location", frame=f + 1)
                pb.keyframe_insert("rotation_quaternion", frame=f + 1)
                pb.keyframe_insert("scale", frame=f + 1)
        for fc in act.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR"
        actions[state] = act
        stats[state] = {"frames": frames + 1, "seconds": seconds, "loop": loop, "max_wrist_gap_m": round(max_gap, 4),
                        "weapon": weapon_model(state), "events": K.events(state),
                        "left_finger_fit_frames": sum(1 for e in extra if abs(e) > 1e-6),
                        "left_finger_fit_range": [round(min(extra), 3), round(max(extra), 3)]}
    rest_pose(rig)
    return actions, stats, signs
