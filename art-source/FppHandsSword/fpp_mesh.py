"""Small procedural mesh builder (rings/tubes/caps) with per-vertex weights and a 'Data' attribute.

Data colour attribute: R = region id / 10, G = aux u (0..1 along a part), B = aux v (0..1 around a part).
Painting in fpp_texture.py reads it back after baking, so materials stay one-per-object.
"""
import math

import bmesh
import bpy
from mathutils import Vector


class Builder:
    def __init__(self):
        self.verts, self.faces, self.weights, self.data = [], [], [], []

    def vert(self, co, weights, region, u=0.0, v=0.0):
        self.verts.append(Vector(co))
        self.weights.append(dict(weights))
        self.data.append((region / 10.0, u, v))
        return len(self.verts) - 1

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
                self.faces.append((a, b, c, d))
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
                self.faces.append((a, b, c, d) if end == n - 1 else (b, a, d, c))
                self.faces.append((inner[s], inner[(s + 1) % sides], pole) if end == n - 1
                                  else (inner[(s + 1) % sides], inner[s], pole))
        return rings

    def poly(self, ids):
        self.faces.append(tuple(ids))

    def to_object(self, name, smooth=True):
        me = bpy.data.meshes.new(name)
        me.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        me.update()
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        attr = me.color_attributes.new("Data", "FLOAT_COLOR", "POINT")
        for i, (r, g, b) in enumerate(self.data):
            attr.data[i].color = (r, g, b, 1.0)
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
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = smooth
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
