"""METINAR-032: draws the item icons (budget ITEMS_COMBAT_SPEC §8: 256² PNG).

Batch 1 (ITEMS): the 9 starter-kit items of §7. Batch 2 (ITEMS_BATCH2): every other item id in docs/design/tables
(items_starter.csv, shop_starter_merchant.csv, skill_tier_items.csv / §4.1), one treatise per class id.

Usage:  python make_icons.py <out_dir> [--masters] [--only=id1,id2]
Writes  <out_dir>/T_ItemIcons_<PascalId>_BC.png (256×256 RGBA, transparent background),
        <out_dir>/contact_sheet.png (batch 1) and <out_dir>/contact_sheet_batch2.png (batch 2).
        With --masters also the 1024² masters.
Deterministic: same script + same numpy/Pillow/scipy versions -> identical pixels.
Original work for MetinAR (licence MetinAR-Owned). No external images, fonts, brushes or references are loaded.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from icon_lib import (S, XX, YY, Icon, bezier, catmull, col, grain, lerp_col, m_ellipse, m_and, m_or, m_poly,
                      m_rect, m_stroke, m_sub, noise, shade, xform, downsample)
from scipy import ndimage

ITEMS = ["starter_jerkin", "starter_boots", "starter_buckler", "amber_earring", "starter_gloves",
         "starter_sword", "potion_hp_s", "antidote", "ore_shard"]

GOLD = (0.86, 0.62, 0.22)
IRON = (0.30, 0.31, 0.35)


def pascal(item_id):
    return "".join(p.capitalize() for p in item_id.split("_"))


def tex(base, seed, scale=60, amount=0.22):
    n = noise(seed, scale, 4)
    return col(base) * (1 - amount / 2 + amount * n)[..., None]


def dashes(points, dash=26, gap=22, width=7):
    """Dashed polyline mask (stitches)."""
    m = np.zeros((S, S), np.float32)
    pts = [np.array(p, float) for p in points]
    seg = []
    for a, b in zip(pts[:-1], pts[1:]):
        L = np.linalg.norm(b - a)
        k = 0.0
        while k < L:
            e = min(k + dash, L)
            seg.append((a + (b - a) * k / L, a + (b - a) * e / L))
            k = e + gap
    for a, b in seg:
        m = np.maximum(m, m_stroke([tuple(a), tuple(b)], width))
    return m


# ------------------------------------------------------------------ 1. starter_sword — Miecz Rekruta
def starter_sword():
    ic = Icon()
    T = lambda pts: xform(pts, angle_deg=-45, scale=1.1, offset=(518, 506), pivot=(0, 0))
    blade = m_poly(T([(-205, -58), (395, -50), (530, 0), (395, 50), (-205, 58)]))
    guard = m_stroke(T(bezier((-160, -178), (-238, -95), (-238, 95), (-160, 178))), 50)
    grip = m_poly(T([(-470, -34), (-228, -36), (-228, 36), (-470, 34)]))
    pc = T([(-508, 0)])[0]
    pommel = m_ellipse(pc[0], pc[1], 56)
    collar = m_poly(T([(-262, -40), (-212, -46), (-212, 46), (-262, 40)]))
    ic.put(shade(grip, tex((0.21, 0.155, 0.12), 11, 40, 0.3), bevel=30, spec=0.08))
    for k in range(7):
        x = -455 + k * 34
        wrap = m_and(m_stroke(T([(x, -36), (x + 22, 36)]), 9), grip)
        ic.mul(wrap, 0.45)
    ic.put(shade(blade, tex((0.70, 0.74, 0.80), 12, 90, 0.12), bevel=50, profile="flat", spec=0.6, shininess=36,
                 ambient=0.38, diffuse=0.85))
    fuller = m_poly(T([(-190, -8), (360, -6), (385, 0), (360, 6), (-190, 8)]))
    ic.mul(fuller, 0.62)
    runes = np.zeros((S, S), np.float32)
    for k, x in enumerate(range(-150, 330, 62)):
        if k % 2 == 0:
            shp = [(x - 14, 0), (x, -16), (x + 14, 0), (x, 16)]
            runes = np.maximum(runes, m_sub(m_poly(T(shp)), m_poly(T([(x - 5, 0), (x, -6), (x + 5, 0), (x, 6)]))))
        else:
            runes = np.maximum(runes, m_stroke(T([(x - 12, -16), (x + 8, 0), (x - 12, 16)]), 8, caps=False))
    ic.paint(runes, (0.55, 0.88, 1.0))
    ic.add_glow(runes, (0.15, 0.55, 1.0), radius=9, strength=1.6)
    ic.put(shade(collar, IRON, bevel=14, spec=0.4))
    ic.put(shade(guard, (0.22, 0.22, 0.26), bevel=22, spec=0.5, shininess=30))
    for end in ((-160, -178), (-160, 178)):
        e = T([end])[0]
        ic.put(shade(m_ellipse(e[0], e[1], 30), GOLD, bevel=30, spec=0.7, shininess=40))
    ic.put(shade(pommel, GOLD, bevel=50, spec=0.75, shininess=40))
    return ic


# ------------------------------------------------------------------ 2. starter_jerkin — Kaftan Wędrowca
def starter_jerkin():
    ic = Icon()
    right = [(570, 150), (690, 178), (780, 232), (792, 300), (742, 330), (708, 400), (700, 520), (712, 640),
             (745, 780), (770, 880), (640, 905), (512, 880)]
    left = [(1024 - x, y) for x, y in reversed(right[:-1])]
    body = m_poly(catmull(right + left, closed=True, steps=8))
    leather = tex((0.53, 0.34, 0.20), 21, 50, 0.28)
    ic.put(shade(body, leather, bevel=60, spec=0.12, vgrad=0.22))
    vneck = m_poly([(436, 140), (588, 140), (512, 340)])
    shirt = m_and(vneck, m_rect(0, 120, S, 360))
    ic.put(shade(shirt, tex((0.30, 0.42, 0.27), 22, 30, 0.25), bevel=20, spec=0.05, ambient=0.3))
    for side in (-1, 1):
        edge = m_stroke([(512 + side * 80, 146), (512 + side * 6, 338)], 30)
        ic.put(shade(m_and(edge, body), (0.66, 0.45, 0.27), bevel=14, spec=0.15))
    # lacing
    lace = np.zeros((S, S), np.float32)
    ys = list(range(350, 540, 38))
    for i, y in enumerate(ys[:-1]):
        lace = np.maximum(lace, m_stroke([(488, y), (536, ys[i + 1])], 9))
        lace = np.maximum(lace, m_stroke([(536, y), (488, ys[i + 1])], 9))
    ic.mul(m_stroke([(512, 340), (512, 560)], 10), 0.55)
    ic.put(shade(lace, (0.86, 0.76, 0.55), bevel=5, spec=0.2, edge=1.5))
    for y in ys:
        for x in (488, 536):
            ic.put(shade(m_ellipse(x, y, 9), GOLD, bevel=9, spec=0.6))
    # shoulder pads
    for side in (-1, 1):
        pad = m_and(m_ellipse(512 + side * 225, 245, 105, 74), body)
        ic.put(shade(pad, tex((0.38, 0.23, 0.13), 23, 40, 0.25), bevel=30, spec=0.15))
        ic.paint(m_and(dashes([(512 + side * 150, 222), (512 + side * 300, 250)], 18, 16, 6), pad), (0.80, 0.68, 0.48), 0.8)
        for k in range(2):
            ic.put(shade(m_ellipse(512 + side * (190 + 70 * k), 270 - 6 * k, 12), GOLD, bevel=12, spec=0.6))
    # belt + buckle
    belt = m_and(m_rect(0, 590, S, 652), body)
    ic.put(shade(belt, tex((0.24, 0.15, 0.09), 24, 30, 0.2), bevel=14, spec=0.15))
    ring = m_sub(m_rect(470, 572, 554, 670, 10), m_rect(488, 590, 536, 652, 4))
    ic.put(shade(ring, GOLD, bevel=12, spec=0.7, shininess=36))
    ic.put(shade(m_stroke([(500, 621), (548, 621)], 9), (0.62, 0.62, 0.66), bevel=4, spec=0.5, edge=1))
    # quilting stitches on the skirt
    for x in (395, 455, 569, 629):
        ic.paint(m_and(dashes([(x, 680), (x + (x - 512) * 0.12, 860)], 22, 18, 6), body), (0.82, 0.70, 0.50), 0.75)
    hem = m_and(body, m_sub(np.ones((S, S), np.float32), ndimage.binary_erosion(body > 0.5, iterations=0) * 0 + m_rect(0, 0, S, 840)))
    ic.mul(hem, 0.78)
    return ic


# ------------------------------------------------------------------ 3. starter_boots — Buty Traktowe
BOOT = [(360, 190), (560, 190), (566, 420), (582, 640), (612, 750), (628, 852), (232, 852), (178, 812),
        (200, 748), (318, 690), (352, 480)]


def _boot(ic, dx, dy, scale, dark, seed):
    pts = [(512 + (x - 512) * scale + dx, 520 + (y - 520) * scale + dy) for x, y in BOOT]
    boot = m_poly(catmull(pts, steps=10))
    ys = np.where(boot > 0.5)[0]
    y0, y1 = ys.min(), ys.max()
    base = tex(tuple(c * dark for c in (0.47, 0.30, 0.17)), seed, 45, 0.28)
    ic.put(shade(boot, base, bevel=50, spec=0.14))
    cuff = m_and(boot, m_rect(0, 0, S, y0 + 95 * scale))
    ic.put(shade(cuff, tex(tuple(c * dark for c in (0.62, 0.42, 0.25)), seed + 1, 40, 0.25), bevel=22, spec=0.12))
    sole = m_and(boot, m_rect(0, y1 - 40 * scale, S, S))
    ic.put(shade(sole, tuple(c * dark for c in (0.16, 0.11, 0.09)), bevel=10, spec=0.05))
    heel = m_and(boot, m_rect(int(pts[4][0] - 120 * scale), int(y1 - 85 * scale), S, int(y1 - 40 * scale)))
    ic.put(shade(heel, tuple(c * dark for c in (0.24, 0.16, 0.11)), bevel=10, spec=0.05))
    # laces along the front of the shaft
    lx = lambda y: 512 + (395 - 512) * scale + dx + (y - (520 + (300 - 520) * scale + dy)) * 0.06
    for k in range(5):
        y = 520 + (310 + 70 * k - 520) * scale + dy
        x = lx(y)
        ic.put(shade(m_and(m_stroke([(x - 28 * scale, y - 18 * scale), (x + 34 * scale, y + 22 * scale)], 9 * scale), boot),
                     (0.85, 0.74, 0.52), bevel=4, spec=0.2, edge=1.2))
        ic.put(shade(m_and(m_stroke([(x - 28 * scale, y + 22 * scale), (x + 34 * scale, y - 18 * scale)], 9 * scale), boot),
                     (0.80, 0.69, 0.48), bevel=4, spec=0.2, edge=1.2))
    ic.paint(m_and(dashes(catmull([(p[0], p[1] - 12) for p in pts[6:9]], closed=False, steps=6), 16, 14, 5), boot),
             (0.80, 0.66, 0.46), 0.7)
    return ic


def starter_boots():
    ic = Icon()
    _boot(ic, 150, -78, 0.90, 0.72, 31)
    _boot(ic, -48, 30, 1.0, 1.0, 33)
    return ic


# ------------------------------------------------------------------ 4. starter_buckler — Lipowy Puklerz
def starter_buckler():
    ic = Icon()
    cx, cy, R = 512, 512, 402
    disk = m_ellipse(cx, cy, R)
    g = grain(41, 0, 34, warp=10)
    plank_id = np.clip(((XX - (cx - R)) // 200), 0, 3)
    tone = np.choose(plank_id.astype(int), [0.95, 1.05, 0.9, 1.0])
    wood = lerp_col((0.64, 0.47, 0.29), (0.84, 0.69, 0.47), g * 0.8 + 0.2 * noise(42, 30, 3)) * tone[..., None]
    ic.put(shade(disk, wood, bevel=70, spec=0.12, vgrad=0.15))
    for x in (cx - R + 200, cx - R + 400, cx - R + 600):
        ic.mul(m_and(m_stroke([(x, 0), (x, S)], 9, caps=False), disk), 0.42)
    rim = m_sub(disk, m_ellipse(cx, cy, R - 40))
    ic.put(shade(rim, IRON, bevel=20, spec=0.55, shininess=30))
    for k in range(12):
        a = 2 * math.pi * k / 12 + 0.26
        ic.put(shade(m_ellipse(cx + (R - 20) * math.cos(a), cy + (R - 20) * math.sin(a), 13), (0.55, 0.56, 0.6), bevel=13, spec=0.8))
    flange = m_ellipse(cx, cy, 158)
    ic.put(shade(flange, (0.24, 0.24, 0.27), bevel=22, spec=0.45))
    for k in range(8):
        a = 2 * math.pi * k / 8 + 0.39
        ic.put(shade(m_ellipse(cx + 134 * math.cos(a), cy + 134 * math.sin(a), 12), (0.60, 0.60, 0.64), bevel=12, spec=0.8))
    boss = m_ellipse(cx, cy, 108)
    ic.put(shade(boss, (0.56, 0.58, 0.63), bevel=108, profile="round", spec=0.85, shininess=46, ambient=0.35))
    return ic


# ------------------------------------------------------------------ 5. amber_earring — Bursztynowy Kolczyk
def amber_earring():
    ic = Icon()
    dy = 40
    hook = m_stroke(bezier((512, 300 + dy), (496, 110 + dy), (660, 96 + dy), (664, 262 + dy)), 24)
    ic.put(shade(hook, GOLD, bevel=12, spec=0.75, shininess=40))
    ring = m_sub(m_ellipse(512, 334 + dy, 46), m_ellipse(512, 334 + dy, 28))
    ic.put(shade(ring, GOLD, bevel=9, spec=0.8, shininess=40))
    gem_pts = [(512, 392), (618, 468), (684, 628), (656, 782), (512, 870), (368, 782), (340, 628), (406, 468)]
    gem = m_poly(catmull([(x, y + dy) for x, y in gem_pts], steps=10))
    r = np.sqrt((XX - 548) ** 2 + (YY - (690 + dy)) ** 2) / 320
    amber = lerp_col((1.0, 0.72, 0.22), (0.60, 0.22, 0.02), np.clip(r, 0, 1) ** 1.2)
    amber = amber * (0.95 + 0.10 * noise(51, 70, 3))[..., None]
    ic.put(shade(gem, amber, bevel=180, profile="round", spec=1.0, shininess=80, ambient=0.78, diffuse=0.38, edge=5, edge_dark=0.55))
    soft = ndimage.gaussian_filter(m_ellipse(570, 720 + dy, 90, 110), 40) * gem
    ic.paint(soft, (1.0, 0.80, 0.40), 0.5)
    ic.paint(m_ellipse(468, 690 + dy, 6), (0.40, 0.17, 0.03), 0.55)
    ic.paint(m_stroke(bezier((418, 560 + dy), (424, 482 + dy), (474, 452 + dy)), 22), (1.0, 0.99, 0.92), 0.85)
    ic.paint(m_ellipse(440, 622 + dy, 13), (1.0, 0.99, 0.92), 0.75)
    cap = m_ellipse(512, 402 + dy, 74, 40)
    for side in (-1, 0, 1):
        ic.put(shade(m_stroke([(512 + side * 50, 410 + dy), (512 + side * 74, 480 + dy)] if side else
                              [(512, 420 + dy), (512, 488 + dy)], 18), GOLD, bevel=9, spec=0.8))
    ic.put(shade(cap, GOLD, bevel=40, spec=0.8, shininess=40))
    return ic


# ------------------------------------------------------------------ 6. starter_gloves — Rękawice Rekruta
def starter_gloves():
    ic = Icon()
    leather = (0.19, 0.19, 0.21)
    thumb = m_stroke([(372, 600), (262, 452)], 92)
    ic.put(shade(thumb, tex(leather, 61, 40, 0.18), bevel=40, spec=0.28, shininess=20))
    palm = m_rect(342, 352, 682, 700, 100)
    ic.put(shade(palm, tex(leather, 62, 40, 0.18), bevel=70, spec=0.25, shininess=20))
    fingers = [((632, 430), (652, 222), 72), ((556, 400), (566, 150), 82), ((472, 396), (466, 128), 84),
               ((392, 412), (372, 180), 80)]
    for i, (a, b, w) in enumerate(fingers):
        f = m_stroke([a, b], w)
        ic.put(shade(f, tex(leather, 63 + i, 40, 0.18), bevel=36, spec=0.3, shininess=22))
        for t in (0.42, 0.72):
            kx, ky = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            ic.mul(m_and(m_stroke([(kx - w * 0.3, ky + 4), (kx + w * 0.3, ky - 4)], 5), f), 0.6)
    ic.paint(m_and(dashes(catmull([(380, 470), (512, 450), (640, 480)], closed=False, steps=6), 18, 14, 6), palm), (0.55, 0.55, 0.58), 0.7)
    # bracer cuff matching the FPP gloves (#36): brass with a dark zig-zag band
    cuff = m_poly([(322, 652), (702, 652), (738, 912), (286, 912)])
    ic.put(shade(cuff, tex((0.80, 0.58, 0.20), 66, 40, 0.15), bevel=40, spec=0.6, shininess=30))
    zig = [(300 + k * 38, 782 + (36 if k % 2 else -36)) for k in range(13)]
    ic.mul(m_and(m_stroke(zig, 13, caps=False), cuff), 0.32)
    for y in (690, 874):
        ic.mul(m_and(m_stroke([(250, y), (780, y)], 10, caps=False), cuff), 0.45)
    return ic


# ------------------------------------------------------------------ 7. potion_hp_s — Mała Mikstura Życia
def _glass_front(ic, glass, streak):
    d = ndimage.distance_transform_edt(glass > 0.5)
    ic.paint(np.clip(1 - np.abs(d - 10) / 6, 0, 1) * glass, (0.92, 0.97, 1.0), 0.45)
    ic.paint(streak, (1.0, 1.0, 1.0), 0.85)


def potion_hp_s():
    ic = Icon()
    bulb = m_ellipse(512, 640, 258)
    neck = m_rect(452, 258, 572, 440, 10)
    glass = m_or(bulb, neck)
    ic.put(shade(glass, (0.70, 0.80, 0.86), bevel=40, spec=0.4, ambient=0.6), opacity=0.42)
    inner = ndimage.binary_erosion(glass > 0.5, iterations=16).astype(np.float32)
    wave = 480 + 8 * np.sin(XX / 40.0)
    liquid = m_and(inner, (YY > wave).astype(np.float32))
    r = np.sqrt((XX - 540) ** 2 + (YY - 660) ** 2) / 260
    red = lerp_col((1.0, 0.30, 0.25), (0.45, 0.02, 0.05), np.clip(r, 0, 1))
    ic.put(shade(liquid, red, bevel=160, spec=0.5, shininess=50, ambient=0.55, diffuse=0.55, edge=0))
    surf = m_and(liquid, (YY < wave + 18).astype(np.float32))
    ic.paint(surf, (1.0, 0.55, 0.5), 0.45)
    for (x, y, rr) in ((600, 600, 14), (470, 720, 10), (560, 780, 8), (640, 700, 7)):
        ic.paint(m_sub(m_ellipse(x, y, rr), m_ellipse(x + 2, y + 2, rr - 4)), (1.0, 0.75, 0.7), 0.7)
    streak = m_stroke(bezier((330, 690), (320, 520), (420, 430)), 26)
    _glass_front(ic, glass, streak)
    ic.paint(m_ellipse(380, 760, 12), (1, 1, 1), 0.6)
    lip = m_sub(m_ellipse(512, 262, 88, 30), m_ellipse(512, 262, 60, 16))
    ic.put(shade(lip, (0.80, 0.88, 0.92), bevel=12, spec=0.7, ambient=0.6))
    cork = m_rect(464, 150, 560, 262, 20)
    ic.put(shade(cork, tex((0.74, 0.53, 0.32), 71, 18, 0.45), bevel=26, spec=0.1))
    string = m_rect(446, 392, 578, 418, 6)
    ic.put(shade(string, (0.86, 0.76, 0.55), bevel=8, spec=0.2, edge=1.5))
    knot = m_or(m_stroke([(578, 405), (618, 470)], 12), m_stroke([(578, 405), (600, 480)], 12))
    ic.put(shade(knot, (0.82, 0.72, 0.50), bevel=6, spec=0.2, edge=1.5))
    return ic


# ------------------------------------------------------------------ 8. antidote — Antidotum
def antidote():
    ic = Icon()
    pts = [(452, 232), (572, 232), (574, 330), (690, 420), (704, 790), (660, 880), (364, 880), (320, 790),
           (334, 420), (450, 330)]
    body = m_poly(catmull(pts, steps=8))
    ic.put(shade(body, (0.72, 0.84, 0.80), bevel=40, spec=0.4, ambient=0.6), opacity=0.42)
    inner = ndimage.binary_erosion(body > 0.5, iterations=16).astype(np.float32)
    wave = 470 + 7 * np.sin(XX / 36.0 + 1.0)
    liquid = m_and(inner, (YY > wave).astype(np.float32))
    r = np.abs(XX - 530) / 190
    green = lerp_col((0.55, 0.95, 0.40), (0.06, 0.36, 0.14), np.clip(r, 0, 1))
    ic.put(shade(liquid, green, bevel=150, spec=0.5, shininess=50, ambient=0.55, diffuse=0.55, edge=0))
    ic.paint(m_and(liquid, (YY < wave + 16).astype(np.float32)), (0.75, 1.0, 0.6), 0.45)
    label = m_rect(392, 560, 632, 776, 14)
    ic.put(shade(label, tex((0.90, 0.83, 0.64), 81, 20, 0.2), bevel=10, spec=0.05, edge=2))
    leaf = m_poly(catmull([(512, 588), (572, 640), (548, 712), (512, 748), (476, 712), (452, 640)], steps=10))
    ic.put(shade(leaf, (0.24, 0.55, 0.20), bevel=24, spec=0.2))
    ic.mul(m_stroke([(512, 600), (512, 760)], 6), 0.5)
    for k, y in enumerate((636, 676, 712)):
        ic.mul(m_and(m_stroke([(512, y + 14), (512 + (36 - k * 8), y - 10)], 4), leaf), 0.6)
        ic.mul(m_and(m_stroke([(512, y + 14), (512 - (36 - k * 8), y - 10)], 4), leaf), 0.6)
    streak = m_stroke(bezier((362, 800), (352, 600), (372, 450)), 24)
    _glass_front(ic, body, streak)
    twine = m_rect(440, 326, 584, 350, 6)
    ic.put(shade(twine, (0.82, 0.72, 0.50), bevel=8, spec=0.2, edge=1.5))
    wax = m_or(m_ellipse(512, 216, 86, 58), m_stroke([(456, 230), (452, 292)], 26), m_stroke([(566, 236), (570, 270)], 22))
    ic.put(shade(wax, (0.12, 0.34, 0.38), bevel=44, spec=0.55, shininess=34))
    ic.put(shade(m_ellipse(512, 206, 34, 22), (0.18, 0.44, 0.48), bevel=16, spec=0.5))
    return ic


# ------------------------------------------------------------------ 9. ore_shard — Odłamek Rudy
def _crystal(ic, tip, left, base, right, light, dark):
    lf = m_poly([tip, left, base])
    rf = m_poly([tip, base, right])
    ic.put(shade(lf, light, bevel=10, profile="flat", spec=0.35, edge=2, vgrad=0.35))
    ic.put(shade(rf, dark, bevel=10, profile="flat", spec=0.2, edge=2, vgrad=0.35))
    ic.paint(m_stroke([tip, base], 7), (0.92, 0.96, 1.0), 0.55)
    return m_or(lf, rf)


def ore_shard():
    ic = Icon()
    rock = m_poly(catmull([(290, 762), (352, 672), (480, 636), (626, 646), (742, 700), (770, 792), (688, 866),
                           (500, 886), (340, 852)], steps=10))
    ic.put(shade(rock, tex((0.40, 0.35, 0.32), 91, 30, 0.45), bevel=60, spec=0.1))
    flecks = (noise(92, 18, 2) > 0.80).astype(np.float32) * rock
    ic.paint(flecks, (0.78, 0.46, 0.20), 0.85)
    _crystal(ic, (318, 410), (282, 600), (352, 742), (430, 600), (0.60, 0.68, 0.78), (0.30, 0.35, 0.45))
    _crystal(ic, (778, 452), (640, 640), (690, 772), (800, 640), (0.56, 0.64, 0.75), (0.28, 0.33, 0.43))
    _crystal(ic, (560, 132), (418, 360), (512, 752), (664, 352), (0.68, 0.76, 0.86), (0.32, 0.38, 0.50))
    ic.paint(m_poly([(560, 132), (500, 236), (566, 262)]), (0.88, 0.93, 1.0), 0.5)
    for (x, y, s) in ((470, 300, 40), (720, 560, 30), (330, 520, 26)):
        star = m_or(m_poly([(x - s, y), (x, y - 5), (x + s, y), (x, y + 5)]), m_poly([(x, y - s), (x + 5, y), (x, y + s), (x - 5, y)]))
        ic.paint(star, (1.0, 1.0, 1.0), 0.95)
        ic.add_glow(star, (0.5, 0.7, 1.0), radius=8, strength=0.8)
    return ic


# ================================================================== batch 2 (#59 follow-up): remaining item ids in
# docs/design/tables (items_starter.csv, shop_starter_merchant.csv, skill_tier_items.csv / ITEMS_COMBAT_SPEC §4.1)
CLASS_IDS = ["warrior", "ninja", "shaman", "slajter"]  # docs/design/tables/classes_skills.json
ITEMS_BATCH2 = ["spider_silk", "potion_hp_m", "potion_mp_s", "smith_oil", "forget_draught", "bag_expansion"] + \
               ["treatise_proficiency_%s" % c for c in CLASS_IDS] + ["spark_artistry", "seal_apex"]


def _ellipse_pts(cx, cy, rx, ry, n=96):
    return [(cx + rx * math.cos(2 * math.pi * k / n), cy + ry * math.sin(2 * math.pi * k / n)) for k in range(n)]


# ------------------------------------------------------------------ 10. spider_silk — Jedwab Pajęczy
def spider_silk():
    ic = Icon()
    ang = -38
    T = lambda pts: xform(pts, angle_deg=ang, scale=1.0, offset=(512, 500), pivot=(0, 0))
    # local frame (rotated): u along the spindle, v across
    a = math.radians(ang)
    u = (XX - 512) * math.cos(a) + (YY - 500) * math.sin(a)
    v = -(XX - 512) * math.sin(a) + (YY - 500) * math.cos(a)
    stick = m_poly(T([(-430, -20), (430, -20), (430, 20), (-430, 20)]))
    ic.put(shade(stick, tex((0.55, 0.38, 0.22), 101, 30, 0.3), bevel=18, spec=0.15))
    for end in (-430, 430):
        e = T([(end, 0)])[0]
        ic.put(shade(m_ellipse(e[0], e[1], 44), tex((0.46, 0.30, 0.17), 102, 20, 0.3), bevel=44, spec=0.25))
    skein = m_poly(T(_ellipse_pts(0, 0, 300, 205)))
    # wound threads: two crossing helices modulate height and colour
    w1 = 0.5 + 0.5 * np.sin((u + v * 0.35) * 2 * math.pi / 22)
    w2 = 0.5 + 0.5 * np.sin((u * 0.6 - v * 0.8) * 2 * math.pi / 70)
    wind = 0.75 * w1 ** 2 + 0.25 * w2 ** 4
    silk = lerp_col((0.78, 0.79, 0.86), (0.99, 0.99, 1.0), wind)
    ic.put(shade(skein, silk, bevel=160, profile="round", spec=0.55, shininess=30, ambient=0.55, diffuse=0.6,
                 extra_height=wind * 3.0))
    sheen = m_and(m_stroke(T(bezier((-200, -120), (-40, -190), (160, -150))), 34), skein)
    ic.paint(ndimage.gaussian_filter(sheen, 8), (0.86, 1.0, 0.92), 0.55)
    # loose strand drifting down with a venom-green bead
    strand = m_stroke(bezier(T([(250, 110)])[0], (760, 760), (520, 900), (380, 820)), 9, caps=True)
    ic.put(shade(strand, (0.93, 0.94, 0.98), bevel=4, spec=0.5, edge=1.2))
    ic.put(shade(m_ellipse(380, 820, 24), (0.45, 0.85, 0.30), bevel=24, spec=0.9, shininess=60, ambient=0.6))
    return ic


# ------------------------------------------------------------------ potions (shared round flask)
def _round_flask(ic, light, deep, surf_col, bubble_col, seed_cork=71):
    bulb = m_ellipse(512, 640, 258)
    neck = m_rect(452, 258, 572, 440, 10)
    glass = m_or(bulb, neck)
    ic.put(shade(glass, (0.70, 0.80, 0.86), bevel=40, spec=0.4, ambient=0.6), opacity=0.42)
    inner = ndimage.binary_erosion(glass > 0.5, iterations=16).astype(np.float32)
    wave = 480 + 8 * np.sin(XX / 40.0)
    liquid = m_and(inner, (YY > wave).astype(np.float32))
    r = np.sqrt((XX - 540) ** 2 + (YY - 660) ** 2) / 260
    ic.put(shade(liquid, lerp_col(light, deep, np.clip(r, 0, 1)), bevel=160, spec=0.5, shininess=50, ambient=0.55,
                 diffuse=0.55, edge=0))
    ic.paint(m_and(liquid, (YY < wave + 18).astype(np.float32)), surf_col, 0.45)
    for (x, y, rr) in ((600, 600, 14), (470, 720, 10), (560, 780, 8), (640, 700, 7)):
        ic.paint(m_sub(m_ellipse(x, y, rr), m_ellipse(x + 2, y + 2, rr - 4)), bubble_col, 0.7)
    _glass_front(ic, glass, m_stroke(bezier((330, 690), (320, 520), (420, 430)), 26))
    ic.paint(m_ellipse(380, 760, 12), (1, 1, 1), 0.6)
    lip = m_sub(m_ellipse(512, 262, 88, 30), m_ellipse(512, 262, 60, 16))
    ic.put(shade(lip, (0.80, 0.88, 0.92), bevel=12, spec=0.7, ambient=0.6))
    return liquid


def potion_mp_s():
    """Same small round flask as potion_hp_s (one family per size), blue mana and a blue wax-dipped cork."""
    ic = Icon()
    liquid = _round_flask(ic, (0.40, 0.70, 1.0), (0.03, 0.10, 0.45), (0.60, 0.85, 1.0), (0.75, 0.90, 1.0))
    for (x, y, s) in ((585, 690, 26), (480, 630, 18)):
        star = m_or(m_poly([(x - s, y), (x, y - 4), (x + s, y), (x, y + 4)]), m_poly([(x, y - s), (x + 4, y), (x, y + s), (x - 4, y)]))
        ic.paint(m_and(star, liquid), (0.85, 0.95, 1.0), 0.9)
        ic.add_glow(m_and(star, liquid), (0.3, 0.6, 1.0), radius=8, strength=0.7)
    cork = m_rect(464, 150, 560, 262, 20)
    ic.put(shade(cork, tex((0.74, 0.53, 0.32), 111, 18, 0.45), bevel=26, spec=0.1))
    dip = m_and(cork, m_rect(0, 0, S, 196))
    ic.put(shade(m_or(dip, m_ellipse(512, 160, 58, 26)), (0.16, 0.30, 0.62), bevel=20, spec=0.6, shininess=36))
    ic.put(shade(m_rect(446, 392, 578, 418, 6), (0.86, 0.76, 0.55), bevel=8, spec=0.2, edge=1.5))
    return ic


# ------------------------------------------------------------------ 11. potion_hp_m — Mikstura Życia (gourd flask)
def potion_hp_m():
    ic = Icon()
    low = m_ellipse(512, 690, 238, 206)
    up = m_ellipse(512, 384, 150, 132)
    waist = m_rect(420, 440, 604, 560, 30)
    neck = m_rect(462, 168, 562, 280, 10)
    glass = m_or(low, up, waist, neck)
    ic.put(shade(glass, (0.70, 0.80, 0.86), bevel=40, spec=0.4, ambient=0.6), opacity=0.42)
    inner = ndimage.binary_erosion(glass > 0.5, iterations=16).astype(np.float32)
    wave = 330 + 7 * np.sin(XX / 34.0)
    liquid = m_and(inner, (YY > wave).astype(np.float32))
    r = np.sqrt((XX - 548) ** 2 + (YY - 640) ** 2) / 330
    ic.put(shade(liquid, lerp_col((1.0, 0.32, 0.26), (0.42, 0.02, 0.05), np.clip(r, 0, 1)), bevel=160, spec=0.5,
                 shininess=50, ambient=0.55, diffuse=0.55, edge=0))
    ic.paint(m_and(liquid, (YY < wave + 16).astype(np.float32)), (1.0, 0.55, 0.5), 0.45)
    for (x, y, rr) in ((610, 720, 14), (450, 780, 10), (560, 420, 9), (640, 640, 7)):
        ic.paint(m_sub(m_ellipse(x, y, rr), m_ellipse(x + 2, y + 2, rr - 4)), (1.0, 0.75, 0.7), 0.7)
    _glass_front(ic, glass, m_or(m_stroke(bezier((334, 760), (322, 620), (392, 548)), 26),
                                 m_stroke(bezier((404, 420), (398, 350), (440, 300)), 18)))
    # gold collar at the waist with a heart-shaped red stone
    band = m_and(m_rect(380, 470, 644, 532, 0), m_or(waist, low, up))
    ic.put(shade(band, GOLD, bevel=16, spec=0.7, shininess=36))
    heart = m_or(m_ellipse(496, 494, 22), m_ellipse(528, 494, 22), m_poly([(476, 502), (548, 502), (512, 546)]))
    ic.put(shade(heart, (0.85, 0.08, 0.10), bevel=22, spec=0.9, shininess=60))
    lip = m_sub(m_ellipse(512, 172, 80, 28), m_ellipse(512, 172, 54, 14))
    ic.put(shade(lip, (0.80, 0.88, 0.92), bevel=12, spec=0.7, ambient=0.6))
    cork = m_rect(468, 70, 556, 172, 20)
    ic.put(shade(cork, tex((0.74, 0.53, 0.32), 121, 18, 0.45), bevel=26, spec=0.1))
    ic.put(shade(m_ellipse(512, 92, 56, 26), (0.62, 0.06, 0.08), bevel=20, spec=0.6, shininess=36))
    return ic


# ------------------------------------------------------------------ 12. smith_oil — Olej Kowalski (stoneware jug)
def smith_oil():
    ic = Icon()
    handle = m_sub(m_stroke(bezier((600, 300), (800, 260), (830, 480), (690, 560)), 54), m_ellipse(512, 600, 230, 250))
    ic.put(shade(handle, tex((0.42, 0.25, 0.15), 131, 30, 0.3), bevel=26, spec=0.25))
    body = m_poly(catmull([(512, 330), (650, 360), (740, 500), (746, 680), (680, 840), (512, 890), (344, 840),
                           (278, 680), (284, 500), (374, 360)], steps=10))
    neck = m_rect(440, 200, 584, 380, 24)
    jug = m_or(body, neck)
    clay = tex((0.72, 0.56, 0.40), 132, 40, 0.25)
    glaze = tex((0.36, 0.20, 0.12), 133, 50, 0.2)
    dip = 560 + 26 * np.sin(XX / 55.0) + 18 * np.sin(XX / 23.0 + 1.3)
    t = np.clip((YY - dip) / 10.0, 0, 1)[..., None]
    ic.put(shade(jug, glaze * (1 - t) + clay * t, bevel=110, spec=0.45, shininess=26, vgrad=0.12))
    for y in (650, 700):
        ic.mul(m_and(m_stroke([(200, y), (820, y + 6)], 6, caps=False), body), 0.72)
    lip = m_rect(418, 176, 606, 234, 22)
    ic.put(shade(lip, (0.30, 0.17, 0.10), bevel=18, spec=0.45))
    stopper = m_rect(460, 108, 564, 192, 18)
    ic.put(shade(stopper, tex((0.74, 0.53, 0.32), 134, 18, 0.45), bevel=24, spec=0.1))
    # anvil stamp on the belly
    anvil = m_poly([(430, 712), (600, 712), (640, 690), (660, 712), (600, 740), (566, 742), (580, 790), (450, 790),
                    (464, 742), (430, 740)])
    ic.mul(m_and(ndimage.binary_dilation(anvil > 0.5, iterations=6).astype(np.float32), body), 0.6)
    ic.put(shade(anvil, (0.62, 0.47, 0.33), bevel=8, spec=0.2, edge=1.5), opacity=0.9)
    # golden oil run from the lip down the side
    path = catmull([(440, 236), (436, 320), (398, 400), (360, 480), (346, 530)], closed=False, steps=8)
    run = m_or(*[m_stroke(path[k:k + 2], 34 - 22 * k / len(path), caps=True) for k in range(len(path) - 1)])
    run = m_or(run, m_rect(420, 226, 470, 250, 10))  # oil pooled on the lip
    oil = lerp_col((1.0, 0.86, 0.35), (0.80, 0.50, 0.06), np.clip((YY - 230) / 400, 0, 1))
    ic.put(shade(run, oil, bevel=8, profile="flat", spec=0.9, shininess=60, ambient=0.75, edge=2, edge_dark=0.3), opacity=0.92)
    drop = m_or(m_ellipse(342, 586, 34, 40), m_poly([(316, 574), (368, 574), (346, 514)]))
    ic.put(shade(drop, (0.96, 0.70, 0.16), bevel=40, spec=1.0, shininess=70, ambient=0.75))
    ic.paint(m_ellipse(332, 570, 9), (1, 1, 0.92), 0.8)
    return ic


# ------------------------------------------------------------------ 13. forget_draught — Napar Zapomnienia
def forget_draught():
    ic = Icon()
    body = m_poly(catmull([(450, 300), (574, 300), (580, 420), (760, 760), (742, 860), (512, 892), (282, 860),
                           (264, 760), (444, 420)], steps=8))
    neck = m_rect(452, 190, 572, 330, 10)
    glass = m_or(body, neck)
    ic.put(shade(glass, (0.76, 0.74, 0.86), bevel=40, spec=0.4, ambient=0.6), opacity=0.42)
    inner = ndimage.binary_erosion(glass > 0.5, iterations=16).astype(np.float32)
    wave = 560 + 8 * np.sin(XX / 38.0 + 0.6)
    liquid = m_and(inner, (YY > wave).astype(np.float32))
    r = np.sqrt((XX - 540) ** 2 + (YY - 760) ** 2) / 300
    ic.put(shade(liquid, lerp_col((0.78, 0.50, 1.0), (0.20, 0.04, 0.36), np.clip(r, 0, 1)), bevel=150, spec=0.5,
                 shininess=50, ambient=0.55, diffuse=0.55, edge=0))
    ic.paint(m_and(liquid, (YY < wave + 16).astype(np.float32)), (0.88, 0.72, 1.0), 0.45)
    # swirl (forgetting) inside the liquid
    spiral = [(520 + (12 + 9 * k) * math.cos(k * 0.55), 740 + (12 + 9 * k) * 0.55 * math.sin(k * 0.55)) for k in range(22)]
    sw = m_and(m_stroke(spiral, 12), liquid)
    ic.paint(sw, (0.95, 0.85, 1.0), 0.75)
    ic.add_glow(sw, (0.55, 0.25, 0.85), radius=12, strength=0.8)
    _glass_front(ic, glass, m_stroke(bezier((330, 800), (380, 660), (444, 520)), 22))
    lip = m_sub(m_ellipse(512, 194, 86, 30), m_ellipse(512, 194, 58, 16))
    ic.put(shade(lip, (0.84, 0.84, 0.92), bevel=12, spec=0.7, ambient=0.6))
    # vapour curling out of the open neck
    for k, (pts, w) in enumerate((([(512, 186), (474, 150), (540, 118), (500, 88)], 34),
                                   ([(534, 180), (600, 150), (584, 116), (636, 96)], 24))):
        wisp = ndimage.gaussian_filter(m_stroke(catmull(pts, closed=False, steps=10), w), 7)
        ic.paint(wisp, (0.82, 0.70, 0.98), 0.75 - 0.15 * k)
    return ic


# ------------------------------------------------------------------ 14. bag_expansion — Sakwa +8 miejsc
def bag_expansion():
    ic = Icon()
    pouch = m_poly(catmull([(512, 330), (640, 340), (760, 470), (800, 650), (740, 830), (512, 896), (284, 830),
                            (224, 650), (264, 470), (384, 340)], steps=10))
    leather = tex((0.50, 0.31, 0.17), 141, 46, 0.3)
    ic.put(shade(pouch, leather, bevel=150, profile="round", spec=0.2, shininess=16, vgrad=0.2))
    for x0, x1 in ((420, 330), (470, 420), (554, 604), (604, 694)):
        ic.mul(m_and(m_stroke([(x0, 360), (x1, 560)], 10), pouch), 0.72)
    ruff = m_poly(catmull([(512, 200), (600, 196), (676, 240), (650, 330), (512, 352), (374, 330), (348, 240),
                           (424, 196)], steps=10))
    ic.put(shade(ruff, tex((0.58, 0.38, 0.22), 142, 36, 0.3), bevel=40, spec=0.15))
    for x in (420, 470, 512, 554, 604):
        ic.mul(m_and(m_stroke([(x, 210), (x + (x - 512) * 0.2, 336)], 9), ruff), 0.62)
    cord = m_rect(352, 326, 672, 360, 14)
    ic.put(shade(cord, (0.86, 0.76, 0.55), bevel=10, spec=0.2, edge=1.5))
    for pts in (bezier((640, 344), (720, 380), (740, 470)), bezier((648, 350), (690, 420), (680, 500))):
        ic.put(shade(m_stroke(pts, 14), (0.82, 0.72, 0.50), bevel=7, spec=0.2, edge=1.5))
    for (x, y) in ((740, 486), (680, 516)):
        ic.put(shade(m_rect(x - 16, y - 20, x + 16, y + 34, 10), (0.70, 0.18, 0.12), bevel=12, spec=0.3))
    ic.paint(m_and(dashes(catmull([(300, 760), (512, 840), (724, 760)], closed=False, steps=8), 20, 16, 6), pouch),
             (0.84, 0.70, 0.48), 0.75)
    # brass "more room" badge: plus sign on a round plate
    ic.put(shade(m_ellipse(512, 640, 128), GOLD, bevel=34, spec=0.75, shininess=40))
    ic.put(shade(m_ellipse(512, 640, 98), (0.66, 0.44, 0.14), bevel=14, spec=0.4, ambient=0.5))
    plus = m_or(m_rect(492, 570, 532, 710, 8), m_rect(442, 620, 582, 660, 8))
    ic.put(shade(plus, (0.98, 0.84, 0.44), bevel=14, spec=0.9, shininess=50))
    return ic


# ------------------------------------------------------------------ 15–18. treatise_proficiency_<classId> — Traktat Biegłości
TREATISE = {  # cover colour, emblem (own design per class role in CLASSES_SKILLS.md)
    "warrior": ((0.56, 0.12, 0.10), "sword"),
    "ninja": ((0.10, 0.36, 0.30), "star"),
    "shaman": ((0.12, 0.26, 0.56), "bolt"),
    "slajter": ((0.34, 0.14, 0.46), "moon"),
}


def _emblem(kind, cx, cy):
    if kind == "sword":
        return m_or(m_poly([(cx - 18, cy + 60), (cx - 18, cy - 90), (cx, cy - 122), (cx + 18, cy - 90), (cx + 18, cy + 60)]),
                    m_rect(cx - 64, cy + 54, cx + 64, cy + 78, 10), m_rect(cx - 12, cy + 76, cx + 12, cy + 118, 4),
                    m_ellipse(cx, cy + 126, 18))
    if kind == "star":
        p = []
        for k in range(8):
            r = 128 if k % 2 == 0 else 38
            a = math.pi / 4 * k + math.pi / 4
            p.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        return m_sub(m_poly(p), m_ellipse(cx, cy, 20))
    if kind == "bolt":
        return m_poly([(cx + 30, cy - 130), (cx - 62, cy + 14), (cx - 4, cy + 14), (cx - 36, cy + 130), (cx + 66, cy - 22),
                       (cx + 6, cy - 22), (cx + 44, cy - 130)])
    # moon
    return m_or(m_sub(m_ellipse(cx, cy, 116), m_ellipse(cx + 54, cy - 30, 100)), m_ellipse(cx + 54, cy - 36, 16))


def _treatise(class_id):
    cover_col, emblem = TREATISE[class_id]
    ic = Icon()
    T = lambda pts: xform(pts, angle_deg=-7, offset=(512, 520))
    pages = m_poly(T([(-250, -350), (300, -340), (300, 352), (-250, 362)]))
    page_tex = lerp_col((0.80, 0.74, 0.60), (0.96, 0.92, 0.80), 0.5 + 0.5 * np.sin((XX * 0.12 + YY * 0.99) / 3.2))
    ic.put(shade(pages, page_tex, bevel=10, profile="flat", spec=0.05, edge=2))
    cover = m_poly(T([(-280, -380), (270, -380), (270, 330), (-280, 330)]))
    ic.put(shade(cover, tex(cover_col, 150 + CLASS_IDS.index(class_id), 40, 0.3), bevel=36, spec=0.3, shininess=20))
    spine = m_poly(T([(-300, -392), (-200, -392), (-200, 342), (-300, 342)]))
    ic.put(shade(spine, tex(tuple(c * 0.7 for c in cover_col), 155, 40, 0.3), bevel=30, spec=0.3))
    for y in (-290, -150, 80, 230):
        ic.put(shade(m_and(m_poly(T([(-306, y - 16), (-194, y - 16), (-194, y + 16), (-306, y + 16)])), spine),
                     GOLD, bevel=10, spec=0.6))
    # gold corner caps
    for (x, y, sx, sy) in ((270, -380, -1, 1), (270, 330, -1, -1)):
        ic.put(shade(m_poly(T([(x, y), (x + sx * 110, y), (x, y + sy * 110)])), GOLD, bevel=14, spec=0.7, shininess=36))
    # embossed frame and emblem plate
    frame = m_sub(m_poly(T([(-160, -320), (220, -320), (220, 270), (-160, 270)])),
                  m_poly(T([(-140, -300), (200, -300), (200, 250), (-140, 250)])))
    ic.put(shade(frame, GOLD, bevel=8, spec=0.6), opacity=0.9)
    c = T([(30, -25)])[0]
    ic.put(shade(m_ellipse(c[0], c[1], 160), GOLD, bevel=30, spec=0.7, shininess=40))
    ic.put(shade(m_ellipse(c[0], c[1], 136), tuple(x * 0.6 for x in cover_col), bevel=12, spec=0.25, ambient=0.5))
    em = _emblem(emblem, c[0], c[1])
    ic.put(shade(em, (0.98, 0.82, 0.42), bevel=16, spec=0.85, shininess=50))
    # bookmark ribbon
    rb = T([(120, 320), (170, 320), (170, 450), (145, 425), (120, 450)])
    ic.put(shade(m_poly(rb), (0.86, 0.72, 0.28), bevel=10, spec=0.4))
    return ic


def treatise_proficiency_warrior():
    return _treatise("warrior")


def treatise_proficiency_ninja():
    return _treatise("ninja")


def treatise_proficiency_shaman():
    return _treatise("shaman")


def treatise_proficiency_slajter():
    return _treatise("slajter")


# ------------------------------------------------------------------ 19. spark_artistry — Iskra Kunsztu
def spark_artistry():
    ic = Icon()
    cx, cy = 512, 520
    rays = np.zeros((S, S), np.float32)
    for k in range(8):
        a = math.pi / 4 * k
        L, w = (410, 60) if k % 2 == 0 else (270, 40)
        tip = (cx + L * math.cos(a), cy + L * math.sin(a))
        l = (cx + w * math.cos(a + math.pi / 2), cy + w * math.sin(a + math.pi / 2))
        r = (cx + w * math.cos(a - math.pi / 2), cy + w * math.sin(a - math.pi / 2))
        rays = np.maximum(rays, m_poly([l, tip, r]))
    ic.put(shade(rays, (1.0, 0.78, 0.30), bevel=14, profile="flat", spec=0.5, ambient=0.85, diffuse=0.3, edge=2))
    ic.add_glow(rays, (1.0, 0.45, 0.05), radius=26, strength=0.55)
    # faceted ember gem in the middle
    top, bot, lft, rgt = (cx, cy - 290), (cx, cy + 290), (cx - 215, cy), (cx + 215, cy)
    facets = [((top, lft, (cx, cy)), (1.0, 0.92, 0.55)), ((top, rgt, (cx, cy)), (1.0, 0.68, 0.18)),
              ((bot, lft, (cx, cy)), (0.95, 0.52, 0.10)), ((bot, rgt, (cx, cy)), (0.75, 0.30, 0.04))]
    gem = np.zeros((S, S), np.float32)
    for tri, c in facets:
        m = m_poly(tri)
        gem = np.maximum(gem, m)
        ic.put(shade(m, c, bevel=8, profile="flat", spec=0.4, ambient=0.9, diffuse=0.3, edge=1.5))
    core = ndimage.gaussian_filter(m_ellipse(cx - 20, cy - 40, 60), 24) * gem
    ic.paint(core, (1.0, 1.0, 0.85), 0.9)
    ic.add_glow(gem, (1.0, 0.55, 0.10), radius=30, strength=0.6)
    for (x, y, s) in ((300, 300, 16), (740, 320, 12), (760, 720, 18), (290, 740, 12), (520, 860, 10)):
        e = m_ellipse(x, y, s)
        ic.paint(e, (1.0, 0.95, 0.7), 1.0)
        ic.add_glow(e, (1.0, 0.5, 0.1), radius=10, strength=0.9)
    return ic


# ------------------------------------------------------------------ 20. seal_apex — Pieczęć Szczytu
def seal_apex():
    ic = Icon()
    cx, cy = 512, 450
    for sx in (-1, 1):
        tail = m_poly([(cx + sx * 40, cy + 160), (cx + sx * 170, cy + 200), (cx + sx * 250, cy + 470),
                       (cx + sx * 180, cy + 430), (cx + sx * 140, cy + 500)])
        ic.put(shade(tail, tex((0.66, 0.08, 0.10), 160, 30, 0.2), bevel=24, spec=0.35))
        ic.mul(m_and(m_stroke([(cx + sx * 100, cy + 200), (cx + sx * 190, cy + 450)], 8), tail), 0.7)
    p = []
    for k in range(144):
        a = 2 * math.pi * k / 144
        r = 330 + 16 * math.cos(24 * a)
        p.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    rim = m_poly(p)
    ic.put(shade(rim, GOLD, bevel=40, spec=0.75, shininess=40))
    ic.put(shade(m_ellipse(cx, cy, 262), (0.70, 0.48, 0.14), bevel=18, spec=0.45, ambient=0.5))
    for k in range(32):
        a = 2 * math.pi * k / 32
        ic.put(shade(m_ellipse(cx + 236 * math.cos(a), cy + 236 * math.sin(a), 9), GOLD, bevel=9, spec=0.8))
    # mountain peak (apex) with a star above it
    mtn = m_poly([(cx - 190, cy + 140), (cx - 80, cy - 20), (cx - 30, cy + 40), (cx + 40, cy - 130), (cx + 190, cy + 140)])
    ic.put(shade(mtn, (0.95, 0.76, 0.32), bevel=26, profile="flat", spec=0.8, shininess=44))
    snow = m_and(mtn, m_poly([(cx + 40, cy - 130), (cx - 10, cy - 50), (cx + 20, cy - 30), (cx + 50, cy - 60),
                              (cx + 80, cy - 40), (cx + 100, cy - 60)]))
    ic.put(shade(snow, (1.0, 0.96, 0.82), bevel=10, profile="flat", spec=0.9))
    s, x, y = 54, cx + 40, cy - 190
    star = m_poly([(x + (s if k % 2 == 0 else s * 0.4) * math.cos(-math.pi / 2 + k * math.pi / 5),
                    y + (s if k % 2 == 0 else s * 0.4) * math.sin(-math.pi / 2 + k * math.pi / 5)) for k in range(10)])
    ic.put(shade(star, (1.0, 0.92, 0.6), bevel=14, spec=0.9, shininess=50))
    ic.add_glow(star, (1.0, 0.8, 0.3), radius=12, strength=0.6)
    return ic


ALL_ITEMS = ITEMS + ITEMS_BATCH2
BUILDERS = {k: globals()[k] for k in ALL_ITEMS}


def contact_sheet(icons, path, items=ITEMS, cols=3,
                  title="METINAR-032 — starter item icons (256² PNG, original, MetinAR-Owned)"):
    cell, pad, lab = 256, 28, 34
    rows = (len(items) + cols - 1) // cols
    W = cols * cell + (cols + 1) * pad
    H = rows * (cell + lab) + (rows + 1) * pad + 40
    sheet = Image.new("RGBA", (W, H), (38, 36, 40, 255))
    d = ImageDraw.Draw(sheet)
    try:
        font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 17)
        title_font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 19)
    except OSError:
        font = title_font = ImageFont.load_default()
    d.text((pad, 12), title, fill=(230, 222, 200), font=title_font)
    for i, item in enumerate(items):
        r, c = divmod(i, cols)
        x = pad + c * (cell + pad)
        y = 40 + pad + r * (cell + lab + pad)
        d.rounded_rectangle([x - 4, y - 4, x + cell + 4, y + cell + 4], 14, fill=(64, 60, 58, 255), outline=(120, 112, 100, 255), width=2)
        sheet.alpha_composite(icons[item], (x, y))
        d.text((x + 4, y + cell + 8), item, fill=(220, 214, 196), font=font)
    sheet.save(path)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = args[0] if args else "out"
    masters = "--masters" in sys.argv
    only = [a.split("=", 1)[1].split(",") for a in sys.argv[1:] if a.startswith("--only=")]
    items = only[0] if only else ALL_ITEMS
    os.makedirs(out, exist_ok=True)
    icons = {}
    for item in items:
        big = BUILDERS[item]().finish()
        if masters:
            Image.fromarray(big, "RGBA").save(os.path.join(out, "master_%s.png" % item))
        im = downsample(big, 256)
        icons[item] = im
        im.save(os.path.join(out, "T_ItemIcons_%s_BC.png" % pascal(item)), optimize=True)
        print(item, "->", "T_ItemIcons_%s_BC.png" % pascal(item))
    if all(i in icons for i in ITEMS):
        contact_sheet(icons, os.path.join(out, "contact_sheet.png"))
    if all(i in icons for i in ITEMS_BATCH2):
        contact_sheet(icons, os.path.join(out, "contact_sheet_batch2.png"), ITEMS_BATCH2, cols=4,
                      title="METINAR-032 batch 2 — item icons (256² PNG, original, MetinAR-Owned)")


if __name__ == "__main__":
    main()
