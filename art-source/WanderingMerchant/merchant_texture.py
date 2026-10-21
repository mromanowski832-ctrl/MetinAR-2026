"""METINAR-016 — original procedural 1K texture set for the Wandering Merchant (no external images).

Regions come from the Data attribute written by merchant_model.py; object-space positions place the eyes,
coat buttons, hem trim and the lantern glass. Outputs T_WanderingMerchant_BC/_MS/_N/_E via bake_util.
"""
import numpy as np

import bake_util as bu
import merchant_model as M
from bake_util import fbm, mix, sstep

SIZE = bu.SIZE
POS_SCALE = 0.5  # object positions span about +/-2 m

SKIN = np.array([0.58, 0.40, 0.30])
SKIN_DARK = np.array([0.45, 0.28, 0.20])
COAT = np.array([0.075, 0.16, 0.12])
COAT_HI = np.array([0.12, 0.24, 0.17])
TRIM = np.array([0.62, 0.42, 0.14])
LEATHER = np.array([0.20, 0.11, 0.055])
LEATHER_HI = np.array([0.33, 0.20, 0.10])
TROUSER = np.array([0.20, 0.17, 0.13])
HAT = np.array([0.16, 0.10, 0.065])
WOOD = np.array([0.33, 0.20, 0.10])
WOOD_DARK = np.array([0.20, 0.11, 0.05])
BRASS = np.array([0.74, 0.54, 0.24])
BRASS_DARK = np.array([0.35, 0.22, 0.09])
HAIR = np.array([0.56, 0.54, 0.50])
CANVAS = np.array([0.52, 0.43, 0.29])
STRIPE = np.array([0.45, 0.10, 0.07])
GLOW = np.array([1.0, 0.66, 0.28])


def paint(baked):
    P = baked["P"]
    reg, u, v = bu._regions(baked)
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    n1 = fbm(P * 18.0, 4, 3)
    n2 = fbm(P * 90.0, 3, 9)
    color = np.zeros(P.shape, np.float32)
    metal = np.zeros(reg.shape, np.float32)
    smooth = np.zeros(reg.shape, np.float32)
    height = 0.5 + 0.08 * (n2 - 0.5)
    emis = np.zeros(P.shape, np.float32)

    def put(mask, c, m, s, h):
        nonlocal color, metal, smooth, height
        color = np.where(mask[..., None], c, color)
        metal = np.where(mask, m, metal)
        smooth = np.where(mask, s, smooth)
        height = np.where(mask, h, height)

    # skin: warm tone, rosy cheeks/nose, painted eyes and brows
    sk = reg == M.REG_SKIN
    sc = mix(SKIN_DARK, SKIN, (0.6 + 0.4 * n1))
    cheek = np.zeros(reg.shape, np.float32)
    for e in M.EYES:
        d = np.sqrt((x - e.x) ** 2 + (z - (e.z - 0.03)) ** 2)
        cheek = np.maximum(cheek, sstep(0.035, 0.0, d) * (y > 0.06))
    sc = mix(sc, np.array([0.66, 0.34, 0.27]), (0.35 * cheek))
    eye = np.zeros(reg.shape, np.float32)
    brow = np.zeros(reg.shape, np.float32)
    for e in M.EYES:
        d = np.sqrt((x - e.x) ** 2 + ((z - e.z) * 1.4) ** 2)
        eye = np.maximum(eye, sstep(0.011, 0.007, d) * (y > 0.06))
        db = np.sqrt((x - e.x) ** 2 + ((z - e.z - 0.022) * 3.0) ** 2)
        brow = np.maximum(brow, sstep(0.02, 0.014, db) * (y > 0.06))
    sc = mix(sc, np.array([0.04, 0.03, 0.03]), eye)
    sc = mix(sc, HAIR * 0.75, brow)
    put(sk, sc, 0.0, 0.32 + 0.08 * n2, 0.5 + 0.05 * (n2 - 0.5) - 0.1 * eye + 0.08 * brow)

    # coat: wool noise, front opening seam, brass buttons, ochre trim at hem and cuffs
    co = reg == M.REG_COAT
    wool = 0.6 * n2 + 0.4 * n1
    cc = mix(COAT, COAT_HI, np.clip(wool - 0.2, 0, 1))
    front = y > 0.05
    seam = sstep(0.006, 0.0, np.abs(x - 0.012)) * front * (z < 1.44)
    hem = sstep(0.0, 0.01, 0.53 - z) * (z < 0.53)
    hemline = (z > 0.495) & (z < 0.53)
    cuff = np.zeros(reg.shape, bool)
    for side in ("Left", "Right"):
        wr = M.bone_head(side + "Hand")
        cuff |= (np.sqrt((x - wr.x) ** 2 + (z - wr.z) ** 2) < 0.06) & (np.abs(x) > 0.5)
    trim = hemline | cuff
    btn = np.zeros(reg.shape, np.float32)
    for bz in (1.40, 1.31, 1.22, 1.13):
        d = np.sqrt((x - 0.04) ** 2 + (z - bz) ** 2)
        btn = np.maximum(btn, sstep(0.012, 0.008, d) * front)
    cc = mix(cc, cc * 0.55, (seam + 0.3 * hem))
    cc = np.where(trim[..., None], mix(TRIM * 0.8, TRIM, n1), cc)
    cc = mix(cc, BRASS, btn)
    put(co, cc, 0.0, 0.14 + 0.06 * wool, 0.5 + 0.12 * (wool - 0.5) - 0.3 * seam + 0.25 * btn + 0.05 * trim)
    metal = np.where(co & (btn > 0.5), 0.9, metal)
    smooth = np.where(co & (btn > 0.5), 0.55, smooth)

    # leather: grain + one stitched seam per part
    le = reg == M.REG_LEATHER
    grain = 0.55 * n2 + 0.45 * n1
    seam_l = sstep(0.025, 0.0, np.abs(((v + 0.5) % 1.0) - 0.5))
    stitch = seam_l * (np.sin(u * 2 * np.pi * 30.0) > 0.3)
    lc = mix(LEATHER, LEATHER_HI, np.clip(grain - 0.2, 0, 1))
    lc = mix(lc, np.array([0.50, 0.40, 0.25]), (0.8 * stitch))
    put(le, lc, 0.0, 0.30 + 0.12 * grain, 0.5 + 0.10 * (grain - 0.5) - 0.2 * seam_l + 0.2 * stitch)

    tr = reg == M.REG_TROUSER
    weave = 0.5 + 0.5 * np.sin(x * 700.0 + z * 300.0) * np.sin(z * 700.0 - x * 300.0)
    put(tr, TROUSER * (0.85 + 0.3 * n1)[..., None] * (0.92 + 0.16 * weave)[..., None], 0.0, 0.1, 0.5 + 0.08 * weave)

    ha = reg == M.REG_HAT
    put(ha, HAT * (0.8 + 0.4 * n1)[..., None], 0.0, 0.12 + 0.05 * n2, 0.5 + 0.06 * (n1 - 0.5))

    wo = reg == M.REG_WOOD
    ring = 0.5 + 0.5 * np.sin(u * 2 * np.pi * 14.0 + n1 * 6.0)
    put(wo, mix(WOOD_DARK, WOOD, ring), 0.0, 0.28, 0.5 + 0.12 * (ring - 0.5))

    br = reg == M.REG_BRASS
    tarn = sstep(0.45, 0.8, n1)
    put(br, mix(BRASS, BRASS_DARK, (0.6 * tarn)), 1.0, 0.58 - 0.25 * tarn, 0.55 + 0.05 * n2)
    L = M.LANTERN
    glass = br & (np.sqrt((x - L.x) ** 2 + (y - L.y) ** 2) < 0.047) & (np.abs(z - L.z) < 0.042) & (np.abs(((v * 6.0) % 1.0) - 0.5) < 0.38)
    put(glass, GLOW * 0.9, 0.0, 0.8, 0.45)
    emis = np.where(glass[..., None], GLOW, emis)

    hr = reg == M.REG_HAIR
    streak = 0.5 + 0.5 * np.sin(v * 2 * np.pi * 22.0 + n1 * 5.0)
    put(hr, mix(HAIR * 0.7, HAIR, streak), 0.0, 0.25, 0.5 + 0.15 * (streak - 0.5))

    ca = reg == M.REG_CANVAS
    cv = CANVAS * (0.82 + 0.3 * n1)[..., None]
    roll = ca & (z > 1.52)
    stripe = (np.abs(((x * 9.0) % 1.0) - 0.5) < 0.08) & roll
    cv = np.where(stripe[..., None], STRIPE, cv)
    put(ca, cv, 0.0, 0.12, 0.5 + 0.1 * (n2 - 0.5))

    empty = reg <= 0
    color = np.where(empty[..., None], COAT, color)
    return np.clip(color, 0, 1), np.clip(metal, 0, 1), np.clip(smooth, 0, 1), np.clip(emis, 0, 1), np.clip(height, 0, 1)


def make_textures(obj, out_dir):
    return bu.make_textures(obj, M_ID, out_dir, paint, POS_SCALE, bump=(0.35, 0.004))


M_ID = "WanderingMerchant"
