"""Small procedural mesh builder (rings/tubes/caps) with per-vertex weights and a 'Data' attribute.

Data colour attribute: R = region id / 10, G = aux u (0..1 along a part), B = aux v (0..1 around a part),
A = part seed (0..1, per part colour variation). Painting in fpp_texture.py reads it back after baking, so materials
stay one-per-object.

Every face carries an interior reference point; to_object() orients each face away from it (no global
recalc), so open shells (armour plates, guard wings) get consistent outward normals even when mirrored.
"""
import math

import bmesh
import bpy
from mathutils import Vector


class Builder:
    def __init__(self):
        self.verts, self.faces, self.weights, self.data, self.refs, self.sharp = [], [], [], [], [], []
        self.seed = 0.5

    def vert(self, co, weights, region, u=0.0, v=0.0):
        self.verts.append(Vector(co))
        self.weights.append(dict(weights))
        self.data.append((region / 10.0, u, v, self.seed))
        return len(self.verts) - 1

    def face(self, ids, ref=None):
        self.faces.append(tuple(ids))
        self.refs.append(None if ref is None else Vector(ref))

    def grid(self, rows, refs, closed=False):
        """Quads between consecutive vertex-id rows; refs[i] = interior point of row i (faces use the row pair mean)."""
        for i in range(len(rows) - 1):
            a_row, b_row = rows[i], rows[i + 1]
            ref = (Vector(refs[i]) + Vector(refs[i + 1])) * 0.5
            m = len(a_row)
            for k in range(m if closed else m - 1):
                k2 = (k + 1) % m
                self.face((a_row[k], a_row[k2], b_row[k2], b_row[k]), ref)

    def strip(self, a_ids, b_ids, ref, closed=False):
        m = len(a_ids)
        for k in range(m if closed else m - 1):
            k2 = (k + 1) % m
            self.face((a_ids[k], a_ids[k2], b_ids[k2], b_ids[k]), ref)

    def fan(self, ids, centre_id, ref, closed=True):
        m = len(ids)
        for k in range(m if closed else m - 1):
            self.face((ids[k], ids[(k + 1) % m], centre_id), ref)

    def tube(self, points, radii, sides, region, weight_fn, normal_hint, cap_start=True, cap_end=True,
             cap_len=None, phase=0.0, u_range=(0.0, 1.0)):
        """Sweep a ring along `points`. radii[i] = r or (r_normal, r_binormal). weight_fn(i, n) -> {bone: w}."""
        pts = [Vector(p) for p in points]
        n = len(pts)
        tangents = []
        for i in range(n):
            a, b = pts[max(0, i - 1)], pts[min(n - 1, i + 1)]
            tangents.append((b - a).normalized())
        hint = Vector(normal_hint)
        rings = []
        for i, (p, t) in enumerate(zip(pts, tangents)):
            nrm = (hint - t * hint.dot(t))
            if nrm.length < 1e-6:
                nrm = t.orthogonal()
            nrm.normalize()
            bin_ = t.cross(nrm).normalized()
            hint = nrm  # parallel-transport-ish: keep the previous normal as the next hint
            r = radii[i]
            rn, rb = (r, r) if not isinstance(r, (tuple, list)) else r
            u = u_range[0] + (u_range[1] - u_range[0]) * (i / max(1, n - 1))
            w = weight_fn(i, n)
            ring = []
            for s in range(sides):
                a = 2.0 * math.pi * (s / sides) + phase
                co = p + nrm * (math.cos(a) * rn) + bin_ * (math.sin(a) * rb)
                ring.append(self.vert(co, w, region, u, s / sides))
            rings.append(ring)
        for i in range(n - 1):
            for s in range(sides):
                a, b = rings[i][s], rings[i][(s + 1) % sides]
                c, d = rings[i + 1][(s + 1) % sides], rings[i + 1][s]
                self.face((a, b, c, d), (pts[i] + pts[i + 1]) * 0.5)
        for end, flag in ((0, cap_start), (n - 1, cap_end)):
            if not flag:
                continue
            ring = rings[end]
            t = tangents[end] * (-1.0 if end == 0 else 1.0)
            r = radii[end]
            rn = r if not isinstance(r, (tuple, list)) else max(r)
            length = rn * 0.6 if cap_len is None else cap_len
            centre = pts[end]
            # one inner ring at 70 % radius for a rounded end, then a pole
            inner = []
            w = weight_fn(end, n)
            u_end = u_range[0] if end == 0 else u_range[1]
            for s, vi in enumerate(ring):
                co = centre + (self.verts[vi] - centre) * 0.72 + t * (length * 0.62)
                inner.append(self.vert(co, w, region, u_end, s / sides))
            pole = self.vert(centre + t * length, w, region, u_end, 0.5)
            for s in range(sides):
                a, b = ring[s], ring[(s + 1) % sides]
                c, d = inner[(s + 1) % sides], inner[s]
                self.face((a, b, c, d), centre - t * (length * 0.5))
                self.face((inner[s], inner[(s + 1) % sides], pole), centre - t * (length * 0.5))
        return rings

    def poly(self, ids, ref=None):
        self.face(ids, ref)

    def _oriented_faces(self):
        out = []
        for ids, ref in zip(self.faces, self.refs):
            if ref is None:
                out.append(ids)
                continue
            pts = [self.verts[i] for i in ids]
            c = sum(pts, Vector()) / len(pts)
            nrm = Vector()
            for k in range(len(pts)):  # Newell normal
                a, b = pts[k], pts[(k + 1) % len(pts)]
                nrm += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
            out.append(ids if nrm.dot(c - ref) >= 0.0 else tuple(reversed(ids)))
        return out

    def to_object(self, name, smooth=True, sharp_angle=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], self._oriented_faces())
        me.update()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        attr = me.color_attributes.new("Data", "FLOAT_COLOR", "POINT")
        for i, (r, g, b, a) in enumerate(self.data):
            attr.data[i].color = (r, g, b, a)
        groups = {}
        for i, w in enumerate(self.weights):
            for bone, value in w.items():
                if value <= 0.0:
                    continue
                if bone not in groups:
                    groups[bone] = obj.vertex_groups.new(name=bone)
                groups[bone].add([i], value, "REPLACE")
        bm = bmesh.new()
        bm.from_mesh(me)
        if any(r is None for r in self.refs):
            bmesh.ops.recalc_face_normals(bm, faces=[f for f, r in zip(bm.faces, self.refs) if r is None])
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = smooth
        if sharp_angle is not None:
            me.set_sharp_from_angle(angle=math.radians(sharp_angle))
        me.update()
        return obj


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def blend_weights(a, b, t):
    out = {}
    for k, v in a.items():
        out[k] = out.get(k, 0.0) + v * (1.0 - t)
    for k, v in b.items():
        out[k] = out.get(k, 0.0) + v * t
    return out


def frames_along(points, normal_hint):
    """Parallel-transported (p, t, n, b) frames along a polyline; n starts from normal_hint (or a callable(i, t))."""
    pts = [Vector(p) for p in points]
    n_pts = len(pts)
    out = []
    prev = None
    for i in range(n_pts):
        a, b = pts[max(0, i - 1)], pts[min(n_pts - 1, i + 1)]
        t = (b - a).normalized()
        hint = Vector(normal_hint(i, t)) if callable(normal_hint) else (prev if prev is not None else Vector(normal_hint))
        nrm = hint - t * hint.dot(t)
        if nrm.length < 1e-6:
            nrm = t.orthogonal()
        nrm.normalize()
        out.append((pts[i], t, nrm, t.cross(nrm).normalized()))
        prev = nrm
    return out


def catmull(points, samples):
    """Centripetal-free uniform Catmull-Rom through `points`, `samples` per span (end points included)."""
    pts = [Vector(p) for p in points]
    ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
    out = []
    for i in range(1, len(ext) - 2):
        p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
        for s in range(samples):
            t = s / samples
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1].copy())
    return out
