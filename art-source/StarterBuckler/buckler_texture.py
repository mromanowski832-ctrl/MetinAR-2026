"""METINAR-032 — original procedural 512 x 512 texture set for the Lipowy Puklerz (painted in UV space, no bake).

Linden (lipa) planks: pale wood, vertical planks with dark seams, a faded ochre painted ring, edge wear;
iron rim/boss/rivets: dark hammered metal; grip: leather wrap. Outputs T_StarterBuckler_BC (sRGB),
_MS (R metallic, A smoothness, URP Lit convention) and _N (tangent space, OpenGL +Y, from a painted height map).
"""
import os

import bpy
import numpy as np

import buckler_model as M

SIZE = 512

WOOD = np.array([0.70, 0.56, 0.38])
WOOD_DARK = np.array([0.50, 0.37, 0.22])
PAINT = np.array([0.62, 0.38, 0.12])
IRON = np.array([0.20, 0.205, 0.215])
IRON_HI = np.array([0.38, 0.385, 0.39])
RUST = np.array([0.33, 0.18, 0.09])
LEATHER = np.array([0.24, 0.13, 0.065])
LEATHER_HI = np.array([0.36, 0.21, 0.11])


def _hash(ix, iy, seed):
    h = (ix * 374761393 + iy * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float32) / 65535.0


def noise2(x, y, seed=0):
    xi, yi = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    tx, ty = x - xi, y - yi
    tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)
    a, b = _hash(xi, yi, seed), _hash(xi + 1, yi, seed)
    c, d = _hash(xi, yi + 1, seed), _hash(xi + 1, yi + 1, seed)
    return (a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty


def fbm(x, y, octaves=4, seed=0):
    t, amp, norm = 0.0, 1.0, 0.0
    for o in range(octaves):
        t = t + amp * noise2(x * 2 ** o, y * 2 ** o, seed + o)
        norm += amp
        amp *= 0.5
    return t / norm


def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    t = np.asarray(t)[..., None]
    return np.asarray(a) * (1 - t) + np.asarray(b) * t


def paint():
    v, u = np.meshgrid((np.arange(SIZE) + 0.5) / SIZE, (np.arange(SIZE) + 0.5) / SIZE, indexing="ij")  # row = v (bottom-up)
    color = np.tile(IRON, (SIZE, SIZE, 1)).astype(np.float32)
    metal = np.zeros((SIZE, SIZE), np.float32)
    smooth = np.full((SIZE, SIZE), 0.3, np.float32)
    height = np.full((SIZE, SIZE), 0.5, np.float32)

    def rect(name):
        u0, v0, u1, v1 = M.UV_RECTS[name]
        mask = (u >= u0) & (u < u1) & (v >= v0) & (v < v1)
        return mask, (u - u0) / (u1 - u0), (v - v0) / (v1 - v0)

    def wood(mask, x, z, back):
        nonlocal color, smooth, height, metal
        plank_w = 0.068
        pi = np.floor((x + 0.2) / plank_w)
        frac = ((x + 0.2) / plank_w) - pi
        tint = 0.88 + 0.22 * _hash(pi.astype(np.int64), np.zeros_like(pi, np.int64), 5)
        warp = fbm(x * 18.0, z * 3.0, 3, 11) * 2.0
        grain = 0.5 + 0.5 * np.sin((x * 260.0 + warp * 3.0 + pi * 1.7) * 1.0)
        fine = fbm(x * 220.0, z * 22.0, 3, 21)
        seam = sstep(0.06, 0.0, np.minimum(frac, 1 - frac))
        r = np.sqrt(x * x + z * z)
        c = mix(WOOD_DARK, WOOD, 0.55 + 0.35 * grain * fine)
        c = c * tint[..., None]
        wear = sstep(0.13, 0.172, r) * 0.30 + sstep(0.09, 0.06, r) * 0.18
        if back:
            c = c * 0.82
            wear = wear + 0.15
        else:
            ring = sstep(0.004, 0.0, np.abs(r - 0.112) - 0.007) * (0.75 + 0.25 * fbm(x * 40, z * 40, 3, 7))
            chip = fbm(x * 60, z * 60, 4, 3) > 0.62
            c = mix(c, PAINT * (0.85 + 0.2 * fine)[..., None], ring * ~chip)
            height_paint = 0.03 * ring
        c = mix(c, WOOD_DARK * 0.55, np.clip(wear * (0.5 + fbm(x * 30, z * 30, 3, 9)), 0, 1))
        c = mix(c, WOOD_DARK * 0.35, seam * 0.85)
        color = np.where(mask[..., None], c, color)
        smooth = np.where(mask, 0.22 + 0.1 * fine, smooth)
        metal = np.where(mask, 0.0, metal)
        h = 0.5 + 0.08 * (grain - 0.5) * fine - 0.35 * seam + (0 if back else height_paint)
        height = np.where(mask, h, height)

    half = M.R_EDGE + 0.004
    m, s, t = rect("front")
    wood(m, (s - 0.5) * 2 * half, (t - 0.5) * 2 * half, False)
    m, s, t = rect("back")
    wood(m, -(s - 0.5) * 2 * half, (t - 0.5) * 2 * half, True)

    # end grain on the edge
    m, s, t = rect("edge")
    eg = fbm(s * 400.0, t * 6.0, 3, 31)
    color = np.where(m[..., None], mix(WOOD_DARK * 0.6, WOOD_DARK, eg), color)
    smooth = np.where(m, 0.15, smooth)
    height = np.where(m, 0.5 + 0.1 * (eg - 0.5), height)

    def iron(mask, a, b, scale, seed, bright=0.0):
        nonlocal color, metal, smooth, height
        dent = fbm(a * scale, b * scale, 4, seed)
        scratch = sstep(0.985, 1.0, noise2(a * scale * 0.5, b * scale * 14.0, seed + 3))
        rust = sstep(0.62, 0.82, fbm(a * scale * 0.6, b * scale * 0.6, 3, seed + 7))
        c = mix(IRON, IRON_HI, np.clip(0.25 + 0.5 * dent + bright + scratch, 0, 1))
        c = mix(c, RUST, 0.7 * rust)
        color = np.where(mask[..., None], c, color)
        metal = np.where(mask, 0.85 - 0.6 * rust, metal)
        smooth = np.where(mask, 0.48 + 0.12 * dent - 0.3 * rust + 0.2 * scratch, smooth)
        height = np.where(mask, 0.5 + 0.18 * (dent - 0.5) - 0.1 * scratch, height)

    m, s, t = rect("boss")
    iron(m, s, t, 9.0, 41, 0.05)
    m, s, t = rect("rivet")
    iron(m, s, t, 3.0, 51, 0.2)
    m, s, t = rect("rim")
    iron(m, s * 30.0, t, 1.6, 61)

    # leather grip: diagonal wrap with stitched overlap
    m, s, t = rect("handle")
    wrap = ((s * 14.0 + t) % 1.0)
    ridge = sstep(0.0, 0.12, wrap) * sstep(1.0, 0.85, wrap)
    lg = fbm(s * 60.0, t * 20.0, 3, 71)
    c = mix(LEATHER * 0.7, LEATHER_HI, np.clip(0.3 + 0.5 * lg * ridge, 0, 1))
    color = np.where(m[..., None], c, color)
    smooth = np.where(m, 0.3 + 0.15 * lg, smooth)
    metal = np.where(m, 0.0, metal)
    height = np.where(m, 0.35 + 0.35 * ridge + 0.05 * lg, height)

    return np.clip(color, 0, 1), np.clip(metal, 0, 1), np.clip(smooth, 0, 1), np.clip(height, 0, 1)


def normal_from_height(h, strength=6.0):
    """Tangent-space normal (OpenGL +Y) from a v-up height map: x along +u, y along +v."""
    dhdu = np.gradient(h, axis=1)
    dhdv = np.gradient(h, axis=0)
    n = np.stack([-dhdu * strength * SIZE / 64.0, -dhdv * strength * SIZE / 64.0, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def _save(name, rgba, path, colorspace):
    # numpy rows are v bottom-up, which is also Blender's pixel order (first row = bottom)
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def make_textures(out_dir, asset_id="StarterBuckler"):
    os.makedirs(out_dir, exist_ok=True)
    color, metal, smooth, height = paint()
    one = np.ones((SIZE, SIZE, 1), np.float32)
    paths = {k: os.path.join(out_dir, "T_%s_%s.png" % (asset_id, k)) for k in ("BC", "MS", "N")}
    _save("T_%s_BC" % asset_id, np.concatenate([color, one], -1), paths["BC"], "sRGB")
    z = np.zeros_like(metal)
    _save("T_%s_MS" % asset_id, np.stack([metal, z, z, smooth], -1), paths["MS"], "Non-Color")
    _save("T_%s_N" % asset_id, np.concatenate([normal_from_height(height), one], -1), paths["N"], "Non-Color")
    return paths


def preview_material(name, paths):
    """Blender preview of the URP Lit set-up (BC, MS: R metallic / A smoothness, N)."""
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
    return mat
