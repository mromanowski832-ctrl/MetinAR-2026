"""Original procedural texture sets for the FPP arms and sword (no external images).

Per-texel data (object-space position, normal and the Data attribute from fpp_mesh) is baked with Cycles,
painted with numpy, and a painted height map is baked to a tangent-space normal map (OpenGL +Y).
Outputs per asset: T_<Id>_BC (sRGB), _MS (R metallic, A smoothness), _N, and for the sword _E (emission).
"""
import math
import os

import bpy
import numpy as np

import fpp_model as M

SIZE = 1024


def unwrap(obj, margin=0.003):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=margin, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(margin=margin, rotate=True)
    bpy.ops.object.mode_set(mode="OBJECT")


def _bake_setup():
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    sc.render.bake.margin = 8
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


def bake_data(obj, pos_scale):
    """pos is baked as object position * pos_scale * 0.5 + 0.5 (pos_scale ~ 1 / half-extent)."""
    _bake_setup()

    def pos(nt, sock):
        tc = nt.nodes.new("ShaderNodeTexCoord")
        mad = nt.nodes.new("ShaderNodeVectorMath")
        mad.operation = "MULTIPLY_ADD"
        mad.inputs[1].default_value = (0.5 * pos_scale,) * 3
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

    out = {}
    for key, fn in (("pos", pos), ("nrm", nrm), ("data", data)):
        img = _float_image("bake_%s_%s" % (obj.name, key))
        _bake_into(obj, _emit_material("M_Bake_%s_%s" % (obj.name, key), fn), img)
        out[key] = _pixels(img)
    out["P"] = (out["pos"][..., :3] * 2.0 - 1.0) / pos_scale
    return out


# ---------------------------------------------------------------- noise helpers
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


def _regions(baked):
    D = baked["data"]
    return np.rint(D[..., 0] * 10.0).astype(np.int32), D[..., 1], D[..., 2]


# ---------------------------------------------------------------- painting
LEATHER = np.array([0.070, 0.047, 0.034])
LEATHER_HI = np.array([0.160, 0.105, 0.070])
STEEL_DARK = np.array([0.105, 0.108, 0.118])
STEEL_EDGE = np.array([0.30, 0.31, 0.33])
GOLD = np.array([0.80, 0.56, 0.20])
GOLD_DARK = np.array([0.36, 0.22, 0.07])
CLOTH = np.array([0.040, 0.046, 0.075])


def paint_arms(baked):
    P = baked["P"]
    reg, u, v = _regions(baked)
    n1 = fbm(P * 60.0, 4, 3)
    n2 = fbm(P * 260.0, 3, 9)
    color = np.zeros(P.shape, np.float32)
    metal = np.zeros(reg.shape, np.float32)
    smooth = np.zeros(reg.shape, np.float32)
    height = 0.5 + 0.15 * (n2 - 0.5)

    lea = reg == M.REG_LEATHER
    grain = 0.55 * n2 + 0.45 * n1
    seam = sstep(0.035, 0.0, np.abs(((v + 0.25) % 1.0) - 0.5) - 0.0)  # one seam line per tube
    seam = sstep(0.02, 0.0, np.abs(((v + 0.5) % 1.0) - 0.5))
    stitch = seam * (np.sin(u * 2 * np.pi * 26.0) > 0.2)
    lc = mix(LEATHER, LEATHER_HI, np.clip(grain - 0.25, 0, 1) * 0.8)
    lc = mix(lc, np.array([0.42, 0.32, 0.18]), stitch * 0.85)
    color = np.where(lea[..., None], lc, color)
    smooth = np.where(lea, 0.30 + 0.12 * grain, smooth)
    height = np.where(lea, 0.5 + 0.12 * (grain - 0.5) - 0.25 * seam + 0.2 * stitch, height)

    met = reg == M.REG_METAL
    # engraved gold inlay: two thin lines along each plate + chevrons on the vambrace
    inlay = sstep(0.012, 0.0, np.abs(((u * 5.0) % 1.0) - 0.5) - 0.40) * (np.abs(((v * 4.0) % 1.0) - 0.5) < 0.30)
    chevron = sstep(0.025, 0.0, np.abs(((u * 6.0 + np.abs(((v * 8.0) % 1.0) - 0.5) * 1.2) % 1.0) - 0.5) - 0.46)
    mc = mix(STEEL_DARK, STEEL_EDGE, sstep(0.55, 0.9, n1) * 0.5)
    mc = mix(mc, GOLD * 0.85, np.clip(chevron, 0, 1) * 0.9)
    color = np.where(met[..., None], mc, color)
    metal = np.where(met, 0.85 - 0.6 * chevron * 0, metal)
    metal = np.where(met & (chevron > 0.5), 1.0, metal)
    smooth = np.where(met, 0.50 + 0.15 * n2, smooth)
    height = np.where(met, 0.62 - 0.25 * chevron, height)

    gold = reg == M.REG_GOLD
    gc = mix(GOLD_DARK, GOLD, sstep(0.25, 0.75, n1))
    color = np.where(gold[..., None], gc, color)
    metal = np.where(gold, 1.0, metal)
    smooth = np.where(gold, 0.62 + 0.1 * n2, smooth)
    height = np.where(gold, 0.6 + 0.1 * (n1 - 0.5), height)

    clo = reg == M.REG_CLOTH
    weave = 0.5 + 0.5 * np.sin(P[..., 0] * 900.0) * np.sin(P[..., 2] * 900.0)
    cc = CLOTH * (0.85 + 0.3 * n1)[..., None] * (0.9 + 0.2 * weave)[..., None]
    color = np.where(clo[..., None], cc, color)
    smooth = np.where(clo, 0.12, smooth)
    height = np.where(clo, 0.5 + 0.1 * weave, height)

    empty = reg <= 0
    color = np.where(empty[..., None], LEATHER, color)
    return np.clip(color, 0, 1), np.clip(metal, 0, 1), np.clip(smooth, 0, 1), None, np.clip(height, 0, 1)


def paint_sword(baked):
    P = baked["P"]
    N = baked["nrm"][..., :3] * 2.0 - 1.0
    reg, u, v = _regions(baked)
    n1 = fbm(P * 40.0, 4, 5)
    brushed = fbm(P * np.array([600.0, 600.0, 18.0]), 3, 13)  # Blender Z = along the blade
    color = np.zeros(P.shape, np.float32)
    metal = np.zeros(reg.shape, np.float32)
    smooth = np.zeros(reg.shape, np.float32)
    emis = np.zeros(P.shape, np.float32)
    height = np.full(reg.shape, 0.5, np.float32)

    blade = (reg == M.REG_BLADE) | (reg == M.REG_FULLER)
    edge = sstep(0.25, 0.02, np.minimum(np.abs(v - 0.0), np.minimum(np.abs(v - 0.5), np.abs(v - 1.0))))
    bc = mix(np.array([0.50, 0.53, 0.58]), np.array([0.78, 0.80, 0.84]), brushed * 0.6 + edge * 0.4)
    color = np.where(blade[..., None], bc, color)
    metal = np.where(blade, 1.0, metal)
    smooth = np.where(blade, 0.70 + 0.12 * edge, smooth)
    height = np.where(blade, 0.5 + 0.05 * (brushed - 0.5), height)

    # fuller: darker channel with original geometric runes (blue Aura emission points)
    ful = reg == M.REG_FULLER
    cell = (u * 18.0) % 1.0
    glyph = ((np.abs(cell - 0.5) < 0.08) | (np.abs(np.abs(cell - 0.5) - 0.28) < 0.05)) & (u > 0.06) & (u < 0.66)
    rune = glyph.astype(np.float32)
    fc = mix(np.array([0.20, 0.22, 0.26]), np.array([0.25, 0.55, 0.95]), rune * 0.8)
    color = np.where(ful[..., None], fc, color)
    smooth = np.where(ful, 0.55, smooth)
    emis = np.where(ful[..., None], np.array([0.12, 0.42, 1.0]) * (0.55 * rune)[..., None], emis)
    height = np.where(ful, 0.42 - 0.08 * rune, height)

    guard = reg == M.REG_GUARD
    gtrim = sstep(0.6, 0.95, np.abs(N[..., 2]))
    gcol = mix(STEEL_DARK, GOLD * 0.9, (np.abs(((u * 10.0) % 1.0) - 0.5) < 0.06) * 1.0)
    color = np.where(guard[..., None], gcol, color)
    metal = np.where(guard, 0.9, metal)
    smooth = np.where(guard, 0.55 + 0.1 * n1, smooth)

    sg = reg == M.REG_SGOLD
    color = np.where(sg[..., None], mix(GOLD_DARK, GOLD, sstep(0.2, 0.8, n1)), color)
    metal = np.where(sg, 1.0, metal)
    smooth = np.where(sg, 0.66, smooth)

    grip = reg == M.REG_GRIP
    wrap = 0.5 + 0.5 * np.sin((u * 16.0 + v) * 2 * np.pi)
    gc = mix(np.array([0.045, 0.030, 0.022]), np.array([0.13, 0.085, 0.055]), wrap * 0.7 + 0.3 * n1)
    color = np.where(grip[..., None], gc, color)
    smooth = np.where(grip, 0.28, smooth)
    height = np.where(grip, 0.35 + 0.35 * wrap, height)

    gem = reg == M.REG_GEM
    gemc = mix(np.array([0.05, 0.18, 0.75]), np.array([0.55, 0.85, 1.0]), sstep(0.6, 1.0, n1))
    color = np.where(gem[..., None], gemc, color)
    smooth = np.where(gem, 0.92, smooth)
    emis = np.where(gem[..., None], np.array([0.15, 0.45, 1.0]) * 0.8, emis)

    empty = reg <= 0
    color = np.where(empty[..., None], STEEL_DARK, color)
    return np.clip(color, 0, 1), np.clip(metal, 0, 1), np.clip(smooth, 0, 1), np.clip(emis, 0, 1), np.clip(height, 0, 1)


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


def bake_normal_from_height(obj, height_img, out_path, name, strength, distance):
    mat = bpy.data.materials.new("M_Bake_Normal_" + name)
    mat.use_nodes = True
    nt = mat.node_tree
    p = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = height_img
    tex.interpolation = "Cubic"
    bump = nt.nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = strength
    bump.inputs["Distance"].default_value = distance
    nt.links.new(tex.outputs["Color"], bump.inputs["Height"])
    nt.links.new(bump.outputs["Normal"], p.inputs["Normal"])
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=False)
    img.colorspace_settings.name = "Non-Color"
    bpy.context.scene.cycles.samples = 4
    _bake_into(obj, mat, img, bake_type="NORMAL", normal_space="TANGENT", normal_r="POS_X", normal_g="POS_Y", normal_b="POS_Z")
    img.filepath_raw = out_path
    img.file_format = "PNG"
    img.save()


def make_textures(obj, asset_id, out_dir, painter, pos_scale, bump=(0.4, 0.002)):
    os.makedirs(out_dir, exist_ok=True)
    baked = bake_data(obj, pos_scale)
    color, metal, smooth, emis, height = painter(baked)
    paths = {"BC": os.path.join(out_dir, "T_%s_BC.png" % asset_id), "MS": os.path.join(out_dir, "T_%s_MS.png" % asset_id),
             "N": os.path.join(out_dir, "T_%s_N.png" % asset_id)}
    _save("T_%s_BC" % asset_id, _rgba(color), paths["BC"], "sRGB")
    zeros = np.zeros(smooth.shape, np.float32)
    _save("T_%s_MS" % asset_id, np.stack([metal, zeros, zeros, smooth], -1), paths["MS"], "Non-Color")
    if emis is not None:
        paths["E"] = os.path.join(out_dir, "T_%s_E.png" % asset_id)
        _save("T_%s_E" % asset_id, _rgba(emis), paths["E"], "sRGB")
    h_img = bpy.data.images.new("height_" + asset_id, SIZE, SIZE, alpha=True, float_buffer=True)
    h_img.colorspace_settings.name = "Non-Color"
    h_img.pixels.foreach_set(_rgba(np.repeat(height[..., None], 3, -1)).astype(np.float32).ravel())
    bake_normal_from_height(obj, h_img, paths["N"], "T_%s_N" % asset_id, *bump)
    obj.data.materials.clear()
    return paths


def final_material(name, paths, emission_strength=1.0):
    """Blender preview of the Unity URP Lit set-up (BC, MS: R metallic / A smoothness, N, E)."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    p = nt.nodes["Principled BSDF"]

    def tex(key, cs):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(paths[key], check_existing=True)
        n.image.colorspace_settings.name = cs
        return n

    bc, ms, nm = tex("BC", "sRGB"), tex("MS", "Non-Color"), tex("N", "Non-Color")
    nt.links.new(bc.outputs["Color"], p.inputs["Base Color"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(ms.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], p.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(ms.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], p.inputs["Normal"])
    if "E" in paths:
        em = tex("E", "sRGB")
        nt.links.new(em.outputs["Color"], p.inputs["Emission Color"])
        p.inputs["Emission Strength"].default_value = emission_strength
    return mat
