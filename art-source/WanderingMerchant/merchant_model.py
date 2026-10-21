"""METINAR-016 — Wandering Merchant (original MetinAR NPC): procedural low-poly mesh + Unity-humanoid rig.

Blender units = metres, Z up, the character FACES BLENDER +Y (Unity's FBX import maps Blender +Y to Unity +Z,
see METINAR-013), so no export-time turn is needed. Left side = Blender -X (= Unity -X).
Rest pose: A-pose (arms 40 deg below horizontal), feet on Z = 0, height ~1.84 m with the hat.
Design (original): travelling trader in a knee-length green coat with a mantle collar, wide-brimmed felt hat,
grey beard, leather belt with pouches and a wooden back-pack frame carrying a bundle, bedroll, pot and lantern.
"""
import math

import bpy
from mathutils import Matrix, Vector

from merchant_mesh import Builder, blend_weights, triangle_count  # noqa: F401

# ------------------------------------------------------------------ regions (Data.R * 10)
REG_SKIN, REG_COAT, REG_LEATHER, REG_TROUSER, REG_HAT, REG_WOOD, REG_BRASS, REG_HAIR, REG_CANVAS = range(1, 10)

ARM_DOWN = math.radians(40.0)

# ------------------------------------------------------------------ skeleton (Unity humanoid names)
# name: (head, tail, parent)
def _arm_chain(side):
    s = -1.0 if side == "Left" else 1.0
    sh = Vector((s * 0.035, 0.0, 1.42))
    ua = Vector((s * 0.175, 0.0, 1.425))
    d = Vector((s * math.cos(ARM_DOWN), 0.0, -math.sin(ARM_DOWN)))
    el = ua + d * 0.285
    wr = el + d * 0.255
    tip = wr + d * 0.10
    return {
        side + "Shoulder": (sh, ua, "Chest"),
        side + "UpperArm": (ua, el, side + "Shoulder"),
        side + "LowerArm": (el, wr, side + "UpperArm"),
        side + "Hand": (wr, tip, side + "LowerArm"),
    }


def _leg_chain(side):
    s = -1.0 if side == "Left" else 1.0
    hip = Vector((s * 0.095, 0.0, 0.93))
    knee = Vector((s * 0.098, 0.012, 0.50))
    ankle = Vector((s * 0.10, -0.01, 0.085))
    ball = Vector((s * 0.105, 0.115, 0.025))
    toe = Vector((s * 0.105, 0.185, 0.025))
    return {
        side + "UpperLeg": (hip, knee, "Hips"),
        side + "LowerLeg": (knee, ankle, side + "UpperLeg"),
        side + "Foot": (ankle, ball, side + "LowerLeg"),
        side + "Toes": (ball, toe, side + "Foot"),
    }


BONES = {
    "Hips": (Vector((0, 0, 0.95)), Vector((0, 0, 1.06)), None),
    "Spine": (Vector((0, 0, 1.06)), Vector((0, 0, 1.22)), "Hips"),
    "Chest": (Vector((0, 0, 1.22)), Vector((0, 0, 1.45)), "Spine"),
    "Neck": (Vector((0, 0.005, 1.45)), Vector((0, 0.01, 1.555)), "Chest"),
    "Head": (Vector((0, 0.01, 1.555)), Vector((0, 0.01, 1.76)), "Neck"),
}
for _side in ("Left", "Right"):
    BONES.update(_arm_chain(_side))
    BONES.update(_leg_chain(_side))
DEFORM_BONES = list(BONES.keys())


def bone_head(name):
    return BONES[name][0]


def bone_tail(name):
    return BONES[name][1]


def build_armature():
    arm = bpy.data.armatures.new("Armature")
    rig = bpy.data.objects.new("Armature", arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (h, t, parent) in BONES.items():
        eb = arm.edit_bones.new(name)
        eb.head, eb.tail = h, t
        # roll: spine/legs keep local Z towards +Y (forward); arms keep local Z up-ish
        eb.align_roll(Vector((0, 1, 0)) if abs((t - h).normalized().z) > 0.7 else Vector((0, 0, 1)))
        eb.use_deform = True
    for name, (h, t, parent) in BONES.items():
        if parent:
            eb = arm.edit_bones[name]
            eb.parent = arm.edit_bones[parent]
            eb.use_connect = (eb.head - arm.edit_bones[parent].tail).length < 1e-4
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.show_in_front = True
    return rig


def bind(mesh, rig):
    mesh.parent = rig
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


# ------------------------------------------------------------------ mesh
def W(**kw):
    return dict(kw)


def _lerp(a, b, t):
    return a + (b - a) * t


def _arm_points(side, a, b, n):
    s = side
    h, t = bone_head(s), bone_tail(s)
    return [h.lerp(t, _lerp(a, b, i / (n - 1))) for i in range(n)]


def build_mesh(name="SK_Npc_WanderingMerchant_LOD0"):
    B = Builder()

    # ---------------- coat: knee-length, flared hem, closed at the neck
    coat_z = [0.46, 0.60, 0.76, 0.90, 0.99, 1.06, 1.16, 1.27, 1.36, 1.42, 1.465, 1.50]
    coat_r = [(0.255, 0.225), (0.235, 0.205), (0.21, 0.18), (0.19, 0.155), (0.178, 0.142), (0.172, 0.135),
              (0.182, 0.14), (0.198, 0.145), (0.20, 0.14), (0.185, 0.125), (0.13, 0.105), (0.065, 0.065)]

    def coat_w(i, n):
        z = coat_z[i]
        if z >= 1.40:
            return blend_weights(W(Chest=1.0), W(Neck=1.0), min(1.0, (z - 1.40) / 0.10))
        if z >= 1.22:
            return blend_weights(W(Spine=1.0), W(Chest=1.0), min(1.0, (z - 1.16) / 0.12))
        if z >= 1.06:
            return blend_weights(W(Hips=1.0), W(Spine=1.0), (z - 1.0) / 0.16)
        return W(Hips=1.0)

    rings = B.tube([(0, 0.0, z) for z in coat_z], coat_r, 20, REG_COAT, coat_w, (1, 0, 0),
                   cap_start=False, cap_end=True, cap_len=0.02)
    # hem: inner wall so the coat has thickness from below (single-sided otherwise)
    hem_in = B.tube([(0, 0.0, 0.46), (0, 0.0, 0.62)], [(0.235, 0.205), (0.20, 0.17)], 20, REG_COAT,
                    lambda i, n: W(Hips=1.0), (1, 0, 0), cap_start=False, cap_end=False)
    # flip inner wall normals by reversing the faces we just added
    n_in = 20
    for k in range(len(B.faces) - n_in, len(B.faces)):
        B.faces[k] = tuple(reversed(B.faces[k]))
    for s in range(20):  # bridge outer hem ring to inner ring
        a, b = rings[0][s], rings[0][(s + 1) % 20]
        c, d = hem_in[0][(s + 1) % 20], hem_in[0][s]
        B.faces.append((b, a, d, c))
    # skirt weights per vertex: below the belt the coat follows the thigh on its side
    for ring_list in (rings[:5], hem_in):
        for ring in ring_list:
            for vi in ring:
                co = B.verts[vi]
                if co.z > 0.97:
                    continue
                side = "Left" if co.x < 0 else "Right"
                lat = min(1.0, abs(co.x) / 0.15)
                down = min(1.0, max(0.0, (0.97 - co.z) / 0.40))
                leg_w = 0.75 * lat * down
                B.weights[vi] = blend_weights(W(Hips=1.0), {side + "UpperLeg": 1.0}, leg_w)

    # ---------------- mantle collar
    B.tube([(0, -0.005, z) for z in (1.30, 1.37, 1.44, 1.50)], [(0.225, 0.165), (0.235, 0.17), (0.19, 0.14), (0.10, 0.095)], 18,
           REG_COAT, lambda i, n: W(Chest=1.0) if i < 2 else blend_weights(W(Chest=1.0), W(Neck=1.0), 0.4 * (i - 1)), (1, 0, 0),
           cap_start=False, cap_end=False, phase=0.17, u_range=(0.0, 0.3))

    # ---------------- belt + buckle + pouches
    B.tube([(0, 0.0, 1.0), (0, 0.0, 1.05)], [(0.186, 0.150), (0.183, 0.146)], 20, REG_LEATHER, lambda i, n: W(Hips=1.0), (1, 0, 0),
           cap_start=False, cap_end=False)
    B.tube([(0, 0.145, 1.025), (0, 0.168, 1.025)], [(0.03, 0.026)] * 2, 6, REG_BRASS, lambda i, n: W(Hips=1.0), (1, 0, 0),
           cap_len=0.004, phase=math.pi / 6)
    for sx, sy, h in ((-1, 0.06, 0.09), (1, 0.03, 0.11), (1, -0.09, 0.08)):
        cx, cy = sx * 0.185, sy
        B.tube([(cx, cy, 1.0), (cx * 1.03, cy, 1.0 - h)], [(0.03, 0.05), (0.034, 0.055)], 8, REG_LEATHER,
               lambda i, n: W(Hips=1.0), (1 * sx, 0, 0), cap_len=0.02, phase=math.pi / 8)

    # ---------------- shoulder straps of the back-pack (front part)
    for sx in (-1, 1):
        pts = [(sx * 0.105, 0.128, 1.18), (sx * 0.11, 0.142, 1.32), (sx * 0.12, 0.10, 1.44), (sx * 0.125, 0.0, 1.475), (sx * 0.12, -0.10, 1.44)]
        B.tube(pts, [(0.012, 0.03)] * len(pts), 6, REG_LEATHER, lambda i, n: W(Chest=1.0), (0, 1, 0), cap_len=0.006)

    # ---------------- sleeves + hands
    for side in ("Left", "Right"):
        sx = -1.0 if side == "Left" else 1.0
        sh, ua, la = side + "Shoulder", side + "UpperArm", side + "LowerArm"
        hand = side + "Hand"
        p0 = bone_head(ua) + Vector((-sx * 0.035, 0, 0.0))
        el, wr = bone_head(la), bone_head(hand)
        d = (wr - el).normalized()
        pts = [p0, bone_head(ua).lerp(el, 0.35), bone_head(ua).lerp(el, 0.75), el, el.lerp(wr, 0.45), wr - d * 0.03, wr + d * 0.005]
        rad = [0.075, 0.068, 0.06, 0.056, 0.053, 0.062, 0.064]

        def sleeve_w(i, n, sh=sh, ua=ua, la=la):
            table = [W(**{sh: 0.6, ua: 0.4}), W(**{ua: 1.0}), W(**{ua: 0.9, la: 0.1}), W(**{ua: 0.5, la: 0.5}),
                     W(**{la: 1.0}), W(**{la: 1.0}), W(**{la: 1.0})]
            return table[i]

        B.tube(pts, rad, 12, REG_COAT, sleeve_w, (0, 1, 0), cap_start=False, cap_end=False)
        # cuff inner wall (short, reversed) so the sleeve end is not open
        B.tube([wr + d * 0.005, wr - d * 0.03], [0.058, 0.055], 12, REG_COAT, lambda i, n, la=la: W(**{la: 1.0}), (0, 1, 0),
               cap_start=False, cap_end=True, cap_len=0.01)
        # hand (mitten-like, palm facing in/down) + thumb
        up = Vector((0, 0, 1)) - d * d.z
        hp = [wr - d * 0.02, wr + d * 0.04, wr + d * 0.10, wr + d * 0.15]
        B.tube(hp, [(0.024, 0.040), (0.026, 0.045), (0.022, 0.043), (0.016, 0.034)], 10, REG_SKIN,
               lambda i, n, la=la, hand=hand: W(**{hand: 1.0}) if i else W(**{la: 0.3, hand: 0.7}), tuple(up), cap_start=False, cap_len=0.02)
        th0 = wr + d * 0.03 + Vector((0, 0.035, 0))
        B.tube([th0, th0 + d * 0.035 + Vector((0, 0.02, -0.01)), th0 + d * 0.065 + Vector((0, 0.03, -0.02))], [0.016, 0.014, 0.011], 8,
               REG_SKIN, lambda i, n, hand=hand: W(**{hand: 1.0}), (0, 0, 1), cap_start=False, cap_len=0.01)

    # ---------------- legs (trousers) + boots
    for side in ("Left", "Right"):
        ul, ll, ft, toe = side + "UpperLeg", side + "LowerLeg", side + "Foot", side + "Toes"
        hip, knee, ankle = bone_head(ul), bone_head(ll), bone_head(ft)
        pts = [hip + Vector((0, 0, 0.02)), hip.lerp(knee, 0.4), hip.lerp(knee, 0.8), knee, knee.lerp(ankle, 0.35), knee.lerp(ankle, 0.62)]
        rad = [0.088, 0.08, 0.068, 0.064, 0.06, 0.055]
        table = [W(**{"Hips": 0.5, ul: 0.5}), W(**{ul: 1.0}), W(**{ul: 0.85, ll: 0.15}), W(**{ul: 0.5, ll: 0.5}), W(**{ll: 1.0}), W(**{ll: 1.0})]
        B.tube(pts, rad, 12, REG_TROUSER, lambda i, n, t=table: t[i], (0, 1, 0), cap_start=False, cap_end=False)
        # boot shaft
        bs = [knee.lerp(ankle, 0.55), knee.lerp(ankle, 0.8), ankle + Vector((0, 0, 0.03)), ankle + Vector((0, 0.0, -0.035))]
        B.tube(bs, [0.066, 0.062, 0.058, 0.06], 12, REG_LEATHER,
               lambda i, n, ll=ll, ft=ft: [W(**{ll: 1.0}), W(**{ll: 1.0}), W(**{ll: 0.5, ft: 0.5}), W(**{ft: 1.0})][i], (0, 1, 0),
               cap_start=False, cap_end=False)
        # boot foot along +Y: heel -> toe; flat-ish sole at z = 0
        x = ankle.x
        fp = [(x, -0.075, 0.05), (x, -0.03, 0.052), (x, 0.05, 0.045), (x * 1.02, 0.12, 0.036), (x * 1.03, 0.175, 0.03)]
        fr = [(0.05, 0.045), (0.055, 0.05), (0.048, 0.05), (0.036, 0.048), (0.026, 0.04)]
        B.tube(fp, fr, 12, REG_LEATHER,
               lambda i, n, ft=ft, toe=toe: [W(**{ft: 1.0}), W(**{ft: 1.0}), W(**{ft: 1.0}), W(**{ft: 0.4, toe: 0.6}), W(**{toe: 1.0})][i],
               (0, 0, 1), cap_len=0.02)

    # ---------------- head, nose, ears, beard, hair
    hz = [1.47, 1.53, 1.585, 1.64, 1.69, 1.735, 1.77]
    hr = [(0.052, 0.055), (0.056, 0.06), (0.082, 0.092), (0.09, 0.1), (0.088, 0.098), (0.074, 0.084), (0.045, 0.05)]
    B.tube([(0, 0.012, z) for z in hz], hr, 16, REG_SKIN,
           lambda i, n: [W(Neck=1.0), W(Neck=0.6, Head=0.4), W(Head=1.0), W(Head=1.0), W(Head=1.0), W(Head=1.0), W(Head=1.0)][i],
           (1, 0, 0), cap_start=False, cap_len=0.03)
    B.tube([(0, 0.105, 1.668), (0, 0.128, 1.646), (0, 0.133, 1.632)], [(0.016, 0.014), (0.014, 0.012), (0.01, 0.009)], 8, REG_SKIN,
           lambda i, n: W(Head=1.0), (1, 0, 0), cap_start=False, cap_len=0.008)
    for sx in (-1, 1):
        B.tube([(sx * 0.086, 0.0, 1.66), (sx * 0.102, -0.005, 1.665)], [(0.022, 0.01), (0.017, 0.008)], 6, REG_SKIN,
               lambda i, n: W(Head=1.0), (0, 0, 1), cap_start=False, cap_len=0.006)
    beard = [(0, 0.068, 1.63), (0, 0.098, 1.585), (0, 0.112, 1.53), (0, 0.11, 1.47), (0, 0.098, 1.42)]
    B.tube(beard, [(0.078, 0.04), (0.074, 0.042), (0.06, 0.036), (0.04, 0.026), (0.016, 0.012)], 12, REG_HAIR,
           lambda i, n: W(Head=1.0), (1, 0, 0), cap_start=False, cap_len=0.015)
    # moustache
    B.tube([(-0.05, 0.1, 1.6), (0, 0.124, 1.615), (0.05, 0.1, 1.6)], [0.012, 0.016, 0.012], 8, REG_HAIR, lambda i, n: W(Head=1.0),
           (0, 0, 1), cap_len=0.012)
    # hair at the back under the hat
    B.tube([(0, -0.04, 1.74), (0, -0.075, 1.66), (0, -0.06, 1.58)], [(0.085, 0.04), (0.088, 0.045), (0.07, 0.03)], 12, REG_HAIR,
           lambda i, n: W(Head=1.0), (1, 0, 0), cap_len=0.015)

    # ---------------- wide-brimmed hat (slightly tilted back) with a leather band and a feather-less pin
    tilt = Matrix.Rotation(math.radians(-6), 4, "X")
    c0 = Vector((0, 0.005, 1.725))

    def hp_(x, y, z):
        return tuple(c0 + tilt @ Vector((x, y, z)))

    B.tube([hp_(0, 0, -0.012), hp_(0, 0, 0.004)], [(0.255, 0.265), (0.25, 0.26)], 24, REG_HAT, lambda i, n: W(Head=1.0), (1, 0, 0),
           cap_len=0.006)
    B.tube([hp_(0, 0, 0.0), hp_(0, 0, 0.05), hp_(0, -0.005, 0.12), hp_(0, -0.015, 0.165)],
           [(0.105, 0.115), (0.103, 0.112), (0.088, 0.096), (0.06, 0.066)], 16, REG_HAT, lambda i, n: W(Head=1.0), (1, 0, 0),
           cap_start=False, cap_len=0.025)
    B.tube([hp_(0, 0, 0.004), hp_(0, 0, 0.035)], [(0.108, 0.118), (0.107, 0.116)], 16, REG_LEATHER, lambda i, n: W(Head=1.0), (1, 0, 0),
           cap_start=False, cap_end=False)
    B.tube([hp_(0.07, 0.093, 0.02), hp_(0.075, 0.1, 0.02)], [0.014, 0.014], 6, REG_BRASS, lambda i, n: W(Head=1.0), (0, 0, 1), cap_len=0.004)

    # ---------------- back-pack: wooden frame, bundle, bedroll, pot, lantern (all rigid to Chest)
    cw = lambda i, n: W(Chest=1.0)  # noqa: E731
    by = -0.255
    B.tube([(0, by, 1.02), (0, by, 1.05), (0, by, 1.46), (0, by, 1.50)], [(0.20, 0.13), (0.235, 0.155), (0.235, 0.155), (0.205, 0.13)],
           8, REG_CANVAS, cw, (1, 0, 0), cap_len=0.015, phase=math.pi / 8, u_range=(0.0, 1.0))
    for sx in (-1, 1):
        B.tube([(sx * 0.2, -0.36, 0.94), (sx * 0.2, -0.36, 1.82)], [0.016, 0.014], 6, REG_WOOD, cw, (0, 1, 0), cap_len=0.01)
        B.tube([(sx * 0.2, -0.36, 1.02), (sx * 0.2, -0.14, 1.02)], [0.012, 0.012], 6, REG_WOOD, cw, (0, 0, 1), cap_len=0.006)
    for z in (1.0, 1.52, 1.78):
        B.tube([(-0.215, -0.36, z), (0.215, -0.36, z)], [0.013, 0.013], 6, REG_WOOD, cw, (0, 0, 1), cap_len=0.006)
    B.tube([(-0.25, -0.27, 1.58), (-0.2, -0.27, 1.58), (0.2, -0.27, 1.58), (0.25, -0.27, 1.58)], [0.06, 0.072, 0.072, 0.06], 12,
           REG_CANVAS, cw, (0, 0, 1), cap_len=0.02, u_range=(0.0, 1.0))
    # cooking pot hanging on the right, lantern on the left
    B.tube([(0.245, -0.30, 1.16), (0.245, -0.30, 1.20), (0.245, -0.30, 1.27), (0.245, -0.30, 1.285)], [0.045, 0.062, 0.066, 0.06], 12,
           REG_BRASS, cw, (1, 0, 0), cap_len=0.006)
    B.tube([(-0.25, -0.31, 1.13), (-0.25, -0.31, 1.15), (-0.25, -0.31, 1.25), (-0.25, -0.31, 1.27), (-0.25, -0.31, 1.31)],
           [0.04, 0.042, 0.042, 0.04, 0.012], 6, REG_BRASS, cw, (1, 0, 0), cap_len=0.008, u_range=(0.0, 1.0))

    obj = B.to_object(name)
    return obj


# Rough object-space points used by the painter (eyes on the face, lantern glass)
EYES = [Vector((-0.036, 0.098, 1.678)), Vector((0.036, 0.098, 1.678))]
LANTERN = Vector((-0.25, -0.31, 1.20))
