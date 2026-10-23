"""METINAR-016 — Wandering Merchant: procedural 1024² palette-atlas textures (BC, N, MS, E) and the preview material.

Every 128 px cell of the 8x8 atlas holds one surface (cells in merchant_model.CELLS). Patterns (wool weave,
felt, leather grain, stripes, trims, stitches) are computed with numpy from fixed seeds: no image input of any kind.
Arrays are in UV orientation (row 0 = v 0), which is also Blender's pixel order.
"""
import os

import bpy
import numpy as np

import merchant_model as mm

SIZE = 1024
CELL = SIZE // mm.GRID


def _noise(shape, seed, scale):
    """Smooth value noise (bilinear upsampled random grid), deterministic."""
    rs = np.random.RandomState(seed)
    gh, gw = max(2, shape[0] // scale + 2), max(2, shape[1] // scale + 2)
    g = rs.rand(gh, gw)
    y = np.linspace(0, gh - 1.001, shape[0])
    x = np.linspace(0, gw - 1.001, shape[1])
    y0, x0 = y.astype(int), x.astype(int)
    fy, fx = (y - y0)[:, None], (x - x0)[None, :]
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    a = g[y0][:, x0]
    b = g[y0][:, x0 + 1]
    c = g[y0 + 1][:, x0]
    d = g[y0 + 1][:, x0 + 1]
    return a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy


def _fbm(shape, seed, scales=(32, 12, 5)):
    out = np.zeros(shape)
    amp, tot = 1.0, 0.0
    for i, s in enumerate(scales):
        out += amp * _noise(shape, seed + 17 * i, s)
        tot += amp
        amp *= 0.5
    return out / tot


def _c(rgb):
    return np.array(rgb, dtype=float)


# cell painters: (u, v) in 0..1 (CELL x CELL), return colour (h,w,3), height (h,w), metallic, smoothness, emission (h,w,3)
def _paint(name, u, v, seed):
    sh = u.shape
    n = _fbm(sh, seed)
    fine = _noise(sh, seed + 5, 2)
    zero3 = np.zeros(sh + (3,))
    met = np.zeros(sh)
    emis = zero3

    def tint(base, var=0.08, nn=None):
        nn = n if nn is None else nn
        return _c(base)[None, None, :] * (1 + var * (nn[..., None] - 0.5) * 2)

    if name == "skin":
        col = tint((0.80, 0.58, 0.45), 0.06)
        col[..., 0] += 0.04 * _noise(sh, seed + 9, 20)  # warm blush variation
        h, sm = 0.15 * fine, 0.35
    elif name == "beard" or name == "brow":
        strands = 0.5 + 0.5 * np.sin(u * 90 + 6 * n)
        base = (0.82, 0.80, 0.76) if name == "beard" else (0.58, 0.56, 0.53)
        col = tint(base, 0.05) * (0.86 + 0.14 * strands[..., None])
        h, sm = 0.6 * strands, 0.25
    elif name == "eye":
        col = tint((0.07, 0.05, 0.04), 0.0)
        h, sm = 0 * u, 0.85
    elif name == "lip":
        col = tint((0.42, 0.20, 0.17), 0.05)
        h, sm = 0 * u, 0.4
    elif name == "hat":
        col = tint((0.12, 0.31, 0.34), 0.12)
        h, sm = 0.35 * fine + 0.3 * n, 0.12
    elif name == "hatband":
        stitch = ((np.abs(v - 0.15) < 0.03) | (np.abs(v - 0.85) < 0.03)) & (np.sin(u * 140) > 0.2)
        col = tint((0.74, 0.53, 0.17), 0.06)
        col[stitch] = _c((0.35, 0.22, 0.08))
        h, sm = 0.3 * n - 0.4 * stitch, 0.3
    elif name == "scarf":
        band = np.floor(u * 10) % 2
        col = np.where(band[..., None] > 0, tint((0.20, 0.42, 0.22), 0.06), tint((0.86, 0.80, 0.64), 0.05))
        knit = 0.5 + 0.5 * np.sin(v * 120) * np.sin(u * 160)
        col = col * (0.9 + 0.1 * knit[..., None])
        h, sm = 0.5 * knit, 0.1
    elif name == "shirt":
        col = tint((0.80, 0.68, 0.40), 0.05)
        h, sm = 0.2 * fine, 0.15
    elif name in ("coat", "sleeve"):
        weave = 0.5 + 0.25 * np.sin(u * 2 * np.pi * 48) + 0.25 * np.sin(v * 2 * np.pi * 48)
        col = tint((0.43, 0.13, 0.18), 0.10) * (0.92 + 0.08 * weave[..., None])
        h = 0.25 * weave + 0.3 * n
        if name == "coat":
            # gold hem trim with a stepped diamond pattern, darker front placket with brass buttons
            hem = v < 0.075
            diamond = (np.abs(((u * 32) % 1) - 0.5) + np.abs((v / 0.075) - 0.5)) < 0.45
            col[hem] = _c((0.76, 0.58, 0.20))
            col[hem & diamond] = _c((0.30, 0.10, 0.12))
            h = np.where(hem, 0.6, h)
            pl = np.abs(u - 0.75) < 0.022
            col[pl & ~hem] = col[pl & ~hem] * 0.65
            btn = (np.abs(u - 0.75) < 0.018) & (np.abs(((v - 0.55) * 14) % 1 - 0.5) < 0.22) & (v > 0.55) & (v < 0.92)
            col[btn] = _c((0.82, 0.64, 0.28))
            met = np.where(btn, 1.0, met)
            h = np.where(btn, 1.0, h)
        else:
            # sleeve trim at the shoulder seam
            seam = v < 0.04
            col[seam] = col[seam] * 0.7
        sm = 0.18
    elif name == "trousers":
        twill = 0.5 + 0.5 * np.sin((u + v) * 2 * np.pi * 40)
        col = tint((0.25, 0.19, 0.14), 0.10) * (0.9 + 0.1 * twill[..., None])
        h, sm = 0.3 * twill, 0.12
    elif name in ("boot", "sole", "belt", "pouch", "strap", "flap"):
        base = {"boot": (0.17, 0.10, 0.07), "sole": (0.08, 0.06, 0.05), "belt": (0.33, 0.19, 0.10),
                "pouch": (0.52, 0.33, 0.18), "strap": (0.38, 0.22, 0.11), "flap": (0.45, 0.30, 0.17)}[name]
        grain = _noise(sh, seed + 3, 3)
        col = tint(base, 0.14, 0.5 * n + 0.5 * grain)
        h = 0.4 * grain + 0.3 * n
        if name in ("belt", "strap", "flap", "pouch"):
            edge = (np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v)) < 0.09) & (np.sin((u + v) * 120) > 0.3)
            edge &= np.minimum(np.minimum(u, 1 - u), np.minimum(v, 1 - v)) > 0.05
            col[edge] = col[edge] * 0.55 + 0.25 * _c((0.85, 0.75, 0.55))
            h = np.where(edge, h - 0.4, h)
        sm = 0.45 if name != "sole" else 0.2
    elif name in ("brass", "copper", "iron"):
        base = {"brass": (0.80, 0.62, 0.28), "copper": (0.74, 0.40, 0.24), "iron": (0.25, 0.25, 0.27)}[name]
        col = tint(base, 0.12)
        if name == "copper":
            soot = np.clip((0.35 - v) * 3, 0, 1) * (0.6 + 0.4 * n)
            col = col * (1 - 0.6 * soot[..., None])
        met = np.ones(sh)
        h = 0.2 * n
        sm = {"brass": 0.62, "copper": 0.55, "iron": 0.40}[name]
    elif name == "canvas":
        weave = 0.5 + 0.25 * np.sin(u * 2 * np.pi * 64) + 0.25 * np.sin(v * 2 * np.pi * 64)
        col = tint((0.62, 0.53, 0.38), 0.12) * (0.9 + 0.1 * weave[..., None])
        patch = (np.abs(u - 0.32) < 0.12) & (np.abs(v - 0.40) < 0.10)
        col[patch] = col[patch] * _c((0.75, 0.85, 0.95))
        ring = patch & ((np.abs(np.abs(u - 0.32) - 0.115) < 0.006) | (np.abs(np.abs(v - 0.40) - 0.095) < 0.006))
        col[ring] = _c((0.25, 0.18, 0.12))
        h, sm = 0.3 * weave + 0.25 * n - 0.3 * ring, 0.1
    elif name == "blanket":
        su = np.floor(u * 8) % 4
        pal = np.array([(0.62, 0.16, 0.12), (0.84, 0.74, 0.52), (0.16, 0.26, 0.46), (0.84, 0.74, 0.52)])
        col = pal[su.astype(int)] * (1 + 0.1 * (n[..., None] - 0.5))
        h, sm = 0.4 * _noise(sh, seed + 2, 2), 0.08
    elif name == "glass":
        glow = 1 - np.clip(np.hypot(u - 0.5, v - 0.5) * 1.6, 0, 1)
        col = tint((0.95, 0.72, 0.32), 0.04) * (0.7 + 0.3 * glow[..., None])
        emis = _c((1.0, 0.62, 0.22))[None, None, :] * (0.45 + 0.55 * glow[..., None])
        h, sm = 0 * u, 0.9
    else:
        raise KeyError(name)
    return np.clip(col, 0, 1), h, met, np.full(sh, sm) if np.isscalar(sm) else sm, emis


def paint_atlas():
    color = np.zeros((SIZE, SIZE, 3))
    color[:] = (0.5, 0.5, 0.5)
    height = np.zeros((SIZE, SIZE))
    metal = np.zeros((SIZE, SIZE))
    smooth = np.full((SIZE, SIZE), 0.2)
    emis = np.zeros((SIZE, SIZE, 3))
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    u = (xx + 0.5) / CELL
    v = (yy + 0.5) / CELL
    # pad: islands use the inset area; the paint covers the full cell so filtering never reaches a neighbour colour
    for i, (name, (cx, cy)) in enumerate(sorted(mm.CELLS.items(), key=lambda kv: kv[1])):
        uu = (u - mm.CELL_INSET) / (1 - 2 * mm.CELL_INSET)
        vv = (v - mm.CELL_INSET) / (1 - 2 * mm.CELL_INSET)
        c, h, m, s, e = _paint(name, np.clip(uu, 0, 1), np.clip(vv, 0, 1), seed=101 + 7 * (cy * mm.GRID + cx))
        sl = (slice(cy * CELL, (cy + 1) * CELL), slice(cx * CELL, (cx + 1) * CELL))
        color[sl], height[sl], metal[sl], smooth[sl], emis[sl] = c, h, m, s, e
    return color, height, metal, smooth, emis


def normal_from_height(height, strength=2.5):
    """OpenGL tangent-space normal map (green = +V) from a height field, per cell (no seams across cells)."""
    nx = np.zeros_like(height)
    ny = np.zeros_like(height)
    for cy in range(mm.GRID):
        for cx in range(mm.GRID):
            sl = (slice(cy * CELL, (cy + 1) * CELL), slice(cx * CELL, (cx + 1) * CELL))
            h = height[sl]
            dv, du = np.gradient(h)
            nx[sl], ny[sl] = -du * strength, -dv * strength
    nz = np.ones_like(height)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / ln, ny / ln, nz / ln], -1) * 0.5 + 0.5


def _save(name, rgba, path, colorspace):
    img = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
    img.colorspace_settings.name = colorspace
    img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return img


def _rgba(rgb, a=None):
    a = np.ones(rgb.shape[:2]) if a is None else a
    return np.concatenate([rgb, a[..., None]], -1)


def make_textures(out_dir, prefix="T_WanderingMerchant"):
    os.makedirs(out_dir, exist_ok=True)
    color, height, metal, smooth, emis = paint_atlas()
    paths = {r: os.path.join(out_dir, "%s_%s.png" % (prefix, r)) for r in ("BC", "N", "MS", "E")}
    _save(prefix + "_BC", _rgba(color), paths["BC"], "sRGB")
    _save(prefix + "_N", _rgba(normal_from_height(height)), paths["N"], "Non-Color")
    zeros = np.zeros_like(metal)
    _save(prefix + "_MS", np.stack([metal, zeros, zeros, smooth], -1), paths["MS"], "Non-Color")
    _save(prefix + "_E", _rgba(emis), paths["E"], "sRGB")
    return paths


def final_material(paths, name="M_WanderingMerchant"):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    p = nt.nodes["Principled BSDF"]

    def tex(key, cs):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(paths[key], check_existing=True)
        n.image.colorspace_settings.name = cs
        return n

    bc, nm, ms, em = tex("BC", "sRGB"), tex("N", "Non-Color"), tex("MS", "Non-Color"), tex("E", "sRGB")
    nt.links.new(bc.outputs["Color"], p.inputs["Base Color"])
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(nm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], p.inputs["Normal"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(ms.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], p.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(ms.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], p.inputs["Roughness"])
    nt.links.new(em.outputs["Color"], p.inputs["Emission Color"])
    p.inputs["Emission Strength"].default_value = 1.5
    return mat
