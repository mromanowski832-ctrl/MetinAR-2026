"""METINAR-FPP-ART-001 — original procedural FPP sword, gloves/forearms and rig (Blender 4.2, no external data).

All dimensions are designed in Unity space (see fpp_contract.py) and converted to Blender space on output.
"""
import math

import bpy
from mathutils import Matrix, Vector

import fpp_contract as C
from fpp_mesh import Builder, blend_weights

# regions (Data.R * 10)
REG_LEATHER, REG_METAL, REG_GOLD, REG_CLOTH = 1, 2, 3, 4
REG_BLADE, REG_FULLER, REG_GUARD, REG_SGOLD, REG_GRIP, REG_GEM = 1, 2, 3, 4, 5, 6

# ---------------------------------------------------------------- sword (Unity grip-local: hilt/blade +Y)
HILT_RADIUS = 0.0165
HILT_BOTTOM, HILT_TOP = -0.172, 0.104
GUARD_Y = 0.116
POMMEL_Y = -0.195

SWORD_SOCKETS = {
    "Grip_Right": (0.0, 0.0, 0.0),
    "Grip_Left": tuple(C.LEFT_GRIP_OFFSET),
    "FX_BladeBase": tuple(C.BLADE_BASE),
    "FX_BladeTip": tuple(C.BLADE_TIP),
    "FX_AuraBurst": tuple(C.AURA_BURST_CENTRE),
    "FX_GuardGem": (0.0, GUARD_Y, 0.0),
}


def _u2b(p):
    return C.S @ Vector(p)


def build_sword():
    b = Builder()
    none = lambda i, n: {}
    up = (0.0, 0.0, 1.0)

    # grip: leather-wrapped, slightly waisted
    ys = [HILT_BOTTOM + (HILT_TOP - HILT_BOTTOM) * k / 9 for k in range(10)]
    rad = [HILT_RADIUS * (1.0 + 0.06 * math.sin(math.pi * k / 9)) for k in range(10)]
    b.tube([_u2b((0, y, 0)) for y in ys], rad, 12, REG_GRIP, none, up, cap_start=False, cap_end=False)

    # cross-guard with up-swept tips
    xs = [-0.125 + 0.25 * k / 14 for k in range(15)]
    pts, radii = [], []
    for x in xs:
        a = abs(x) / 0.125
        pts.append(_u2b((x, GUARD_Y + 0.020 * a ** 2.6, 0)))
        s = 1.0 - 0.45 * a ** 3
        radii.append((0.012 * s, 0.017 * s))  # (Unity y, Unity z) thickness
    b.tube(pts, radii, 10, REG_GUARD, none, _u2b((0, 1, 0)))
    # guard centre block (gold) + gems on both faces
    b.tube([_u2b((0, y, 0)) for y in (0.098, 0.106, 0.128, 0.137)], [(0.020, 0.015), (0.027, 0.020), (0.027, 0.020), (0.020, 0.015)],
           10, REG_SGOLD, none, _u2b((1, 0, 0)))
    for sgn in (-1, 1):
        b.tube([_u2b((0, GUARD_Y, sgn * z)) for z in (0.012, 0.019, 0.023)], [0.0095, 0.0095, 0.007], 10, REG_GEM, none,
               _u2b((1, 0, 0)), cap_start=False)

    # blade: lens profile with a fuller, ending in a point at BLADE_TIP
    def width(y):
        if y <= 0.80:
            return 0.032 - 0.006 * (y - 0.13) / 0.67
        return 0.026 * max(0.0, 1.0 - (y - 0.80) / (C.BLADE_TIP.y - 0.80)) ** 0.85

    def thick(y):
        return 0.0065 - 0.003 * min(1.0, (y - 0.13) / 0.75)

    profile = [(1.0, 0.0), (0.55, 0.8), (0.18, 1.0), (0.0, 0.55), (-0.18, 1.0), (-0.55, 0.8),
               (-1.0, 0.0), (-0.55, -0.8), (-0.18, -1.0), (0.0, -0.55), (0.18, -1.0), (0.55, -0.8)]
    stations = [0.105, 0.13, 0.16, 0.22, 0.30, 0.38, 0.46, 0.54, 0.62, 0.70, 0.76, 0.80, 0.84, 0.88, 0.91]
    rings = []
    for y in stations:
        w, t = width(max(y, 0.13)), thick(max(y, 0.13))
        fuller = 0.16 <= y <= 0.70
        ring = []
        for k, (px, pz) in enumerate(profile):
            if abs(px) < 1e-6:
                pz = (0.55 if fuller else 1.0) * (1 if pz > 0 else -1)
            reg = REG_FULLER if (fuller and abs(px) < 0.2) else REG_BLADE
            ring.append(b.vert(_u2b((px * w, y, pz * t)), {}, reg, (y - 0.13) / 0.81, k / len(profile)))
        rings.append(ring)
    n = len(profile)
    for i in range(len(rings) - 1):
        for k in range(n):
            b.poly((rings[i][k], rings[i][(k + 1) % n], rings[i + 1][(k + 1) % n], rings[i + 1][k]))
    tip = b.vert(_u2b(tuple(C.BLADE_TIP)), {}, REG_BLADE, 1.0, 0.5)
    for k in range(n):
        b.poly((rings[-1][k], rings[-1][(k + 1) % n], tip))
    base = b.vert(_u2b((0, 0.105, 0)), {}, REG_BLADE, 0.0, 0.5)
    for k in range(n):
        b.poly((rings[0][(k + 1) % n], rings[0][k], base))

    # pommel (gold, faceted) with a gem through it
    prof = [(-0.170, 0.019), (-0.176, 0.026), (-0.186, 0.030), (-0.198, 0.031), (-0.209, 0.027), (-0.217, 0.016)]
    b.tube([_u2b((0, y, 0)) for y, _ in prof], [r for _, r in prof], 12, REG_SGOLD, none, _u2b((1, 0, 0)),
           cap_start=False, cap_len=0.006)
    b.tube([_u2b((0, POMMEL_Y, z)) for z in (-0.0335, -0.028, 0.028, 0.0335)], [0.0075, 0.0095, 0.0095, 0.0075], 10, REG_GEM,
           none, _u2b((1, 0, 0)))

    obj = b.to_object("SM_Weapon_FppSword")
    sockets = []
    for name, p in SWORD_SOCKETS.items():
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.03
        e.location = _u2b(p)
        e.parent = obj
        bpy.context.scene.collection.objects.link(e)
        sockets.append(e)
    return obj, sockets


# ---------------------------------------------------------------- hands (grip-local Unity design frame)
HAND_YAW_DEG = -45.0  # right hand rotated about the hilt so the wrist points back towards the elbow
UPPER_LEN, FORE_LEN = 0.30, 0.285
PALM_X = 0.036
WRIST_LOCAL = Vector((PALM_X, 0.0, -0.080))
KNUCKLE_LOCAL = Vector((PALM_X, 0.0, 0.024))
FINGERS = (  # name, y, radius, last-segment scale
    ("Index", 0.029, 0.0092, 0.85),
    ("Middle", 0.009, 0.0095, 1.0),
    ("Ring", -0.011, 0.0090, 0.85),
    ("Pinky", -0.029, 0.0080, 0.60),
)
FINGER_PATH = ((PALM_X, 0.022), (-0.004, 0.036), (-0.033, 0.008), (-0.020, -0.026))  # (x, z) MCP, PIP, DIP, tip
THUMB_PATH = ((0.040, 0.038, -0.040), (0.014, 0.050, -0.036), (-0.012, 0.054, -0.030), (-0.029, 0.051, -0.012))
THUMB_RADIUS = 0.0098


def _yaw():
    return C.u_euler(0.0, HAND_YAW_DEG, 0.0)


def design_to_local(p, side):
    """Design point -> hand frame (grip-local Unity); side = +1 right, -1 left (mirror X)."""
    q = _yaw() @ Vector(p)
    return Vector((q.x * side, q.y, q.z))


def finger_joints(y, last_scale):
    pts = [Vector((x, y, z)) for x, z in FINGER_PATH]
    pts[3] = pts[2] + (pts[3] - pts[2]) * last_scale
    return pts


def hand_frame(side, action="Rest", time=0.0):
    if side > 0:
        return C.sword_pose(action, time) if action != "Rest" else C.sword_pose("Idle", 0.0)
    return C.left_hand_pose(action, time) if action != "Rest" else C.left_hand_pose("Idle", 0.0)


def side_tag(side):
    return "R" if side > 0 else "L"


def bone_names(side):
    s = side_tag(side)
    names = ["B_UpperArm_" + s, "B_Forearm_" + s, "B_Hand_" + s]
    for f, *_ in FINGERS:
        names += ["B_%s1_%s" % (f, s), "B_%s2_%s" % (f, s)]
    names += ["B_Thumb1_" + s, "B_Thumb2_" + s]
    return names


DEFORM_BONES = ["B_Root"] + bone_names(1) + bone_names(-1) + ["B_Socket_Sword", "B_Socket_GripL"]


# ---------------------------------------------------------------- arm IK (Unity space)
def pole_dir(side):
    return Vector((side * 1.0, -1.0, -0.3)).normalized()


def solve_elbow(shoulder, wrist, side):
    a, b = UPPER_LEN, FORE_LEN
    d_vec = wrist - shoulder
    d_raw = d_vec.length
    d = min(max(d_raw, abs(a - b) + 1e-4), a + b - 1e-4)
    axis = d_vec.normalized()
    cos_a = (a * a + d * d - b * b) / (2 * a * d)
    along, h = a * cos_a, a * math.sqrt(max(0.0, 1 - cos_a * cos_a))
    p = pole_dir(side)
    n = (p - axis * p.dot(axis)).normalized()
    elbow = shoulder + axis * along + n * h
    return elbow, n, max(0.0, d_raw - d)


def wrist_world(side, action, time=0.0):
    pos, rot = hand_frame(side, action, time)
    return C.frame_point(pos, rot, design_to_local(WRIST_LOCAL, side))


def all_wrists(side):
    out = []
    for action, length in (("Attack", C.ATTACK_SECONDS), ("Aura", C.AURA_CAST_SECONDS)):
        for f in range(int(round(length * C.FPS)) + 1):
            out.append(wrist_world(side, action, f / C.FPS))
    out.append(wrist_world(side, "Rest"))
    return out


def choose_shoulder(side):
    """Deterministic search: shoulder out of view (behind/below the camera) that reaches every wrist pose."""
    wrists = all_wrists(side)
    reach = (UPPER_LEN + FORE_LEN) * 0.985
    best = None
    for ix in range(0, 9):
        for iy in range(0, 11):
            for iz in range(0, 11):
                s = Vector((side * (0.12 + 0.02 * ix), -0.22 - 0.025 * iy, -0.20 + 0.025 * iz))
                dmax = max((w - s).length for w in wrists)
                dmin = min((w - s).length for w in wrists)
                if dmax > reach or dmin < 0.30:
                    continue
                score = s.z * 2.0 + abs(abs(s.x) - 0.19) + abs(s.y + 0.30)  # prefer further back, natural width
                if best is None or score < best[0]:
                    best = (score, s, dmax, dmin)
    if best is None:
        raise RuntimeError("no reachable shoulder position")
    return best[1], best[2], best[3]


SHOULDERS = {}


def shoulder(side):
    if side not in SHOULDERS:
        SHOULDERS[side] = choose_shoulder(side)
    return SHOULDERS[side][0]


# ---------------------------------------------------------------- arms mesh
def build_arms():
    b = Builder()
    for side in (1, -1):
        _build_arm(b, side)
    return b.to_object("SK_Player_FppArms")


def _build_arm(b, side):
    s = side_tag(side)
    hand_b, fore_b, up_b = "B_Hand_" + s, "B_Forearm_" + s, "B_UpperArm_" + s
    pos, rot = hand_frame(side)

    def W(p):  # design -> Blender world (rest)
        return C.to_blender_point(C.frame_point(pos, rot, design_to_local(p, side)))

    hand_axis_b = C.to_blender_point(rot @ Vector((0, 1, 0)))
    side_axis_b = C.to_blender_point(rot @ design_to_local((1, 0, 0), side) - Vector((0, 0, 0)))
    hw = {hand_b: 1.0}

    # palm (elliptic, thickness along the design X axis, width along the hilt)
    zs = (-0.080, -0.056, -0.026, 0.004, 0.026)
    radii = ((0.013, 0.027), (0.015, 0.036), (0.016, 0.042), (0.0155, 0.043), (0.013, 0.041))
    b.tube([W((PALM_X, 0, z)) for z in zs], list(radii), 14, REG_LEATHER,
           lambda i, n: blend_weights({fore_b: 1.0}, hw, 0.5) if i == 0 else hw, side_axis_b, cap_len=0.010)
    # back-of-hand plate (dark metal) and gold knuckle bar
    b.tube([W((PALM_X + 0.0175, 0, z)) for z in (-0.062, -0.032, -0.002, 0.016)],
           [(0.0035, 0.030), (0.0035, 0.035), (0.0035, 0.036), (0.0035, 0.033)], 12, REG_METAL, lambda i, n: hw, side_axis_b)
    b.tube([W((0.047, y, 0.026)) for y in (-0.040, -0.013, 0.013, 0.041)], [0.0075] * 4, 8, REG_GOLD, lambda i, n: hw,
           side_axis_b)

    # fingers
    for name, y, r, last in FINGERS:
        p0, p1, p2, p3 = finger_joints(y, last)
        pts = [p0, (p0 + p1) / 2, p1, (p1 + p2) / 2, p2, (p2 + p3) / 2, p3]
        b1, b2 = "B_%s1_%s" % (name, s), "B_%s2_%s" % (name, s)
        wts = [hw, {b1: 0.85, hand_b: 0.15}, {b1: 0.5, b2: 0.5}, {b2: 1.0}, {b2: 1.0}, {b2: 1.0}, {b2: 1.0}]
        rr = [r * k for k in (1.05, 1.0, 1.02, 0.97, 0.97, 0.93, 0.9)]
        b.tube([W(p) for p in pts], rr, 10, REG_LEATHER, lambda i, n, wts=wts: wts[i], hand_axis_b, cap_start=False,
               u_range=(0.0, 1.0))
        # dark-metal plate on the back of the proximal phalanx (gauntlet look), offset away from the hilt
        out0 = Vector((p0.x, 0, p0.z)).normalized()
        out1 = Vector((p1.x, 0, p1.z)).normalized()
        q0 = p0.lerp(p1, 0.18) + out0.lerp(out1, 0.18).normalized() * (r + 0.0018)
        q1 = p0.lerp(p1, 0.82) + out0.lerp(out1, 0.82).normalized() * (r + 0.0018)
        b.tube([W(q0), W((q0 + q1) / 2), W(q1)], [(0.0022, r * 0.85)] * 3, 8, REG_METAL, lambda i, n, b1=b1: {b1: 1.0},
               C.to_blender_point(rot @ design_to_local(((q0 + q1) / 2 - Vector((0, y, 0))).normalized(), side)),
               cap_len=0.002)
    # thumb
    t0, t1, t2, t3 = (Vector(p) for p in THUMB_PATH)
    pts = [t0, (t0 + t1) / 2, t1, (t1 + t2) / 2, t2, (t2 + t3) / 2, t3]
    b1, b2 = "B_Thumb1_" + s, "B_Thumb2_" + s
    wts = [{hand_b: 0.6, b1: 0.4}, {b1: 1.0}, {b1: 1.0}, {b1: 1.0}, {b1: 0.5, b2: 0.5}, {b2: 1.0}, {b2: 1.0}]
    b.tube([W(p) for p in pts], [THUMB_RADIUS * k for k in (1.15, 1.05, 1.0, 0.98, 0.97, 0.93, 0.9)], 10, REG_LEATHER,
           lambda i, n: wts[i], hand_axis_b, cap_start=False)

    # forearm: glove cuff -> vambrace -> sleeve, then the upper arm
    wrist_u = C.frame_point(pos, rot, design_to_local(WRIST_LOCAL, side))
    sh = shoulder(side)
    elbow_u, pole_n, _ = solve_elbow(sh, wrist_u, side)
    fore_dir = (wrist_u - elbow_u).normalized()
    wrist_u = elbow_u + fore_dir * FORE_LEN

    def F(t):
        return C.to_blender_point(wrist_u.lerp(elbow_u, t))

    hint = C.to_blender_point(pole_n)
    ts = [0.0, 0.06, 0.12, 0.16, 0.30, 0.50, 0.70, 0.86, 1.0]
    rf = [0.027, 0.031, 0.036, 0.033, 0.035, 0.038, 0.041, 0.043, 0.044]

    def fw(i, n):
        t = ts[i]
        if t < 0.12:
            return blend_weights({hand_b: 1.0}, {fore_b: 1.0}, min(1.0, 0.5 + t / 0.24))
        if t > 0.9:
            return blend_weights({fore_b: 1.0}, {up_b: 1.0}, (t - 0.9) / 0.2)
        return {fore_b: 1.0}

    b.tube([F(t) for t in ts], rf, 16, REG_LEATHER, fw, hint, cap_end=False, cap_len=0.008, u_range=(0.0, 1.0))
    vt = [0.20, 0.32, 0.46, 0.60, 0.74, 0.84]
    b.tube([F(t) for t in vt], [0.035 + 0.0065 * t + 0.006 for t in vt], 16, REG_METAL, lambda i, n: {fore_b: 1.0}, hint,
           u_range=(0.0, 1.0))
    for t in (0.20, 0.84):
        b.tube([F(t - 0.015), F(t + 0.015)], [0.035 + 0.0065 * t + 0.009] * 2, 16, REG_GOLD, lambda i, n: {fore_b: 1.0}, hint,
               cap_len=0.002)
    # sleeve: elbow -> shoulder (cloth); usually outside the phone frame
    up_ts = [0.0, 0.25, 0.6, 1.0]

    def U(t):
        return C.to_blender_point(elbow_u.lerp(sh, t))

    b.tube([U(t) for t in up_ts], [0.045, 0.047, 0.049, 0.050], 12, REG_CLOTH,
           lambda i, n: blend_weights({fore_b: 1.0}, {up_b: 1.0}, 0.5) if i == 0 else {up_b: 1.0}, hint)


# ---------------------------------------------------------------- armature
def bone_frame(head, tail, z_hint):
    y = (tail - head).normalized()
    z = (z_hint - y * z_hint.dot(y)).normalized()
    x = y.cross(z)
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = head
    return m


def arm_frames(side, action, time, curl=None):
    """Blender-space bone matrices for one arm at (action, time). Returns (dict, wrist gap metres)."""
    s = side_tag(side)
    pos, rot = hand_frame(side, action, time)
    return arm_frames_from(side, pos, rot)


def arm_frames_from(side, pos, rot):
    s = side_tag(side)
    wrist = C.frame_point(pos, rot, design_to_local(WRIST_LOCAL, side))
    knuckle = C.frame_point(pos, rot, design_to_local(KNUCKLE_LOCAL, side))
    hilt = rot @ Vector((0, 1, 0))
    sh = shoulder(side)
    elbow, n, gap = solve_elbow(sh, wrist, side)
    fore_tail = elbow + (wrist - elbow).normalized() * FORE_LEN
    B = C.to_blender_point
    out = {
        "B_UpperArm_" + s: bone_frame(B(sh), B(elbow), B(n)),
        "B_Forearm_" + s: bone_frame(B(elbow), B(fore_tail), B(n)),
        "B_Hand_" + s: bone_frame(B(wrist), B(knuckle), B(hilt)),
    }
    return out, gap


def build_armature():
    arm_data = bpy.data.armatures.new("Rig")
    rig = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    root = eb.new("B_Root")
    root.head, root.tail = Vector((0, 0, 0)), Vector((0, 0.05, 0))
    for side in (1, -1):
        s = side_tag(side)
        frames, _ = arm_frames(side, "Rest", 0.0)
        prev = root
        for name in ("B_UpperArm_" + s, "B_Forearm_" + s, "B_Hand_" + s):
            m = frames[name]
            bone = eb.new(name)
            head = m.translation.copy()
            y = Vector(m.col[1][:3])
            length = (UPPER_LEN if "Upper" in name else FORE_LEN if "Fore" in name else (KNUCKLE_LOCAL - WRIST_LOCAL).length)
            bone.head, bone.tail = head, head + y * length
            bone.align_roll(Vector(m.col[2][:3]))
            bone.parent = prev
            bone.use_connect = False
            prev = bone
        hand = prev
        pos, rot = hand_frame(side)

        def Wd(p):
            return C.to_blender_point(C.frame_point(pos, rot, design_to_local(p, side)))

        hilt_b = C.to_blender_point(rot @ Vector((0, 1, 0)))
        for fname, y, r, last in FINGERS:
            p0, p1, p2, p3 = finger_joints(y, last)
            b1 = eb.new("B_%s1_%s" % (fname, s))
            b1.head, b1.tail = Wd(p0), Wd(p1)
            b1.align_roll(hilt_b)
            b1.parent = hand
            b2 = eb.new("B_%s2_%s" % (fname, s))
            b2.head, b2.tail = Wd(p1), Wd(p3)
            b2.align_roll(hilt_b)
            b2.parent = b1
            b2.use_connect = True
        t0, t1, t2, t3 = (Vector(p) for p in THUMB_PATH)
        th1 = eb.new("B_Thumb1_" + s)
        th1.head, th1.tail = Wd(t0), Wd(t2)
        th1.align_roll(C.to_blender_point(rot @ design_to_local((1, 0, 0), side)))
        th1.parent = hand
        th2 = eb.new("B_Thumb2_" + s)
        th2.head, th2.tail = Wd(t2), Wd(t3)
        th2.align_roll(C.to_blender_point(rot @ design_to_local((1, 0, 0), side)))
        th2.parent = th1
        th2.use_connect = True
        # socket bones: exactly the code's RightGrip / LeftHand frames. The bone matrix must equal
        # to_blender_matrix(frame) so that Unity (which conjugates node transforms by the axis swap) sees the
        # frame itself: bone Y = Blender column 1 (= Unity frame +Z), bone Z = column 2 (= Unity frame +Y, hilt).
        sock = eb.new("B_Socket_Sword" if side > 0 else "B_Socket_GripL")
        m = C.to_blender_matrix(pos, rot)
        sock.head = m.translation.copy()
        sock.tail = sock.head + Vector(m.col[1][:3]) * 0.05
        sock.align_roll(Vector(m.col[2][:3]))
        sock.parent = hand
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


def bind(mesh_obj, rig):
    mesh_obj.parent = rig
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return mod
