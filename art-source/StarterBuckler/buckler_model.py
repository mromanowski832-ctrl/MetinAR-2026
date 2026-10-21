"""METINAR-032 — Lipowy Puklerz (starter_buckler): original procedural low-poly FPP buckler.

Authoring space (Blender, METINAR-009 section 3.1 front-view convention): the shield's FRONT faces Blender -Y,
up = +Z, pivot (object origin) = centre of the grip where the left fist closes. The build turns mesh + sockets
180 deg about Z for export, so after Unity's Bake Axis Conversion the front faces Unity +Z and the handle runs
along Unity +Y (same grip-frame convention as the PR #32/#36 hand frames: fist wraps +Y, forward +Z).

All parts are lathes/tubes with hand-assigned UVs into a fixed 512 x 512 atlas (see UV_RECTS), so the texture
painter works directly in UV space (no bake). Distances: d = distance in front of the grip (towards the front face).
"""
import math

import bmesh
import bpy
from mathutils import Vector

R_SHIELD = 0.170          # planks radius (34 cm buckler)
R_EDGE = 0.172
SEG = 32                  # radial segments of planks/rim
SEG_BOSS = 24
BACK_D = 0.035            # back face distance in front of the grip at the rim
THICK = 0.012
DOME = 0.018              # planks dome height (centre vs rim)
BOSS_R = 0.064
BOSS_H = 0.048

# UV atlas rectangles (u0, v0, u1, v1) in 0..1, v up (Blender convention)
UV_RECTS = {
    "front": (0.0, 0.5, 0.5, 1.0),
    "back": (0.5, 0.5, 1.0, 1.0),
    "boss": (0.0, 0.25, 0.25, 0.5),
    "rivet": (0.25, 0.25, 0.3125, 0.3125),
    "handle": (0.5, 0.25, 1.0, 0.5),
    "rim": (0.0, 0.0, 1.0, 0.125),
    "edge": (0.0, 0.14, 1.0, 0.20),
}
PART_ORDER = list(UV_RECTS)
RIVETS = 8
RIVET_R = 0.145


def d_front(r):
    return BACK_D + THICK + DOME * (1.0 - (r / R_SHIELD) ** 2)


def d_back(r):
    return BACK_D + DOME * (1.0 - (r / R_SHIELD) ** 2)


def P(r, d, a):
    """Lathe point: radius r, distance d in front of the grip (towards Blender -Y), angle a around the axis."""
    return Vector((r * math.cos(a), -d, r * math.sin(a)))


def _uv_in(rect, s, t):
    u0, v0, u1, v1 = rect
    return (u0 + (u1 - u0) * s, v0 + (v1 - v0) * t)


class Mesh:
    def __init__(self):
        self.verts, self.faces = [], []  # faces: (vertex ids, uvs, part)

    def v(self, co):
        self.verts.append(Vector(co))
        return len(self.verts) - 1

    def face(self, ids, uvs, part):
        self.faces.append((tuple(ids), tuple(uvs), part))


def _planar(part, co, mirror=False):
    """Planar UV of a front/back/boss point: x/z of the shield plane mapped into the part's rect."""
    half = R_EDGE + 0.004 if part in ("front", "back") else BOSS_R + 0.012
    x = -co.x if mirror else co.x
    return _uv_in(UV_RECTS[part], 0.5 + 0.5 * x / half, 0.5 + 0.5 * co.z / half)


def _lathe(m, profile, seg, part, uv_fn, flip=False):
    """profile: list of (r, d). Rings revolve around the shield axis; r == 0 makes a pole."""
    rings = []
    for r, d in profile:
        if r == 0.0:
            rings.append([m.v(P(0.0, d, 0.0))])
        else:
            rings.append([m.v(P(r, d, 2 * math.pi * k / seg)) for k in range(seg)])
    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        for k in range(seg):
            k1 = (k + 1) % seg
            if len(a) == 1:
                ids = [a[0], b[k1], b[k]]
            elif len(b) == 1:
                ids = [a[k], a[k1], b[0]]
            else:
                ids = [a[k], a[k1], b[k1], b[k]]
            if flip:
                ids = ids[::-1]
            uvs = [uv_fn(m.verts[j], i, k, j) for j in ids]
            m.face(ids, uvs, part)
    return rings


def build(name="SM_Weapon_StarterBuckler"):
    m = Mesh()

    # --- planks: front (domed), edge, back (domed, closed)
    fr = [0.058, 0.090, 0.120, 0.148, R_SHIELD - 0.004]
    _lathe(m, [(r, d_front(r)) for r in fr] + [(R_EDGE, BACK_D + THICK)], SEG, "front",
           lambda co, i, k, j: _planar("front", co))
    # edge band (end grain) between the front rim ring and the back rim ring: u around, v across thickness
    def edge_uv(co, i, k, j):
        a = math.atan2(co.z, co.x) % (2 * math.pi)
        return _uv_in(UV_RECTS["edge"], a / (2 * math.pi), 0.0 if abs(-co.y - BACK_D) < 1e-4 else 1.0)
    edge_rings = []
    for d in (BACK_D + THICK, BACK_D):
        edge_rings.append([m.v(P(R_EDGE, d, 2 * math.pi * k / SEG)) for k in range(SEG + 1)])  # +1: UV seam column
    for k in range(SEG):
        ids = [edge_rings[0][k], edge_rings[0][k + 1], edge_rings[1][k + 1], edge_rings[1][k]]
        us = [k / SEG, (k + 1) / SEG, (k + 1) / SEG, k / SEG]
        uvs = [_uv_in(UV_RECTS["edge"], u, 1.0 if n < 2 else 0.0) for n, u in enumerate(us)]
        m.face(ids, uvs, "edge")
    br = [R_EDGE, 0.140, 0.095, 0.045, 0.0]
    _lathe(m, [(r, d_back(min(r, R_SHIELD))) for r in br], SEG, "back",
           lambda co, i, k, j: _planar("back", co, mirror=True), flip=True)

    # --- boss: flange + dome (steel), pole at the top
    base = d_front(BOSS_R)
    boss = [(BOSS_R + 0.012, base - 0.002), (BOSS_R + 0.010, base + 0.004), (BOSS_R, base + 0.006),
            (BOSS_R * 0.93, base + BOSS_H * 0.45), (BOSS_R * 0.72, base + BOSS_H * 0.80), (BOSS_R * 0.38, base + BOSS_H * 0.98),
            (0.0, base + BOSS_H)]
    _lathe(m, boss, SEG_BOSS, "boss", lambda co, i, k, j: _planar("boss", co))

    # --- rim: iron band wrapping the edge, 4-sided closed tube around the circle (seam column duplicated)
    prof = [(R_EDGE + 0.007, BACK_D + THICK + 0.004), (R_EDGE + 0.009, BACK_D + THICK * 0.5),
            (R_EDGE + 0.007, BACK_D - 0.004), (R_EDGE - 0.012, BACK_D + THICK * 0.5 + 0.0)]
    # the 4th point sits inside the planks, so the band reads as a U-channel clamped over the edge
    prof[3] = (R_EDGE - 0.010, d_front(R_EDGE - 0.010) + 0.003)
    cols = []
    for k in range(SEG + 1):
        a = 2 * math.pi * k / SEG
        cols.append([m.v(P(r, d, a)) for r, d in prof])
    np_ = len(prof)
    for k in range(SEG):
        for s in range(np_):
            s1 = (s + 1) % np_
            ids = [cols[k][s], cols[k + 1][s], cols[k + 1][s1], cols[k][s1]]
            vs = [s / np_, s / np_, (s + 1) / np_, (s + 1) / np_]
            us = [k / SEG, (k + 1) / SEG, (k + 1) / SEG, k / SEG]
            m.face(ids, [_uv_in(UV_RECTS["rim"], u, v) for u, v in zip(us, vs)], "rim")

    # --- rivets: 8 low domes on the front ring
    for n in range(RIVETS):
        a = 2 * math.pi * (n + 0.5) / RIVETS
        c = Vector((RIVET_R * math.cos(a), 0.0, RIVET_R * math.sin(a)))
        d0 = d_front(RIVET_R) - 0.001
        ring = []
        for k in range(6):
            b = 2 * math.pi * k / 6
            off = Vector((math.cos(b), 0.0, math.sin(b))) * 0.0075
            ring.append(m.v(Vector((c.x + off.x, -d0, c.z + off.z))))
        mid = []
        for k in range(6):
            b = 2 * math.pi * k / 6
            off = Vector((math.cos(b), 0.0, math.sin(b))) * 0.005
            mid.append(m.v(Vector((c.x + off.x, -(d0 + 0.0035), c.z + off.z))))
        top = m.v(Vector((c.x, -(d0 + 0.005), c.z)))

        def ruv(j, c=c):
            co = m.verts[j]
            return _uv_in(UV_RECTS["rivet"], 0.5 + (co.x - c.x) / 0.016, 0.5 + (co.z - c.z) / 0.016)
        for k in range(6):
            k1 = (k + 1) % 6
            ids = [ring[k], mid[k], mid[k1], ring[k1]]
            m.face(ids, [ruv(j) for j in ids], "rivet")
            ids = [mid[k], top, mid[k1]]
            m.face(ids, [ruv(j) for j in ids], "rivet")

    # --- handle: leather-wrapped bar along +Z (Unity +Y) behind the boss, ends bent into the back of the planks
    path = [Vector((0, -(d_back(0.08) + 0.004), -0.082)), Vector((0, -0.018, -0.068)), Vector((0, 0.0, -0.042)),
            Vector((0, 0.0, 0.0)), Vector((0, 0.0, 0.042)), Vector((0, -0.018, 0.068)), Vector((0, -(d_back(0.08) + 0.004), 0.082))]
    sides = 8
    rh = 0.0135
    length = [0.0]
    for i in range(1, len(path)):
        length.append(length[-1] + (path[i] - path[i - 1]).length)
    rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        nrm = Vector((1, 0, 0))
        bn = t.cross(nrm).normalized()
        rings.append([m.v(p + nrm * (math.cos(2 * math.pi * s / sides) * rh) + bn * (math.sin(2 * math.pi * s / sides) * rh))
                      for s in range(sides + 1)])
    for i in range(len(path) - 1):
        for s in range(sides):
            ids = [rings[i][s], rings[i][s + 1], rings[i + 1][s + 1], rings[i + 1][s]]
            us = [length[i], length[i], length[i + 1], length[i + 1]]
            vs = [s / sides, (s + 1) / sides, (s + 1) / sides, s / sides]
            m.face(ids, [_uv_in(UV_RECTS["handle"], u / length[-1], v) for u, v in zip(us, vs)], "handle")

    obj = _to_object(m, name)
    return obj


def _to_object(m, name):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in m.verts], [], [f[0] for f in m.faces])
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    part_attr = me.attributes.new("Part", "INT", "FACE")
    for poly, (ids, uvs, part) in zip(me.polygons, m.faces):
        for li, uvco in zip(poly.loop_indices, uvs):
            uv.data[li].uv = uvco
        part_attr.data[poly.index].value = PART_ORDER.index(part)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.faces.layers.int["Part"]
    # weld duplicated seam columns WITHIN each part only, so part borders stay hard (separate vertices)
    for idx in range(len(PART_ORDER)):
        vs = list({v for f in bm.faces if f[layer] == idx for v in f.verts})
        bmesh.ops.remove_doubles(bm, verts=vs, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    # smooth, but keep the rim channel, boss flange and plank corners crisp
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(42))
    obj.select_set(False)
    return obj


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def sockets():
    """(name, location in authoring space). Socket_Grip = pivot; Socket_FxBlock = front of the boss (block spark)."""
    return [("Socket_Grip", Vector((0, 0, 0))), ("Socket_FxBlock", Vector((0, -(d_front(BOSS_R) + BOSS_H), 0)))]
