"""METINAR-016 — Wandering Merchant: procedural low-poly geometry, UV layout and humanoid rig.

Original MetinAR asset, built only from the primitives in this file (no imported meshes, scans or references).
Blender space: Z up, metres, the character faces -Y (Blender Front view); its left side is +X.
Every part is generated with explicit per-loop UVs inside one cell of an 8x8 palette atlas (merchant_texture.py)
and explicit skin weights, so the result is deterministic.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

# ------------------------------------------------------------------------------------------------ atlas cells
GRID = 8
CELLS = {
    "skin": (0, 0), "beard": (1, 0), "brow": (2, 0), "eye": (3, 0), "lip": (4, 0),
    "hat": (0, 1), "hatband": (1, 1), "scarf": (2, 1), "shirt": (3, 1),
    "coat": (0, 2), "sleeve": (1, 2), "trousers": (2, 2), "boot": (3, 2), "sole": (4, 2),
    "belt": (0, 3), "brass": (1, 3), "pouch": (2, 3), "strap": (3, 3),
    "canvas": (0, 4), "blanket": (1, 4), "copper": (2, 4), "iron": (3, 4), "glass": (4, 4), "flap": (5, 4),
}
CELL_INSET = 5.0 / 128.0  # keep 5 px of the 128 px cell free on each side (mip bleed stays inside the cell)

# ------------------------------------------------------------------------------------------------ rig
# Unity Humanoid bone names (auto-mapped by the importer). (name, parent, head, tail)
BONES = [
    ("Root", None, (0, 0, 0), (0, 0.12, 0)),
    ("Hips", "Root", (0, 0.0, 0.95), (0, 0.0, 1.06)),
    ("Spine", "Hips", (0, 0.0, 1.06), (0, 0.005, 1.22)),
    ("Chest", "Spine", (0, 0.005, 1.22), (0, 0.0, 1.43)),
    ("Neck", "Chest", (0, 0.0, 1.43), (0, -0.01, 1.53)),
    ("Head", "Neck", (0, -0.01, 1.53), (0, -0.01, 1.76)),
    ("Jaw", "Head", (0, -0.03, 1.60), (0, -0.10, 1.54)),
    ("LeftShoulder", "Chest", (0.03, 0.0, 1.39), (0.17, 0.0, 1.41)),
    ("LeftUpperArm", "LeftShoulder", (0.17, 0.0, 1.41), (0.45, 0.0, 1.41)),
    ("LeftLowerArm", "LeftUpperArm", (0.45, 0.0, 1.41), (0.70, -0.005, 1.41)),
    ("LeftHand", "LeftLowerArm", (0.70, -0.005, 1.41), (0.84, -0.005, 1.41)),
    ("LeftUpperLeg", "Hips", (0.10, 0.0, 0.93), (0.10, -0.015, 0.51)),
    ("LeftLowerLeg", "LeftUpperLeg", (0.10, -0.015, 0.51), (0.10, 0.015, 0.095)),
    ("LeftFoot", "LeftLowerLeg", (0.10, 0.015, 0.095), (0.10, -0.10, 0.03)),
    ("LeftToes", "LeftFoot", (0.10, -0.10, 0.03), (0.10, -0.17, 0.03)),
    ("Backpack", "Chest", (0, 0.24, 1.12), (0, 0.24, 1.46)),
    ("Lantern", "Backpack", (0.235, 0.25, 1.10), (0.235, 0.25, 0.95)),
]


def _mirror(b):
    name, parent, h, t = b
    if not name.startswith("Left"):
        return None
    m = lambda p: (-p[0], p[1], p[2])  # noqa: E731
    return ("Right" + name[4:], ("Right" + parent[4:]) if parent.startswith("Left") else parent, m(h), m(t))


BONES = BONES + [mb for mb in (_mirror(b) for b in BONES) if mb]
DEFORM_BONES = [b[0] for b in BONES]
BONE_DATA = {b[0]: b for b in BONES}
# IK helpers (not deform, never exported: use_armature_deform_only)
CONTROL_BONES = [
    # same rest head/tail as the Foot bones: Foot copies the IK target's world rotation (flat on the ground)
    ("IK_Foot_L", "Root", (0.10, 0.015, 0.095), (0.10, -0.10, 0.03)),
    ("IK_Foot_R", "Root", (-0.10, 0.015, 0.095), (-0.10, -0.10, 0.03)),
    ("Pole_Knee_L", "Root", (0.10, -0.60, 0.52), (0.10, -0.65, 0.52)),
    ("Pole_Knee_R", "Root", (-0.10, -0.60, 0.52), (-0.10, -0.65, 0.52)),
]


BONE_DATA_ALL = {b[0]: b for b in BONES + CONTROL_BONES}


# ------------------------------------------------------------------------------------------------ builder
class Builder:
    def __init__(self):
        self.verts = []      # Vector
        self.weights = []    # dict bone -> w
        self.faces = []      # (vert idx tuple, uv tuple (cell-local 0..1), cell name)

    # weights ---------------------------------------------------------------------------------------
    def add_vert(self, co, wspec):
        co = Vector(co)
        self.verts.append(co)
        self.weights.append(_weights(co, wspec))
        return len(self.verts) - 1

    def face(self, idx, uvs, cell):
        self.faces.append((tuple(idx), tuple(tuple(u) for u in uvs), cell))

    # primitives ------------------------------------------------------------------------------------
    def loft(self, rings, cell, wspec, segs=12, cap0=True, cap1=True, v_range=(0.0, 1.0), phase=0.0):
        """rings: list of (center, axis_dir, rx, ry, up_hint). Builds a tube with cylindrical UVs."""
        n = len(rings)
        idx = []
        for (c, d, rx, ry, up) in rings:
            c, d = Vector(c), Vector(d).normalized()
            up = Vector(up)
            u_ax = (up - d * up.dot(d)).normalized()  # 'ry' direction
            r_ax = d.cross(u_ax).normalized()          # 'rx' direction
            ring = []
            for j in range(segs):
                a = 2 * math.pi * j / segs + phase
                ring.append(self.add_vert(c + r_ax * (rx * math.cos(a)) + u_ax * (ry * math.sin(a)), wspec))
            idx.append(ring)
        v0, v1 = v_range
        for i in range(n - 1):
            va, vb = v0 + (v1 - v0) * i / (n - 1), v0 + (v1 - v0) * (i + 1) / (n - 1)
            for j in range(segs):
                j2 = (j + 1) % segs
                ua, ub = j / segs, (j + 1) / segs
                self.face((idx[i][j], idx[i][j2], idx[i + 1][j2], idx[i + 1][j]),
                          ((ua, va), (ub, va), (ub, vb), (ua, vb)), cell)
        for cap, ring, rev in ((cap0, idx[0], True), (cap1, idx[-1], False)):
            if not cap:
                continue
            order = list(reversed(ring)) if rev else list(ring)
            uvs = []
            for j in range(segs):
                a = 2 * math.pi * j / segs
                uvs.append((0.5 + 0.45 * math.cos(a), 0.5 + 0.45 * math.sin(a)))
            if rev:
                uvs = list(reversed(uvs))
            # fan around an n-gon -> triangulated by the exporter consistently; use real triangles here
            center_co = sum((self.verts[k] for k in ring), Vector()) / len(ring)
            if cap == "point":
                center_co = center_co + (Vector(rings[0][1]).normalized() * (-1 if rev else 1)) * 0.0
            ci = self.add_vert(center_co, wspec)
            for j in range(segs):
                j2 = (j + 1) % segs
                self.face((order[j], order[j2], ci), (uvs[j], uvs[j2], (0.5, 0.5)), cell)
        return idx

    def tube_z(self, profile, cell, wspec, segs=12, center=(0, 0), **kw):
        """profile: list of (z, rx, ry[, dx, dy]) along +Z."""
        rings = []
        for p in profile:
            z, rx, ry = p[0], p[1], p[2]
            dx, dy = (p[3], p[4]) if len(p) > 3 else (0.0, 0.0)
            rings.append(((center[0] + dx, center[1] + dy, z), (0, 0, 1), rx, ry, (0, 1, 0)))
        return self.loft(rings, cell, wspec, segs=segs, **kw)

    def tube_path(self, pts, radii, cell, wspec, segs=8, up=(0, 0, 1), **kw):
        rings = []
        for i, p in enumerate(pts):
            p = Vector(p)
            a = Vector(pts[max(i - 1, 0)])
            b = Vector(pts[min(i + 1, len(pts) - 1)])
            d = (b - a).normalized()
            rx, ry = radii[i]
            rings.append((p, d, rx, ry, up))
        return self.loft(rings, cell, wspec, segs=segs, **kw)

    def box(self, center, size, cell, wspec, taper=(1.0, 1.0), rot=None):
        """Axis-aligned box (optionally top face scaled by taper (x, y)); every face uses the whole cell."""
        cx, cy, cz = center
        sx, sy, sz = (s / 2 for s in size)
        corners = []
        for k in range(8):
            x = -sx if k & 1 == 0 else sx
            y = -sy if k & 2 == 0 else sy
            z = -sz if k & 4 == 0 else sz
            if z > 0:
                x, y = x * taper[0], y * taper[1]
            v = Vector((x, y, z))
            if rot is not None:
                v = rot @ v
            corners.append(self.add_vert(Vector((cx, cy, cz)) + v, wspec))
        quads = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
        uv = ((0, 0), (1, 0), (1, 1), (0, 1))
        for q in quads:
            self.face([corners[i] for i in q], uv, cell)

    # -----------------------------------------------------------------------------------------------
    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        bm = bmesh.new()
        bverts = [bm.verts.new(v) for v in self.verts]
        bm.verts.ensure_lookup_table()
        uv_layer = bm.loops.layers.uv.new("UVMap")
        for idx, uvs, cell in self.faces:
            try:
                f = bm.faces.new([bverts[i] for i in idx])
            except ValueError:
                continue
            cx, cy = CELLS[cell]
            for loop, (u, v) in zip(f.loops, uvs):
                u = CELL_INSET + (1 - 2 * CELL_INSET) * min(max(u, 0.0), 1.0)
                v = CELL_INSET + (1 - 2 * CELL_INSET) * min(max(v, 0.0), 1.0)
                loop[uv_layer].uv = ((cx + u) / GRID, (cy + v) / GRID)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        bm.to_mesh(me)
        bm.free()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        for bone in DEFORM_BONES:
            obj.vertex_groups.new(name=bone)
        for i, w in enumerate(self.weights):
            for bone, val in w.items():
                if val > 1e-4:
                    obj.vertex_groups[bone].add([i], val, "REPLACE")
        return obj


# ------------------------------------------------------------------------------------------------ weights
def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def _chain(co, bones, power=6.0, keep=2):
    ws = {}
    for b in bones:
        _, _, h, t = BONE_DATA[b]
        d = _seg_dist(co, Vector(h), Vector(t))
        ws[b] = 1.0 / (d + 0.02) ** power
    top = sorted(ws.items(), key=lambda kv: -kv[1])[:keep]
    s = sum(v for _, v in top)
    return {k: v / s for k, v in top}


def _smooth(x, e0, e1):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def _weights(co, spec):
    kind = spec[0]
    if kind == "rigid":
        return {spec[1]: 1.0}
    if kind == "chain":
        return _chain(co, spec[1], *spec[2:])
    if kind == "func":
        return spec[1](co)
    raise ValueError(spec)


def _w_body(co):
    """Coat / torso: Hips-Spine-Chest by height, shoulders near the arm sockets, legs below the waist."""
    z, x = co.z, co.x
    side = "Left" if x >= 0 else "Right"
    w = {}
    if z < 0.97:
        # skirt: follows the leg on its side more the lower it is, centre panels stay with the hips
        leg = 0.85 * (1 - _smooth(z, 0.48, 0.97)) * _smooth(abs(x), 0.0, 0.11)
        w["Hips"] = 1 - leg
        if leg > 0:
            w[side + "UpperLeg"] = leg
        return w
    a = 1 - _smooth(z, 1.0, 1.14)          # hips share
    c = _smooth(z, 1.20, 1.34)             # chest share
    s = max(0.0, 1 - a - c)
    for k, v in (("Hips", a), ("Spine", s), ("Chest", c)):
        if v > 1e-3:
            w[k] = v
    sh = _smooth(abs(x), 0.10, 0.17) * _smooth(z, 1.28, 1.40)
    if sh > 0:
        w = {k: v * (1 - sh) for k, v in w.items()}
        w[side + "Shoulder"] = w.get(side + "Shoulder", 0) + sh
    if z > 1.44:
        n = _smooth(z, 1.44, 1.50)
        w = {k: v * (1 - n) for k, v in w.items()}
        w["Neck"] = w.get("Neck", 0) + n
    return w


def _w_head(co):
    w = {"Head": 1.0}
    if co.z < 1.56:
        n = 1 - _smooth(co.z, 1.49, 1.56)
        w = {"Head": 1 - n, "Neck": n}
    return w


def _w_beard(co):
    j = 1 - _smooth(co.z, 1.56, 1.61)
    return {"Jaw": j, "Head": 1 - j} if 0 < j < 1 else ({"Jaw": 1.0} if j >= 1 else {"Head": 1.0})


def _w_leg(side):
    s = side
    return ("chain", [s + "UpperLeg", s + "LowerLeg", s + "Foot"], 6.0, 2)


def _w_arm(side):
    s = side
    return ("chain", [s + "Shoulder", s + "UpperArm", s + "LowerArm", s + "Hand"], 6.0, 2)


# ------------------------------------------------------------------------------------------------ parts
def _legs(B):
    for sgn, side in ((1, "Left"), (-1, "Right")):
        x = 0.10 * sgn
        # trousers (mostly hidden by the coat above the knee)
        prof = [(0.27, 0.058, 0.060, 0, 0.014), (0.40, 0.066, 0.066, 0, 0.0), (0.51, 0.070, 0.072, 0, -0.012),
                (0.64, 0.074, 0.078, 0, -0.008), (0.80, 0.082, 0.086, 0, -0.004), (0.93, 0.090, 0.090, 0, 0)]
        B.tube_z(prof, "trousers", _w_leg(side), segs=12, center=(x, 0.0), cap0=False, cap1=False)
        # boot shaft
        prof = [(0.06, 0.056, 0.064, 0, 0.012), (0.18, 0.058, 0.062, 0, 0.014), (0.31, 0.068, 0.070, 0, 0.012),
                (0.33, 0.072, 0.074, 0, 0.012)]
        B.tube_z(prof, "boot", ("chain", [side + "LowerLeg", side + "Foot"], 6.0, 2), segs=12, center=(x, 0.0), cap0=False, cap1=False)
        # boot foot: heel box + toe wedge
        B.box((x, 0.0, 0.055), (0.105, 0.13, 0.09), "boot", ("rigid", side + "Foot"))
        B.box((x, -0.115, 0.042), (0.10, 0.11, 0.07), "boot", ("chain", [side + "Foot", side + "Toes"], 6.0, 2), taper=(0.9, 0.7))
        B.box((x, -0.045, 0.006), (0.112, 0.29, 0.012), "sole", ("chain", [side + "Foot", side + "Toes"], 4.0, 2))


def _body(B):
    # coat: hem (knee) -> waist -> chest -> shoulders, slight belly, collar opening at the neck
    prof = [
        (0.46, 0.255, 0.230, 0, 0.0),
        (0.56, 0.240, 0.215, 0, 0.0),
        (0.72, 0.215, 0.190, 0, -0.005),
        (0.88, 0.200, 0.180, 0, -0.012),
        (1.00, 0.192, 0.180, 0, -0.022),
        (1.10, 0.190, 0.172, 0, -0.024),
        (1.22, 0.192, 0.150, 0, -0.012),
        (1.32, 0.196, 0.135, 0, -0.004),
        (1.40, 0.180, 0.118, 0, 0.004),
        (1.45, 0.120, 0.090, 0, 0.004),
        (1.48, 0.070, 0.064, 0, 0.0),
    ]
    B.tube_z(prof, "coat", ("func", _w_body), segs=24, cap0=False, cap1=False, phase=0.0)
    # hem inner lining (gives the skirt thickness when seen from below)
    prof = [(0.462, 0.245, 0.220, 0, 0.0), (0.60, 0.205, 0.185, 0, 0.0)]
    B.tube_z(prof, "trousers", ("func", _w_body), segs=24, cap0=False, cap1=False)
    # belt + buckle + pouches
    prof = [(0.975, 0.198, 0.188, 0, -0.022), (1.035, 0.198, 0.188, 0, -0.023)]
    B.tube_z(prof, "belt", ("func", _w_body), segs=24, cap0=False, cap1=False)
    B.box((0, -0.212, 1.005), (0.06, 0.016, 0.05), "brass", ("rigid", "Hips"))
    for sgn in (1, -1):
        B.box((0.165 * sgn, -0.112, 0.935), (0.075, 0.05, 0.085), "pouch", ("rigid", "Hips"), taper=(1.0, 1.15))
        B.box((0.165 * sgn, -0.139, 0.965), (0.08, 0.012, 0.04), "pouch", ("rigid", "Hips"))
    # scarf around the neck with a hanging tail
    B.loft([((0, 0.0, 1.47), (0, 0, 1), 0.105, 0.098, (0, 1, 0)), ((0, 0.0, 1.52), (0, 0, 1), 0.088, 0.084, (0, 1, 0))],
           "scarf", ("chain", ["Neck", "Chest"], 4.0, 2), segs=12, cap0=False, cap1=False)
    B.tube_path([(0.04, -0.105, 1.47), (0.06, -0.135, 1.36), (0.07, -0.150, 1.24)], [(0.035, 0.008), (0.035, 0.008), (0.03, 0.008)],
                "scarf", ("chain", ["Chest", "Neck"], 4.0, 2), segs=4, up=(0, 1, 0))
    # shoulder straps of the backpack
    for sgn in (1, -1):
        pts = [(0.10 * sgn, 0.17, 1.44), (0.115 * sgn, 0.05, 1.47), (0.12 * sgn, -0.10, 1.40), (0.13 * sgn, -0.165, 1.24), (0.14 * sgn, -0.20, 1.10)]
        B.tube_path(pts, [(0.026, 0.008)] * 5, "strap", ("chain", ["Chest", "Spine"], 4.0, 2), segs=4, up=(0, 0, 1))


def _arms(B):
    for sgn, side in ((1, "Left"), (-1, "Right")):
        s = sgn
        pts = [(0.13 * s, 0.0, 1.405), (0.20 * s, 0.0, 1.41), (0.33 * s, 0.0, 1.41), (0.45 * s, 0.0, 1.41), (0.58 * s, -0.002, 1.41), (0.665 * s, -0.004, 1.41)]
        rad = [(0.090, 0.085), (0.078, 0.076), (0.068, 0.066), (0.060, 0.058), (0.060, 0.058), (0.072, 0.070)]
        B.tube_path(pts, rad, "sleeve", _w_arm(side), segs=12, up=(0, 0, 1), cap0=False, cap1=False)
        # cuff
        B.tube_path([(0.63 * s, -0.004, 1.41), (0.69 * s, -0.005, 1.41)], [(0.074, 0.072), (0.072, 0.070)], "hatband",
                    ("chain", [side + "LowerArm", side + "Hand"], 6.0, 2), segs=12, up=(0, 0, 1), cap0=False, cap1="point")
        # wrist + hand (palm faces down in the T-pose, thumb forward)
        B.tube_path([(0.665 * s, -0.005, 1.41), (0.72 * s, -0.005, 1.41)], [(0.032, 0.026), (0.036, 0.022)], "skin",
                    ("chain", [side + "LowerArm", side + "Hand"], 6.0, 2), segs=8, up=(0, 0, 1), cap0=False, cap1=False)
        B.box((0.77 * s, -0.005, 1.41), (0.10, 0.085, 0.036), "skin", ("rigid", side + "Hand"), )
        B.box((0.845 * s, -0.005, 1.403), (0.05, 0.080, 0.028), "skin", ("rigid", side + "Hand"))
        B.box((0.755 * s, -0.062, 1.405), (0.055, 0.03, 0.028), "skin", ("rigid", side + "Hand"),
              rot=Matrix.Rotation(math.radians(-25 * s), 3, "Z"))


def _head(B):
    prof = [(1.48, 0.050, 0.050, 0, 0.0), (1.53, 0.054, 0.056, 0, -0.004), (1.555, 0.074, 0.082, 0, -0.012),
            (1.58, 0.088, 0.098, 0, -0.014), (1.61, 0.095, 0.105, 0, -0.012), (1.64, 0.098, 0.108, 0, -0.009),
            (1.675, 0.098, 0.107, 0, -0.006), (1.71, 0.092, 0.101, 0, -0.003), (1.74, 0.080, 0.088, 0, 0.0),
            (1.765, 0.058, 0.064, 0, 0.002), (1.782, 0.028, 0.030, 0, 0.002)]
    B.tube_z(prof, "skin", ("func", _w_head), segs=18, cap0=False, cap1=True)
    # ears
    for s in (1, -1):
        B.box((0.098 * s, 0.0, 1.645), (0.022, 0.04, 0.05), "skin", ("rigid", "Head"))
    # nose
    B.box((0, -0.118, 1.635), (0.032, 0.03, 0.05), "skin", ("rigid", "Head"), taper=(0.6, 0.5))
    # eyes + brows
    for s in (1, -1):
        B.box((0.038 * s, -0.104, 1.665), (0.026, 0.012, 0.018), "eye", ("rigid", "Head"))
        B.box((0.040 * s, -0.108, 1.688), (0.040, 0.016, 0.012), "brow", ("rigid", "Head"),
              rot=Matrix.Rotation(math.radians(-8 * s), 3, "Y"))
    # moustache (upper lip, stays with the head)
    for s in (1, -1):
        B.box((0.030 * s, -0.116, 1.602), (0.055, 0.02, 0.018), "beard", ("rigid", "Head"),
              rot=Matrix.Rotation(math.radians(14 * s), 3, "Y"))
    # mouth (dark lip line on the jaw)
    B.box((0, -0.108, 1.585), (0.04, 0.01, 0.008), "lip", ("rigid", "Jaw"))
    # beard: from the jaw line down to a rounded point on the chest
    rings = [((0, -0.050, 1.600), (0, -0.25, -1), 0.090, 0.058, (0, -1, 0.2)),
             ((0, -0.078, 1.540), (0, -0.30, -1), 0.082, 0.052, (0, -1, 0.2)),
             ((0, -0.100, 1.470), (0, -0.25, -1), 0.064, 0.042, (0, -1, 0.2)),
             ((0, -0.114, 1.405), (0, -0.15, -1), 0.040, 0.028, (0, -1, 0.2)),
             ((0, -0.118, 1.365), (0, -0.10, -1), 0.012, 0.010, (0, -1, 0.2))]
    B.loft(rings, "beard", ("func", _w_beard), segs=14, cap0=False, cap1=True)


HAT_SEGS = 24


def _hat(B):
    w = ("rigid", "Head")
    tilt = Matrix.Rotation(math.radians(-6), 3, "X")  # brim slightly down at the front
    base = Vector((0, 0.005, 1.722))

    def ring(r_x, r_y, dz):
        return [base + tilt @ Vector((r_x * math.cos(a), r_y * math.sin(a), dz)) for a in (2 * math.pi * j / HAT_SEGS for j in range(HAT_SEGS))]

    # brim: outer rim (top/bottom) and inner ring, built as a closed flat tube
    outer_t, outer_b = ring(0.27, 0.255, 0.012), ring(0.27, 0.255, -0.002)
    inner_t, inner_b = ring(0.105, 0.115, 0.012), ring(0.105, 0.115, -0.002)
    loops = [inner_t, outer_t, outer_b, inner_b]
    vids = [[B.add_vert(p, w) for p in L] for L in loops]
    for k in range(len(vids)):
        A, C = vids[k], vids[(k + 1) % len(vids)]
        for j in range(HAT_SEGS):
            j2 = (j + 1) % HAT_SEGS
            ua, ub = j / HAT_SEGS, (j + 1) / HAT_SEGS
            B.face((A[j], A[j2], C[j2], C[j]), ((ua, k / 4), (ub, k / 4), (ub, (k + 1) / 4), (ua, (k + 1) / 4)), "hat")
    # crown (slightly bent tip towards the back)
    crown = []
    for (dz, rx, ry, dy) in ((0.0, 0.108, 0.118, 0.0), (0.03, 0.104, 0.112, 0.004), (0.09, 0.090, 0.098, 0.012),
                             (0.15, 0.064, 0.070, 0.030), (0.19, 0.030, 0.034, 0.055), (0.205, 0.010, 0.012, 0.07)):
        c = base + tilt @ Vector((0, dy, dz))
        crown.append((c, tilt @ Vector((0, 0, 1)), rx, ry, (0, 1, 0)))
    B.loft(crown, "hat", w, segs=HAT_SEGS, cap0=False, cap1=True)
    # hat band + feather
    band = [(base + tilt @ Vector((0, 0, 0.012)), tilt @ Vector((0, 0, 1)), 0.112, 0.122, (0, 1, 0)),
            (base + tilt @ Vector((0, 0.002, 0.045)), tilt @ Vector((0, 0, 1)), 0.106, 0.115, (0, 1, 0))]
    B.loft(band, "hatband", w, segs=HAT_SEGS, cap0=False, cap1=False)
    B.tube_path([(0.10, 0.03, 1.76), (0.14, 0.07, 1.86), (0.15, 0.13, 1.93)], [(0.018, 0.004), (0.026, 0.004), (0.006, 0.003)],
                "scarf", w, segs=4, up=(1, 0, 0))


def _pack(B):
    w = ("rigid", "Backpack")
    # main bag (tapered towards the top), flap, side pocket
    B.box((0, 0.27, 1.20), (0.36, 0.22, 0.48), "canvas", w, taper=(0.92, 0.85))
    B.box((0, 0.255, 1.39), (0.34, 0.205, 0.08), "flap", w)
    B.box((0, 0.17, 1.32), (0.30, 0.04, 0.16), "flap", w)
    B.box((0, 0.395, 1.12), (0.22, 0.06, 0.20), "canvas", w)
    for s in (1, -1):
        B.box((0.08 * s, 0.152, 1.29), (0.03, 0.01, 0.06), "brass", w)
    # bedroll on top, with two tie straps
    B.tube_path([(-0.23, 0.27, 1.50), (0.23, 0.27, 1.50)], [(0.075, 0.070), (0.075, 0.070)], "blanket", w, segs=14, up=(0, 0, 1))
    for s in (1, -1):
        B.tube_path([(0.13 * s - 0.012, 0.27, 1.50), (0.13 * s + 0.012, 0.27, 1.50)], [(0.081, 0.076), (0.081, 0.076)], "strap", w,
                    segs=14, up=(0, 0, 1), cap0=False, cap1=False)
    # copper pot hanging on the right side of the bag
    B.tube_z([(1.01, 0.050, 0.050), (1.03, 0.066, 0.066), (1.10, 0.070, 0.070), (1.13, 0.064, 0.064), (1.14, 0.068, 0.068)],
             "copper", w, segs=12, center=(-0.255, 0.28), cap0=True, cap1=True)
    B.tube_path([(-0.255, 0.28, 1.14), (-0.255, 0.28, 1.20), (-0.21, 0.28, 1.24)], [(0.006, 0.006)] * 3, "iron", w, segs=4)


def _lantern(B):
    w = ("rigid", "Lantern")
    x, y = 0.235, 0.25
    B.tube_path([(x, y, 1.12), (x, y, 1.065)], [(0.006, 0.006)] * 2, "iron", w, segs=4)
    B.box((x, y, 1.055), (0.075, 0.075, 0.02), "iron", w, taper=(0.5, 0.5))
    B.box((x, y, 0.995), (0.06, 0.06, 0.10), "glass", w)
    for dx in (-1, 1):
        for dy in (-1, 1):
            B.box((x + 0.031 * dx, y + 0.031 * dy, 0.995), (0.01, 0.01, 0.10), "iron", w)
    B.box((x, y, 0.94), (0.08, 0.08, 0.016), "iron", w)


def build_mesh(name="SK_Npc_WanderingMerchant_LOD0"):
    B = Builder()
    _legs(B)
    _body(B)
    _arms(B)
    _head(B)
    _hat(B)
    _pack(B)
    _lantern(B)
    obj = B.to_object(name)
    # smooth shading with hard edges above 40 degrees (low-poly silhouette, smooth cloth)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    obj.select_set(False)
    return obj


# ------------------------------------------------------------------------------------------------ armature
def build_armature(name="Armature"):
    arm = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    eb = {}
    for bname, parent, h, t in BONES + CONTROL_BONES:
        b = arm.edit_bones.new(bname)
        b.head, b.tail = Vector(h), Vector(t)
        b.roll = 0.0
        b.use_deform = bname in DEFORM_BONES
        eb[bname] = b
    for bname, parent, h, t in BONES + CONTROL_BONES:
        if parent:
            eb[bname].parent = eb[parent]
            eb[bname].use_connect = False
    bpy.ops.object.mode_set(mode="POSE")
    for side, s in (("Left", "L"), ("Right", "R")):
        pb = rig.pose.bones[side + "LowerLeg"]
        ik = pb.constraints.new("IK")
        ik.target, ik.subtarget = rig, "IK_Foot_" + s
        ik.pole_target, ik.pole_subtarget = rig, "Pole_Knee_" + s
        ik.pole_angle = math.radians(-90)
        ik.chain_count = 2
        ik.use_tail = True
        cr = rig.pose.bones[side + "Foot"].constraints.new("COPY_ROTATION")
        cr.target, cr.subtarget = rig, "IK_Foot_" + s
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def bind(mesh, rig):
    mesh.parent = rig
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    mod.use_vertex_groups = True


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)
