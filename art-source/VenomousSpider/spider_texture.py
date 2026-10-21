"""METINAR-013 — original hand-painted-style texture set for the Venomous Spider.

UVs are generated with Smart UV Project. Per-texel data (object-space position, normal, region and
aux values from spider_model.py) is baked with Cycles, then painted procedurally with numpy:
T_VenomousSpider_BC (sRGB), _MS (R metallic, A smoothness, linear), _E (emission, sRGB) and a height map
that is baked into the tangent-space normal map _N (OpenGL +Y) with Cycles. No external images are used.
"""
import math
import os

import bpy
import numpy as np

import spider_model as sm

SIZE = 1024


# ---------------------------------------------------------------- UVs + baking helpers
def unwrap(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=0.004, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def _bake_setup():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    sc.render.bake.margin = 12
    sc.render.bake.margin_type = "EXTEND"
    sc.render.bake.use_clear = True


def _emit_material(name, build):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs[0], out.inputs[0])
    build(nt, em.inputs["Color"])
    return mat


def _bake_into(obj, mat, image, bake_type="EMIT", **kw):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    mat.node_tree.nodes.active = node
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.bake(type=bake_type, **kw)


def _float_image(name):
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    return img


def _pixels(img):
    a = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(SIZE, SIZE, 4)


def bake_data(obj):
    _bake_setup()

    def pos(nt, sock):
        tc = nt.nodes.new("ShaderNodeTexCoord")
        mad = nt.nodes.new("ShaderNodeVectorMath")
        mad.operation = "MULTIPLY_ADD"
        mad.inputs[1].default_value = (0.5, 0.5, 0.5)
        mad.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(tc.outputs["Object"], mad.inputs[0])
        nt.links.new(mad.outputs[0], sock)

    def nrm(nt, sock):
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        tr = nt.nodes.new("ShaderNodeVectorTransform")
        tr.vector_type = "NORMAL"
        tr.convert_from = "WORLD"
        tr.convert_to = "OBJECT"
        mad = nt.nodes.new("ShaderNodeVectorMath")
        mad.operation = "MULTIPLY_ADD"
        mad.inputs[1].default_value = (0.5, 0.5, 0.5)
        mad.inputs[2].default_value = (0.5, 0.5, 0.5)
        nt.links.new(geo.outputs["Normal"], tr.inputs[0])
        nt.links.new(tr.outputs[0], mad.inputs[0])
        nt.links.new(mad.outputs[0], sock)

    def data(nt, sock):
        at = nt.nodes.new("ShaderNodeAttribute")
        at.attribute_name = "Data"
        nt.links.new(at.outputs["Color"], sock)

    result = {}
    for key, fn in (("pos", pos), ("nrm", nrm), ("data", data)):
        img = _float_image("bake_" + key)
        _bake_into(obj, _emit_material("M_Bake_" + key, fn), img)
        result[key] = _pixels(img)
    return result


# ---------------------------------------------------------------- numpy painting
def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761)
    h = (h ^ (h >> 13)) * 1274126177
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float32) / 65535.0


def value_noise(p, seed=0):
    f = np.floor(p)
    t = p - f
    t = t * t * (3 - 2 * t)
    i = f.astype(np.int64)
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (t[..., 0] if dx else 1 - t[..., 0]) * (t[..., 1] if dy else 1 - t[..., 1]) * (t[..., 2] if dz else 1 - t[..., 2])
                out = out + w * _hash(i[..., 0] + dx, i[..., 1] + dy, i[..., 2] + dz, seed)
    return out


def fbm(p, octaves=4, seed=0):
    total, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        total = total + amp * value_noise(p * (2.0 ** o), seed + o)
        norm += amp
        amp *= 0.5
    return total / norm


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = np.asarray(t)[..., None] if np.ndim(t) else t
    return np.asarray(a) * (1 - t) + np.asarray(b) * t


def paint(baked):
    P = baked["pos"][..., :3] * 2.0 - 1.0
    N = baked["nrm"][..., :3] * 2.0 - 1.0
    N = N / np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-6)
    D = baked["data"]
    reg = np.rint(D[..., 0] * 10.0).astype(np.int32)
    g, b = D[..., 1], D[..., 2]
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    nz = N[..., 2]

    n1 = fbm(P * 9.0, 4, 1)
    n2 = fbm(P * 38.0, 3, 7)
    streak = fbm(P * np.array([24.0, 24.0, 6.0]), 3, 11)
    top = sstep(-0.35, 0.95, nz)

    dark = np.array([0.090, 0.045, 0.110])
    mid = np.array([0.340, 0.160, 0.400])
    light = np.array([0.500, 0.300, 0.560])
    green = np.array([0.520, 0.960, 0.180])
    green_edge = np.array([0.200, 0.560, 0.070])
    outline = np.array([0.030, 0.050, 0.020])

    body_t = np.clip(top * 0.85 + (n1 - 0.5) * 0.35 + (streak - 0.5) * 0.15, 0, 1)
    color = mix(dark, mid, body_t)
    color = mix(color, light, sstep(0.80, 1.0, top) * 0.35 * (0.6 + 0.8 * n2))
    color = color * (1.0 - 0.45 * sstep(-0.25, -0.85, nz))[..., None]
    smooth = np.full(reg.shape, 0.35, np.float32)
    emis = np.zeros(P.shape, np.float32)
    height = 0.55 * n2 + 0.45 * n1

    # Cephalothorax: radial grooves.
    ceph = (reg == sm.REG_BODY)
    ang = np.arctan2(x, y - sm.CEPH_C.y)
    groove = sstep(0.82, 0.97, np.cos(ang * 8.0)) * sstep(0.2, 0.7, nz) * sstep(0.02, 0.09, np.hypot(x, y - sm.CEPH_C.y))
    color = np.where(ceph[..., None], color * (1 - 0.55 * groove)[..., None], color)
    height = np.where(ceph, height - 0.5 * groove, height)

    # Head: darker around the eyes.
    head = (reg == sm.REG_HEAD)
    color = np.where(head[..., None], color * 0.75, color)

    # Abdomen: toxic chevrons + side spots with dark outline.
    abd = (reg == sm.REG_ABDOMEN)
    u = (y - sm.ABD_C.y) / sm.ABD_R.y
    v = x / sm.ABD_R.x
    w = (z - sm.ABD_C.z) / sm.ABD_R.z
    warp = (n1 - 0.5) * 0.07
    dist = np.full(reg.shape, 9.0, np.float32)
    for uk, width in ((-0.42, 1.0), (-0.02, 1.0), (0.36, 0.8)):
        d = np.abs(u - uk - 0.55 * np.abs(v) ** 1.2 + warp) / width
        d = np.where(np.abs(v) < 0.78, d, 9.0)
        dist = np.minimum(dist, d)
    stripe = np.abs(v) * 1.4 + np.maximum(0.0, u + 0.55) * 4.0
    dist = np.minimum(dist, np.where(u < -0.5, stripe, 9.0))
    dorsal = sstep(0.0, 0.32, w)
    for su, sv, sw in ((-0.15, 0.95, 0.05), (0.38, 0.86, 0.05)):
        for sgn in (-1, 1):
            d = np.sqrt((u - su) ** 2 + (v - sgn * sv) ** 2 + (w - sw) ** 2) / 2.2
            dist = np.where(d < dist, d, dist)
            dorsal = np.where(d < 0.2, 1.0, dorsal)
    core = sstep(0.050, 0.020, dist + warp * 0.3) * dorsal
    mark = sstep(0.072, 0.050, dist) * dorsal
    rim = np.clip(sstep(0.100, 0.075, dist) * dorsal - mark, 0, 1)
    mcol = mix(green_edge, green, core)
    a_col = mix(color, outline, rim * 0.85)
    a_col = mix(a_col, mcol, mark)
    color = np.where(abd[..., None], a_col, color)
    smooth = np.where(abd, 0.48 + 0.1 * mark, smooth)
    emis = np.where(abd[..., None], (mcol * (0.25 + 0.45 * core)[..., None]) * mark[..., None], emis)
    height = np.where(abd, height + 0.35 * mark - 0.25 * rim, height)

    # Legs: darker, lighter joint bands with a thin venom ring, black tips.
    leg = (reg == sm.REG_LEG)
    band = sstep(0.35, 0.8, b)
    ring = sstep(0.82, 0.97, b)
    l_col = color * 0.72
    l_col = mix(l_col, light * 0.9, band * 0.65)
    l_col = mix(l_col, green_edge, ring * 0.8)
    l_col = mix(l_col, np.array([0.025, 0.018, 0.028]), sstep(0.86, 0.97, g))
    color = np.where(leg[..., None], l_col, color)
    smooth = np.where(leg, 0.30 + 0.15 * band, smooth)
    emis = np.where(leg[..., None], (green_edge * 0.6 * ring[..., None]), emis)
    height = np.where(leg, height - 0.45 * ring + 0.2 * band, height)

    # Eyes: glossy red with a painted highlight; strong emission.
    eye = (reg == sm.REG_EYE)
    L = np.array([-0.3, -0.5, 0.81])
    hl = sstep(0.93, 0.99, (N * L).sum(-1))
    e_col = mix(np.array([0.70, 0.035, 0.025]), np.array([1.0, 0.75, 0.65]), hl)
    color = np.where(eye[..., None], e_col, color)
    smooth = np.where(eye, 0.9, smooth)
    emis = np.where(eye[..., None], np.array([1.0, 0.10, 0.04]) * (0.85 + 0.15 * hl)[..., None], emis)

    # Fangs: pale bone to dark red-brown tips.
    fang = (reg == sm.REG_FANG)
    f_col = mix(np.array([0.87, 0.81, 0.64]), np.array([0.32, 0.07, 0.05]), sstep(0.55, 1.0, g))
    f_col = f_col * (0.85 + 0.15 * top)[..., None]
    color = np.where(fang[..., None], f_col, color)
    smooth = np.where(fang, 0.62, smooth)

    # Spikes: purple base to venom-green tip.
    spike = (reg == sm.REG_SPIKE)
    s_col = mix(color, np.array([0.55, 0.85, 0.30]), sstep(0.15, 0.5, g))
    color = np.where(spike[..., None], s_col, color)
    emis = np.where(spike[..., None], np.array([0.30, 0.60, 0.10]) * sstep(0.3, 0.5, g)[..., None] * 0.5, emis)
    smooth = np.where(spike, 0.45, smooth)

    empty = reg <= 0
    color = np.where(empty[..., None], dark, color)
    emis = np.where(empty[..., None], 0.0, emis)
    return np.clip(color, 0, 1), np.clip(smooth, 0, 1), np.clip(emis, 0, 1), np.clip(height, 0, 1)


# ---------------------------------------------------------------- output
def _save(name, rgba, path, colorspace):
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def _rgba(rgb, a=None):
    alpha = np.ones(rgb.shape[:2], np.float32) if a is None else a
    return np.concatenate([rgb, alpha[..., None]], axis=-1)


def bake_normal_from_height(obj, height_img, out_path):
    mat = bpy.data.materials.new("M_Bake_Normal")
    mat.use_nodes = True
    nt = mat.node_tree
    p = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = height_img
    tex.interpolation = "Cubic"
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.55
    bump.inputs["Distance"].default_value = 0.004
    nt.links.new(tex.outputs["Color"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    img = bpy.data.images.new("T_VenomousSpider_N", SIZE, SIZE, alpha=False)
    img.colorspace_settings.name = "Non-Color"
    bpy.context.scene.cycles.samples = 4
    _bake_into(obj, mat, img, bake_type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y", normal_b="POS_Z")
    img.filepath_raw = out_path
    img.file_format = "PNG"
    img.save()
    return img


def make_textures(obj, out_dir):
    """Returns dict of written texture paths. obj must already be unwrapped."""
    os.makedirs(out_dir, exist_ok=True)
    baked = bake_data(obj)
    color, smooth, emis, height = paint(baked)
    paths = {k: os.path.join(out_dir, "T_VenomousSpider_%s.png" % k) for k in ("BC", "MS", "E", "N")}
    _save("T_VenomousSpider_BC", _rgba(color), paths["BC"], "sRGB")
    zeros = np.zeros(smooth.shape, np.float32)
    _save("T_VenomousSpider_MS", np.stack([zeros, zeros, zeros, smooth], -1), paths["MS"], "Non-Color")
    _save("T_VenomousSpider_E", _rgba(emis), paths["E"], "sRGB")
    h_img = bpy.data.images.new("height", SIZE, SIZE, alpha=True, float_buffer=True)
    h_img.colorspace_settings.name = "Non-Color"
    h_img.pixels.foreach_set(_rgba(np.repeat(height[..., None], 3, -1)).astype(np.float32).ravel())
    bake_normal_from_height(obj, h_img, paths["N"])
    obj.data.materials.clear()
    return paths


def final_material(paths):
    """Blender preview material equivalent to the Unity URP Lit setup (for renders)."""
    mat = bpy.data.materials.new("M_VenomousSpider")
    mat.use_nodes = True
    nt = mat.node_tree
    p = nt.nodes["Principled BSDF"]

    def tex(key, cs):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(paths[key], check_existing=True)
        n.image.colorspace_settings.name = cs
        return n

    bc, ms, em, nm = tex("BC", "sRGB"), tex("MS", "Non-Color"), tex("E", "sRGB"), tex("N", "Non-Color")
    nt.links.new(bc.outputs["Color"], p.inputs["Base Color"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(ms.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    p.inputs["Metallic"].default_value = 0.0
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], p.inputs["Normal"])
    nt.links.new(em.outputs["Color"], p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 1.5
    return mat
