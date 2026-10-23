"""METINAR-FPP-ART final gear (P4): warrior gauntlets/forearms, 1H sword and 2H greatsword (original, procedural).

Style: dark oriental fantasy, stylised hand-painted metal / leather / energy. Geometry is built from swept
tubes and open armour shells (plates with rim walls, no hidden inner skin) on top of the unchanged P3 hand rig:
finger paths, radii, grip tightening, shoulders and bone layout come from fpp_model, so the V3 contract and the
grip fit are untouched. Sword model units are scale 1 (the prefab nests the weapon at C.SWORD_SCALE).
"""
import math

import bpy
from mathutils import Vector

import fpp_contract as C
import fpp_model as M
from fpp_mesh import Builder, blend_weights, catmull, frames_along

# arms regions (Data.R * 10)
REG_LEATHER, REG_METAL, REG_GOLD, REG_CLOTH, REG_ENERGY, REG_STRAP = 1, 2, 3, 4, 5, 6
# sword regions
REG_BLADE, REG_FULLER, REG_GUARD, REG_SGOLD, REG_GRIP, REG_GEM = 1, 2, 3, 4, 5, 6

SHARP_ARMS = 50.0
SHARP_SWORD = 38.0


# ================================================================ helpers
def shell(b, surf, inner, n_i, n_j, region, wfn, axis_fn, closed=False, walls=True, v_const=None):
    """Open plate: top surface surf(i, j) (faces oriented away from axis_fn(i)), plus rim walls down to inner(i, j)."""
    def uv(i, j):
        return i / max(1, n_i - 1), (v_const if v_const is not None else j / max(1, n_j - 1))

    top = [[b.vert(surf(i, j), wfn(i), region, *uv(i, j)) for j in range(n_j)] for i in range(n_i)]
    b.grid(top, [axis_fn(i) for i in range(n_i)], closed)
    if not walls:
        return top
    cache = {}

    def bot(i, j):
        if (i, j) not in cache:
            cache[(i, j)] = b.vert(inner(i, j), wfn(i), region, *uv(i, j))
        return cache[(i, j)]

    jr = range(n_j) if closed else range(n_j - 1)
    for i, i_in in ((0, 1), (n_i - 1, n_i - 2)):
        for j in jr:
            j2 = (j + 1) % n_j
            b.face((top[i][j], top[i][j2], bot(i, j2), bot(i, j)), (surf(i_in, j) + surf(i_in, j2)) * 0.5)
    if not closed:
        for j, j_in in ((0, 1), (n_j - 1, n_j - 2)):
            for i in range(n_i - 1):
                b.face((top[i][j], top[i + 1][j], bot(i + 1, j), bot(i, j)), (surf(i, j_in) + surf(i + 1, j_in)) * 0.5)
    return top


def dome(b, centre, normal, radius, height, sides, region, w, ring_frac=0.72):
    """Faceted cabochon (gem) on a surface: base ring, upper ring, pole."""
    n = Vector(normal).normalized()
    t1 = n.orthogonal().normalized()
    t2 = n.cross(t1)
    base = [b.vert(centre + (t1 * math.cos(a) + t2 * math.sin(a)) * radius, w, region, 0.0, k / sides)
            for k, a in ((k, 2 * math.pi * k / sides) for k in range(sides))]
    up = [b.vert(centre + n * (height * 0.62) + (t1 * math.cos(a) + t2 * math.sin(a)) * radius * ring_frac, w, region, 0.6,
                 k / sides) for k, a in ((k, 2 * math.pi * k / sides + math.pi / sides) for k in range(sides))]
    pole = b.vert(centre + n * height, w, region, 1.0, 0.5)
    ref = centre - n * radius
    for k in range(sides):
        k2 = (k + 1) % sides
        b.face((base[k], base[k2], up[k]), ref)
        b.face((up[k], base[k2], up[k2]), ref)
        b.face((up[k], up[k2], pole), ref)
    return base


def spike(b, base, tip, radius, sides, region, w, hint):
    """Cone from a ring at `base` to a point `tip` (flame spikes, studs)."""
    base, tip = Vector(base), Vector(tip)
    ax = (tip - base).normalized()
    n = (Vector(hint) - ax * Vector(hint).dot(ax)).normalized()
    bn = ax.cross(n)
    ring = [b.vert(base + (n * math.cos(2 * math.pi * k / sides) + bn * math.sin(2 * math.pi * k / sides)) * radius, w, region,
                   0.0, k / sides) for k in range(sides)]
    p = b.vert(tip, w, region, 1.0, 0.5)
    b.fan(ring, p, base - ax * radius)
    return ring


# ================================================================ arms
GLOVE_ID = "GloveWarrior01"  # default glove set (M_FppArms_<GloveId>, T_FppArms_<GloveId>_BC/MS/N)


def build_arms():
    """Single-builder arms (one object, no glove split). Kept for quick checks."""
    b = Builder()
    for side in (1, -1):
        _build_arm(b, side)
    return b.to_object("SK_Player_FppArms", sharp_angle=SHARP_ARMS)


def build_arms_parts():
    """Glove-swap split (spec §9 L412): returns (glove, arms) objects with identical rig weighting.

    glove = hand, fingers, thumb, knuckle guard, bell cuff + cuff gem (material slot 0, M_FppArms_<GloveId>)
    arms  = padded under-sleeve, vambrace lames, straps/buckles, cloth sleeve (material slot 1, M_FppArms)
    The two are textured separately and then joined into SK_Player_FppArms (one skinned mesh, 2 submeshes),
    so a glove change in Unity is a material swap on slot 0 (same UV layout) or a mesh swap of the glove part.
    """
    bg, ba = Builder(), Builder()
    for side in (1, -1):
        _build_arm(bg, side, ba)
    return (bg.to_object("SK_Player_FppArms_Glove", sharp_angle=SHARP_ARMS),
            ba.to_object("SK_Player_FppArms_Sleeve", sharp_angle=SHARP_ARMS))


def _radial(p):
    """Outward (away from the hilt axis, design y) unit direction at a design point."""
    v = Vector((p.x, 0.0, p.z))
    return v.normalized() if v.length > 1e-6 else Vector((1.0, 0.0, 0.0))


def _finger(b, W, pts, radius_fn, sides, weights_fn, lames, tip_dome=True):
    """Leather glove finger along design points + dorsal armour lames. lames: [(i0, i1, pointed)] station ranges."""
    frames = frames_along(pts, lambda i, t: _radial(pts[i]))
    n = len(frames)
    rings = []
    for i, (p, t, nr, bn) in enumerate(frames):
        r = radius_fn(i, n)
        ring = [b.vert(W(p + (nr * math.cos(a) + bn * math.sin(a)) * r), weights_fn(i, n), REG_LEATHER, i / (n - 1), k / sides)
                for k, a in ((k, 2 * math.pi * k / sides) for k in range(sides))]
        rings.append(ring)
    b.grid(rings, [W(f[0]) for f in frames], closed=True)
    if tip_dome:
        p, t, nr, bn = frames[-1]
        r = radius_fn(n - 1, n)
        prev = rings[-1]
        for k_len, k_r in ((0.45, 0.82), (0.80, 0.48)):
            c = p + t * (r * k_len)
            ring = [b.vert(W(c + (nr * math.cos(a) + bn * math.sin(a)) * r * k_r), weights_fn(n - 1, n), REG_LEATHER, 1.0, k / sides)
                    for k, a in ((k, 2 * math.pi * k / sides) for k in range(sides))]
            b.strip(prev, ring, W(p), closed=True)
            prev = ring
        pole = b.vert(W(p + t * (r * 0.98)), weights_fn(n - 1, n), REG_LEATHER, 1.0, 0.5)
        b.fan(prev, pole, W(p))
    # dorsal lames (dark lacquered steel), each a shell over the glove
    for li, (i0, i1, pointed) in enumerate(lames):
        b.seed = 0.2 + 0.15 * li
        cols = 7
        phis = [math.radians(-58 + 116 * j / (cols - 1)) for j in range(cols)]
        idx = list(range(i0, i1 + 1))

        def surf(ii, j, lift=0.0016, idx=idx, pointed=pointed):
            i = idx[ii]
            p, t, nr, bn = frames[i]
            r = radius_fn(i, n)
            ph = phis[j]
            ridge = 0.0009 * max(0.0, 1.0 - abs(ph) / math.radians(22))
            shift = 0.0
            if pointed and ii == len(idx) - 1:
                shift = 0.0045 * math.cos(ph * 1.4) ** 2
            if ii == 0:
                lift += 0.0006  # proximal edge sits over the previous lame
            return W(p + t * shift + (nr * math.cos(ph) + bn * math.sin(ph)) * (r + lift + ridge))

        def inner(ii, j, idx=idx):
            i = idx[ii]
            p, t, nr, bn = frames[i]
            ph = phis[j]
            return W(p + (nr * math.cos(ph) + bn * math.sin(ph)) * (radius_fn(i, n) - 0.0012))

        shell(b, surf, inner, len(idx), cols, REG_METAL, lambda ii, idx=idx: weights_fn(idx[ii], n),
              lambda ii, idx=idx: W(frames[idx[ii]][0]))
    b.seed = 0.5
    return frames


def _build_arm(b, side, ba=None):
    bg = b
    ba = ba or b
    s = M.side_tag(side)
    hand_b, fore_b, up_b = "B_Hand_" + s, "B_Forearm_" + s, "B_UpperArm_" + s
    pos, rot = M.hand_frame(side)

    def W(p):  # design -> Blender world (rest)
        return C.to_blender_point(C.frame_point(pos, rot, M.design_to_local(p, side)))

    hw = {hand_b: 1.0}
    PX = M.PALM_X

    # ---------------- palm (leather), elliptic: thickness along design X, width along the hilt (design Y)
    zs = (-0.086, -0.070, -0.050, -0.026, -0.002, 0.016, 0.027)
    rx = (0.0128, 0.0140, 0.0153, 0.0160, 0.0158, 0.0146, 0.0124)
    ry = (0.0265, 0.0315, 0.0380, 0.0420, 0.0430, 0.0420, 0.0390)
    sides = 16
    rings = []
    for i, z in enumerate(zs):
        w = blend_weights({fore_b: 1.0}, hw, 0.5) if i == 0 else hw
        rings.append([b.vert(W((PX + math.cos(a) * rx[i], math.sin(a) * ry[i], z)), w, REG_LEATHER, i / 6, k / sides)
                      for k, a in ((k, 2 * math.pi * k / sides) for k in range(sides))])
    b.grid(rings, [W((PX, 0, z)) for z in zs], closed=True)
    cap = [b.vert(W((PX + math.cos(a) * rx[-1] * 0.7, math.sin(a) * ry[-1] * 0.82, 0.034)), hw, REG_LEATHER, 1.0, k / sides)
           for k, a in ((k, 2 * math.pi * k / sides) for k in range(sides))]
    b.strip(rings[-1], cap, W((PX, 0, 0.02)), closed=True)
    b.fan(cap, b.vert(W((PX, 0, 0.037)), hw, REG_LEATHER, 1.0, 0.5), W((PX, 0, 0.02)))

    def palm_r(z):
        z = min(max(z, zs[0]), zs[-1])
        for i in range(len(zs) - 1):
            if zs[i] <= z <= zs[i + 1]:
                k = (z - zs[i]) / (zs[i + 1] - zs[i])
                return rx[i] + (rx[i + 1] - rx[i]) * k, ry[i] + (ry[i + 1] - ry[i]) * k
        return rx[-1], ry[-1]

    def palm_pt(z, ph, off):
        a, c = palm_r(z)
        return Vector((PX + math.cos(ph) * (a + off), math.sin(ph) * (c + off), z))

    # ---------------- back-of-hand lames (3, overlapping towards the wrist) + gold knuckle guard with studs
    for k, (z0, z1) in enumerate(((-0.074, -0.046), (-0.052, -0.022), (-0.028, 0.006))):
        b.seed = 0.3 + 0.1 * k
        off = 0.0042 - 0.0011 * k
        cols, rows = 7, 4
        phis = [math.radians(-60 + 120 * j / (cols - 1)) for j in range(cols)]

        def surf(i, j, z0=z0, z1=z1, off=off, phis=phis):
            z = z0 + (z1 - z0) * i / 3
            ph = phis[j]
            if i == 3:
                z += 0.005 * math.cos(ph * 1.5) ** 2  # pointed distal edge
            ridge = 0.0011 * max(0.0, 1.0 - abs(ph) / math.radians(24))
            return W(palm_pt(z, ph, off + ridge))

        def inner(i, j, z0=z0, z1=z1, phis=phis):
            return W(palm_pt(z0 + (z1 - z0) * i / 3, phis[j], -0.0018))

        shell(b, surf, inner, rows, cols, REG_METAL, lambda i: hw, lambda i, z0=z0, z1=z1: W((PX, 0, z0 + (z1 - z0) * i / 3)))
    b.seed = 0.75
    kphis = [math.radians(-72 + 144 * j / 10) for j in range(11)]
    shell(b, lambda i, j: W(palm_pt(0.004 + 0.008 * i, kphis[j], 0.0040 + 0.0012 * math.sin(math.pi * i / 3))),
          lambda i, j: W(palm_pt(0.004 + 0.008 * i, kphis[j], -0.0015)), 4, 11, REG_GOLD, lambda i: hw,
          lambda i: W((PX, 0, 0.004 + 0.008 * i)))
    for name, y, r, last in M.FINGERS:  # studs over each knuckle
        base = palm_pt(0.016, math.asin(max(-1.0, min(1.0, y / (palm_r(0.016)[1] + 0.004)))), 0.0050)
        out = Vector((1.0, 0.0, 0.25)).normalized()
        spike(b, W(base), W(base + out * 0.0055), 0.0032, 5, REG_METAL, hw, W(base + Vector((0, 1, 0))) - W(base))
    b.seed = 0.5

    # ---------------- fingers (glove + 3 lames) and thumb (glove + 2 lames)
    for name, y, r, last in M.FINGERS:
        p0, p1, p2, p3 = M.finger_joints(y, last)
        pts = catmull([p0, p1, p2, p3], 3)  # 10 stations, joints at 0, 3, 6, 9
        b1, b2 = "B_%s1_%s" % (name, s), "B_%s2_%s" % (name, s)

        def wf(i, n, b1=b1, b2=b2):
            if i == 0:
                return {hand_b: 0.55, b1: 0.45}
            if i < 3:
                return {b1: 1.0}
            if i == 3:
                return {b1: 0.5, b2: 0.5}
            return {b2: 1.0}

        def rf(i, n, r=r):
            k = (1.05, 1.0, 0.98, 1.04, 0.97, 0.95, 1.0, 0.93, 0.91, 0.88)[i]
            return r * k

        _finger(b, W, pts, rf, 10, wf, [(0, 2, False), (3, 5, False), (6, 9, True)])
    t0, t1, t2, t3 = (Vector(p) for p in M.THUMB_PATH)
    pts = catmull([t0, t1, t2, t3], 3)
    b1, b2 = "B_Thumb1_" + s, "B_Thumb2_" + s

    def wt(i, n):
        if i == 0:
            return {hand_b: 0.6, b1: 0.4}
        if i < 6:
            return {b1: 1.0}
        if i == 6:
            return {b1: 0.5, b2: 0.5}
        return {b2: 1.0}

    _finger(b, W, pts, lambda i, n: M.THUMB_RADIUS * (1.18, 1.08, 1.02, 1.0, 0.99, 0.98, 1.0, 0.95, 0.92, 0.9)[i], 10, wt,
            [(2, 5, False), (6, 9, True)])

    # ---------------- forearm frame (straight wrist -> elbow), dorsal direction = back of the hand
    wrist_u = C.frame_point(pos, rot, M.design_to_local(M.WRIST_LOCAL, side))
    sh = M.shoulder(side)
    elbow_u, pole_n, _ = M.solve_elbow(sh, wrist_u, side)
    fdir = (elbow_u - wrist_u).normalized()
    wrist_u = elbow_u - fdir * M.FORE_LEN
    dors = rot @ M.design_to_local((1, 0, 0), side)
    D = (dors - fdir * dors.dot(fdir)).normalized()
    E = fdir.cross(D).normalized()

    def FP(t, ph, r):
        return C.to_blender_point(wrist_u + fdir * (t * M.FORE_LEN) + (D * math.cos(ph) + E * math.sin(ph)) * r)

    # the vambrace is centred on the side the FPP camera (origin) sees, not on the back of the hand
    to_cam = -(wrist_u + fdir * (0.5 * M.FORE_LEN))
    V = (to_cam - fdir * to_cam.dot(fdir)).normalized()
    VE = fdir.cross(V).normalized()

    def FV(t, ph, r):
        return C.to_blender_point(wrist_u + fdir * (t * M.FORE_LEN) + (V * math.cos(ph) + VE * math.sin(ph)) * r)

    def FA(t):
        return C.to_blender_point(wrist_u + fdir * (t * M.FORE_LEN))

    def few(t):  # forearm weights
        if t < 0.08:
            return blend_weights(hw, {fore_b: 1.0}, min(1.0, max(0.0, 0.5 + t / 0.16)))
        if t > 0.9:
            return blend_weights({fore_b: 1.0}, {up_b: 1.0}, min(0.5, (t - 0.9) / 0.2))
        return {fore_b: 1.0}

    def rf_fore(t):
        pts = ((0.0, 0.029), (0.08, 0.031), (0.18, 0.034), (0.35, 0.036), (0.55, 0.0385), (0.75, 0.041), (0.92, 0.043), (1.0, 0.044))
        t = min(max(t, 0.0), 1.0)
        for (ta, ra), (tb, rb) in zip(pts, pts[1:]):
            if ta <= t <= tb:
                return ra + (rb - ra) * (t - ta) / (tb - ta)
        return pts[-1][1]

    b = ba  # glove/arms split
    # padded under-sleeve (quilted cloth) wrist -> elbow
    ts = [-0.02, 0.06, 0.16, 0.30, 0.45, 0.60, 0.75, 0.88, 1.0]
    sides = 14
    rings = [[b.vert(FP(t, -math.pi + 2 * math.pi * k / sides, rf_fore(t)), few(t), REG_CLOTH, i / 8, k / sides)
              for k in range(sides)] for i, t in enumerate(ts)]
    b.grid(rings, [FA(t) for t in ts], closed=True)

    b = bg  # glove/arms split
    # flared bell cuff with a flame-pointed rim, gold rim band and a rolled lip
    b.seed = 0.6
    cuff_t = [-0.035, -0.012, 0.025, 0.07, 0.115, 0.150, 0.170]
    cuff_r = [0.0318, 0.0340, 0.0388, 0.0445, 0.0498, 0.0535, 0.0552]
    cols = 24
    phis = [-math.pi + 2 * math.pi * j / cols for j in range(cols)]

    def flame(ph):
        c = max(0.0, math.cos(ph))
        return 0.030 * c ** 5 + 0.011 * max(0.0, math.cos(3 * ph)) ** 3 * (1.0 - c)

    def cuff_pt(i, j, dr=0.0):
        f = (0.0, 0.0, 0.0, 0.0, 0.1, 0.45, 1.0)[i]
        t = cuff_t[i] + f * flame(phis[j]) / M.FORE_LEN
        ridge = 0.0018 * max(0.0, 1.0 - abs(phis[j]) / math.radians(16)) if i >= 2 else 0.0
        return FP(t, phis[j], cuff_r[i] + ridge + dr)

    rows = [[b.vert(cuff_pt(i, j), few(cuff_t[i]), REG_METAL, i / 8, 0.5) for j in range(cols)] for i in range(5)]
    b.grid(rows, [FA(t) for t in cuff_t[:5]], closed=True)
    gold = [[b.vert(cuff_pt(i, j), few(cuff_t[i]), REG_GOLD, i / 8, 0.5) for j in range(cols)] for i in range(4, 7)]
    b.grid(gold, [FA(t) for t in cuff_t[4:7]], closed=True)
    # inner skin of the bell (visible from the camera through the opening) + rim wall, faces point inwards
    inner_rows = [[b.vert(cuff_pt(i, j, -0.0030), few(cuff_t[i]), REG_GOLD if i >= 5 else REG_METAL, i / 8, 0.5)
                   for j in range(cols)] for i in range(2, 7)]
    for ii in range(len(inner_rows) - 1):
        i = ii + 2
        for j in range(cols):
            j2 = (j + 1) % cols
            b.face((inner_rows[ii][j], inner_rows[ii][j2], inner_rows[ii + 1][j2], inner_rows[ii + 1][j]),
                   FP(cuff_t[i], phis[j] + math.pi / cols, cuff_r[i] * 2.5))
    for j in range(cols):
        j2 = (j + 1) % cols
        b.face((gold[-1][j], gold[-1][j2], inner_rows[-1][j2], inner_rows[-1][j]), cuff_pt(5, j) * 0.5 + cuff_pt(5, j2) * 0.5)
    # cuff gem (blue energy) in a gold bezel, on the back of the wrist
    b.seed = 0.9
    gem_c = FP(0.075, 0.0, 0.0447 + 0.0018)
    gem_n = (FP(0.075, 0.0, 1.0) - FA(0.075)).normalized()
    dome(b, gem_c - gem_n * 0.001, gem_n, 0.0105, 0.0035, 8, REG_GOLD, few(0.075), ring_frac=0.92)
    dome(b, gem_c + gem_n * 0.0012, gem_n, 0.0078, 0.0058, 8, REG_ENERGY, few(0.075))
    b.seed = 0.5

    b = ba  # glove/arms split
    # segmented vambrace: 3 overlapping lames with a central ridge, pointed elbow end
    for k, (ta, tb) in enumerate(((0.15, 0.40), (0.37, 0.63), (0.60, 0.85))):
        b.seed = 0.35 + 0.12 * k
        n_i, n_j = 5, 13
        vph = [math.radians(-142 + 284 * j / (n_j - 1)) for j in range(n_j)]

        def vt(i, ta=ta, tb=tb):
            return ta + (tb - ta) * i / (n_i - 1)

        def surf(i, j, k=k, vph=vph, vt=vt):
            ph = vph[j]
            t = vt(i)
            if k == 2 and i == n_i - 1:
                t += 0.0
            if k == 0 and i == 0:
                t -= 0.03 * max(0.0, math.cos(ph)) ** 3
            ridge = 0.0042 * max(0.0, 1.0 - abs(ph) / math.radians(21))
            swell = 0.0016 * math.sin(math.pi * i / (n_i - 1))
            return FV(t, ph, rf_fore(t) + 0.0058 + 0.0016 * k + ridge + swell)

        def inner(i, j, vph=vph, vt=vt):
            t = vt(i)
            return FV(t, vph[j], rf_fore(t) - 0.001)

        shell(b, surf, inner, n_i, n_j, REG_METAL, lambda i, vt=vt: few(vt(i)), lambda i, vt=vt: FA(vt(i)))
    # leather straps with gold buckles on the inner side
    for k, tc in enumerate((0.385, 0.615)):
        b.seed = 0.15 + 0.2 * k
        sph = [math.radians(128 + 104 * j / 8) for j in range(9)]
        shell(b, lambda i, j, tc=tc, sph=sph: FV(tc - 0.03 + 0.03 * i, sph[j], rf_fore(tc) + 0.0100),
              lambda i, j, tc=tc, sph=sph: FV(tc - 0.03 + 0.03 * i, sph[j], rf_fore(tc) - 0.001), 3, 9, REG_STRAP,
              lambda i, tc=tc: few(tc), lambda i, tc=tc: FA(tc - 0.03 + 0.03 * i))
        bc = FV(tc, math.pi, rf_fore(tc) + 0.0100)
        bn = (FV(tc, math.pi, 1.0) - FA(tc)).normalized()
        dome(b, bc, bn, 0.0085, 0.003, 4, REG_GOLD, few(tc), ring_frac=0.8)
    b.seed = 0.5

    # cloth sleeve: elbow puff -> upper arm (folds); mostly outside the phone frame
    sh_b = C.to_blender_point(sh)
    el_b = C.to_blender_point(elbow_u)
    up_dir = (sh - elbow_u).normalized()
    Du = (D - up_dir * D.dot(up_dir)).normalized()
    Eu = up_dir.cross(Du).normalized()
    sides = 16
    ring_defs = []
    for t, dr in ((0.80, 0.0118), (0.88, 0.0135), (0.96, 0.0145)):
        ring_defs.append((wrist_u + fdir * (t * M.FORE_LEN), D, E, rf_fore(t) + dr, few(t)))
    for u, rr in ((0.05, 0.054), (0.22, 0.052), (0.50, 0.050), (0.80, 0.051), (1.0, 0.052)):
        w = blend_weights({fore_b: 1.0}, {up_b: 1.0}, 0.5) if u < 0.1 else {up_b: 1.0}
        ring_defs.append((elbow_u + up_dir * (u * M.UPPER_LEN), Du, Eu, rr, w))
    rings = []
    for i, (c, d, e, rr, w) in enumerate(ring_defs):
        fold = 0.0 if i == 0 else (0.05 if i in (1, 2, 3) else 0.03)
        rings.append([b.vert(C.to_blender_point(c + (d * math.cos(a) + e * math.sin(a)) * rr * (1.0 + fold * math.sin(5 * a + i * 1.3))),
                             w, REG_CLOTH, i / (len(ring_defs) - 1), k / sides)
                      for k, a in ((k, -math.pi + 2 * math.pi * k / sides) for k in range(sides))])
    b.grid(rings, [C.to_blender_point(c) for c, *_ in ring_defs], closed=True)
    del sh_b, el_b


# ================================================================ swords
def _u2b(p):
    return C.S @ Vector(p)


class SwordSpec:
    def __init__(self, name, hilt_bottom, hilt_top, guard_y, blade_base, blade_tip, width_base, width_mid, thick_base, thick_tip,
                 ricasso, wing_x, wing_rise, pommel_y, pommel_scale, mid_ring=None, lugs=False):
        self.__dict__.update(locals())
        del self.__dict__["self"]


SWORD_1H = SwordSpec("SM_Weapon_FppSword", M.HILT_BOTTOM, M.HILT_TOP, M.GUARD_Y, C.MODEL_BLADE_BASE.y, C.MODEL_BLADE_TIP.y,
                     0.0335, 0.0285, 0.0062, 0.0034, 0.185, 0.118, 0.052, M.POMMEL_Y, 1.0)
# 2H greatsword: same hilt radius (the hands close on the same drawn radius), longer grip for both hands.
GS_HILT_BOTTOM, GS_HILT_TOP, GS_GUARD_Y, GS_POMMEL_Y = -0.236, 0.100, 0.122, -0.268
GS_BLADE_BASE, GS_BLADE_TIP = 0.14, 1.30
GS_LEFT_GRIP = -0.15
SWORD_2H = SwordSpec("SM_Weapon_FppGreatsword", GS_HILT_BOTTOM, GS_HILT_TOP, GS_GUARD_Y, GS_BLADE_BASE, GS_BLADE_TIP,
                     0.046, 0.038, 0.0080, 0.0042, 0.245, 0.165, 0.070, GS_POMMEL_Y, 1.18, mid_ring=(-0.080, -0.068), lugs=True)

GREATSWORD_SOCKETS = {
    "Socket_GripR": (0.0, 0.0, 0.0),
    "Socket_GripL": (0.0, GS_LEFT_GRIP, 0.0),
    "Socket_FxBladeBase": (0.0, GS_BLADE_BASE, 0.0),
    "Socket_FxBladeTip": (0.0, GS_BLADE_TIP, 0.0),
    "Socket_FxAuraBurst": (0.0, 0.50, 0.0),
    "Socket_FxGuardGem": (0.0, GS_GUARD_Y, 0.0),
}

# conservative collision proxies (sword-model units) used by the hand/sword penetration checks
PROXIES = {
    "SM_Weapon_FppSword": {"hilt": [M.HILT_BOTTOM - 0.010, M.HILT_TOP, 0.0165], "guard": [0.125, 0.096, 0.160, 0.024],
                           "blade": [0.13, 0.94, 0.0335, 0.0066], "pommel": [-0.236, -0.172, 0.026]},
    "SM_Weapon_FppGreatsword": {"hilt": [GS_HILT_BOTTOM - 0.010, GS_HILT_TOP, 0.0165], "guard": [0.172, 0.100, 0.196, 0.028],
                                "blade": [0.14, 1.30, 0.046, 0.0085], "pommel": [-0.320, -0.236, 0.030]},
}


def proxy_hit(weapon, lu):
    """Name of the proxy volume containing the sword-model-local Unity point lu, or None."""
    p = PROXIES[weapon]
    r = math.hypot(lu.x, lu.z)
    hb, ht, hr = p["hilt"]
    if hb <= lu.y <= ht and r < hr:
        return "hilt"
    gx, gy0, gy1, gz = p["guard"]
    if abs(lu.x) < gx and gy0 <= lu.y <= gy1 and abs(lu.z) < gz and (abs(lu.x) < 0.04 or lu.y < gy0 + 0.035 + 0.5 * abs(lu.x)):
        return "guard"
    by0, by1, bx, bz = p["blade"]
    if by0 <= lu.y <= by1 and abs(lu.x) < bx and abs(lu.z) < bz:
        return "blade"
    py0, py1, pr = p["pommel"]
    if py0 <= lu.y <= py1 and r < pr:
        return "pommel"
    return None


def _blade(b, sp):
    y0, y1 = sp.blade_base, sp.blade_tip
    L = y1 - y0
    tip_start = y1 - 0.15 * L

    def width(y):
        if y < sp.ricasso:
            return sp.width_base * 0.78
        if y < sp.ricasso + 0.012:
            return sp.width_base * (0.78 + 0.22 * (y - sp.ricasso) / 0.012)
        if y <= tip_start:
            k = (y - sp.ricasso) / (tip_start - sp.ricasso)
            return sp.width_base + (sp.width_mid - sp.width_base) * k + 0.0016 * math.sin(math.pi * k)
        k = (y - tip_start) / (y1 - tip_start)
        return sp.width_mid * max(0.0, 1.0 - k ** 1.7) ** 0.62

    def thick(y):
        k = min(1.0, max(0.0, (y - y0) / (0.95 * L)))
        return sp.thick_base + (sp.thick_tip - sp.thick_base) * k

    prof = [(1.0, 0.0), (0.72, 0.42), (0.42, 0.93), (0.20, 1.0), (0.0, 0.45), (-0.20, 1.0), (-0.42, 0.93), (-0.72, 0.42),
            (-1.0, 0.0), (-0.72, -0.42), (-0.42, -0.93), (-0.20, -1.0), (0.0, -0.45), (0.20, -1.0), (0.42, -0.93), (0.72, -0.42)]
    stations = [y0 - 0.03, y0, sp.ricasso - 0.004, sp.ricasso + 0.004, sp.ricasso + 0.012]
    n_main = 13
    for k in range(1, n_main + 1):
        stations.append(sp.ricasso + 0.012 + (tip_start - sp.ricasso - 0.012) * k / n_main)
    for k in (0.3, 0.55, 0.75, 0.9):
        stations.append(tip_start + (y1 - tip_start) * k)
    fuller_lo, fuller_hi = sp.ricasso + 0.03, y0 + 0.72 * L
    rings = []
    for y in stations:
        w, t = width(max(y, y0)), thick(max(y, y0))
        fuller = fuller_lo <= y <= fuller_hi
        ricasso = y < sp.ricasso + 0.004
        ring = []
        for k, (px, pz) in enumerate(prof):
            if abs(px) < 1e-6:
                pz = (0.45 if fuller else 1.0) * (1 if pz > 0 else -1)
            if abs(px) == 1.0 and ricasso:
                pz = 0.0
            if ricasso and abs(px) == 0.72:
                pz = 0.75 * (1 if pz > 0 else -1)
            reg = REG_FULLER if (fuller and abs(px) < 0.3) else REG_BLADE
            ring.append(b.vert(_u2b((px * w, y, pz * t)), {}, reg, (y - y0) / L, k / len(prof)))
        rings.append(ring)
    b.grid(rings, [_u2b((0, y, 0)) for y in stations], closed=True)
    tip = b.vert(_u2b((0, y1, 0)), {}, REG_BLADE, 1.0, 0.5)
    b.fan(rings[-1], tip, _u2b((0, stations[-1] - 0.02, 0)))
    if sp.lugs:  # flame-shaped parrying lugs at the end of the ricasso
        for sgn in (-1, 1):
            yb = sp.ricasso - 0.004
            spike(b, _u2b((sgn * width(yb) * 0.9, yb - 0.004, 0)), _u2b((sgn * (width(yb) + 0.030), yb - 0.020, 0)), 0.0060, 5,
                  REG_GUARD, {}, _u2b((0, 0, 1)))


def _guard(b, sp):
    g = sp.guard_y
    s2 = sp.wing_x / 0.118  # size factor vs the 1H guard
    # central block (gold, faceted octagon)
    ys = [g - 0.019, g - 0.014, g + 0.002, g + 0.018, g + 0.026]
    rr = [(0.018, 0.013), (0.029, 0.020), (0.033, 0.023), (0.029, 0.020), (0.019, 0.012)]
    rings = []
    for i, y in enumerate(ys):
        rings.append([b.vert(_u2b((math.cos(a) * rr[i][0] * (0.85 + 0.15 * s2), y, math.sin(a) * rr[i][1] * (0.85 + 0.15 * s2))),
                             {}, REG_SGOLD, i / 4, k / 8)
                      for k, a in ((k, math.pi / 8 + 2 * math.pi * k / 8) for k in range(8))])
    b.grid(rings, [_u2b((0, y, 0)) for y in ys], closed=True)
    for end, y in ((0, ys[0] - 0.004), (-1, ys[-1] + 0.006)):
        b.fan(rings[end], b.vert(_u2b((0, y, 0)), {}, REG_SGOLD, 0.5, 0.5), _u2b((0, g, 0)))
    # langets on both faces of the blade (pointed, with a ridge)
    for sgn in (-1, 1):
        yl0, yl1 = g + 0.018, g + 0.018 + 0.075 * s2
        wl = 0.0145 * (0.8 + 0.2 * s2)

        def surf(i, j, sgn=sgn, yl0=yl0, yl1=yl1, wl=wl):
            k = i / 4
            y = yl0 + (yl1 - yl0) * k
            x = wl * (1.0 - k ** 1.4) * (-1 + j / 2)
            z = sp.thick_base + 0.0024 + (0.0012 if j == 2 else 0.0)
            return _u2b((x, y, sgn * z))

        shell(b, surf, lambda i, j, sgn=sgn, yl0=yl0, yl1=yl1, wl=wl: _u2b((wl * (1 - (i / 4) ** 1.4) * (-1 + j / 2), yl0 + (yl1 - yl0) * i / 4,
                                                                             sgn * sp.thick_base * 0.4)),
              5, 5, REG_SGOLD, lambda i: {}, lambda i: _u2b((0, g + 0.03, 0)))
    # swept dragon-wing quillons with a scroll curl at the tip, diamond section (dark steel)
    for sgn in (-1, 1):
        ctrl = [(0.026, g + 0.000), (0.046, g + 0.001), (0.066, g + 0.006), (0.084, g + 0.015), (0.098, g + 0.027),
                (0.109, g + 0.040), (0.116, g + 0.051), (0.117, g + 0.059), (0.111, g + 0.063)]
        ctrl = [(x * s2, g + (y - g) * (sp.wing_rise / 0.052)) for x, y in ctrl]
        pts = [Vector((sgn * x, y, 0.0)) for x, y in ctrl]
        pts = catmull(pts, 2)
        n = len(pts)
        frames = frames_along(pts, (0.0, 0.0, 1.0))
        sides = 6
        rings = []
        for i, (p, t, nr, bn) in enumerate(frames):
            k = i / (n - 1)
            hz = (0.0150 - 0.0100 * k) * (0.85 + 0.15 * s2)
            hy = (0.0110 - 0.0072 * k) * (0.85 + 0.15 * s2)
            ring = []
            for j in range(sides):
                a = 2 * math.pi * j / sides
                off = nr * (math.cos(a) * hz) + bn * (math.sin(a) * hy)
                ring.append(b.vert(_u2b(p + off), {}, REG_GUARD, k, j / sides))
            rings.append(ring)
        b.grid(rings, [_u2b(f[0]) for f in frames], closed=True)
        p, t, *_ = frames[-1]
        b.fan(rings[-1], b.vert(_u2b(p + t * 0.006), {}, REG_GUARD, 1.0, 0.5), _u2b(p - t * 0.004))
        # flame spike under the wing and a whisker fin above the block
        spike(b, _u2b((sgn * 0.060 * s2, g - 0.002, 0)), _u2b((sgn * 0.086 * s2, g - 0.016, 0)), 0.0058, 5, REG_GUARD, {},
              _u2b((0, 0, 1)))
        spike(b, _u2b((sgn * 0.024 * s2, g + 0.016, 0)), _u2b((sgn * 0.046 * s2, g + 0.050 * s2, 0)), 0.0050, 5, REG_SGOLD, {},
              _u2b((0, 0, 1)))
    # gems (energy) in gold bezels on both faces of the block
    for sgn in (-1, 1):
        zc = 0.023 * (0.85 + 0.15 * s2)
        dome(b, _u2b((0, g, sgn * (zc - 0.0015))), _u2b((0, 0, sgn)), 0.0118, 0.0028, 8, REG_SGOLD, {}, ring_frac=0.9)
        dome(b, _u2b((0, g, sgn * zc)), _u2b((0, 0, sgn)), 0.0088, 0.0052, 8, REG_GEM, {})


def _hilt(b, sp):
    ys, rr = [], []
    n = 14
    for k in range(n + 1):
        y = sp.hilt_bottom + (sp.hilt_top - sp.hilt_bottom) * k / n
        ys.append(y)
        rr.append(0.0152 * (1.0 + 0.045 * math.sin(math.pi * k / n)))
    sides = 12
    rings = [[b.vert(_u2b((math.cos(a) * rr[i], y, math.sin(a) * rr[i])), {}, REG_GRIP, i / n, j / sides)
              for j, a in ((j, 2 * math.pi * j / sides) for j in range(sides))] for i, y in enumerate(ys)]
    b.grid(rings, [_u2b((0, y, 0)) for y in ys], closed=True)
    # gold ferrules (top under the guard, bottom above the pommel, optional middle ring between the hands)
    bands = [(sp.hilt_top - 0.012, sp.hilt_top + 0.002), (sp.hilt_bottom - 0.008, sp.hilt_bottom + 0.002)]
    if sp.mid_ring:
        bands.append(sp.mid_ring)
    for ya, yb in bands:
        prof = [(ya, 0.0150), (ya + 0.0015, 0.0172), (yb - 0.0015, 0.0172), (yb, 0.0150)]
        rings = [[b.vert(_u2b((math.cos(a) * r, y, math.sin(a) * r)), {}, REG_SGOLD, i / 3, j / 12)
                  for j, a in ((j, 2 * math.pi * j / 12 + math.pi / 12) for j in range(12))] for i, (y, r) in enumerate(prof)]
        b.grid(rings, [_u2b((0, y, 0)) for y, _ in prof], closed=True)


def _pommel(b, sp):
    k = sp.pommel_scale
    top = sp.hilt_bottom - 0.004
    prof = [(0.000, 0.0138), (-0.008, 0.0162), (-0.016, 0.0235), (-0.025, 0.0275), (-0.034, 0.0245), (-0.041, 0.0170),
            (-0.047, 0.0110), (-0.053, 0.0090), (-0.059, 0.0050)]
    sides = 16
    rings = []
    for i, (dy, r) in enumerate(prof):
        y = top + dy * k
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides
            petal = 1.0 + (0.13 * math.cos(4 * a) if 2 <= i <= 4 else 0.0)
            ring.append(b.vert(_u2b((math.cos(a) * r * k * petal, y, math.sin(a) * r * k * petal)), {}, REG_SGOLD, i / 8, j / sides))
        rings.append(ring)
    b.grid(rings, [_u2b((0, top + dy * k, 0)) for dy, _ in prof], closed=True)
    b.fan(rings[-1], b.vert(_u2b((0, top - 0.070 * k, 0)), {}, REG_SGOLD, 1.0, 0.5), _u2b((0, top - 0.05 * k, 0)))
    yc = top - 0.025 * k
    for sgn in (-1, 1):
        dome(b, _u2b((0, yc, sgn * 0.0300 * k)), _u2b((0, 0, sgn)), 0.0085 * k, 0.0045 * k, 8, REG_GEM, {})


def _sword(sp, sockets):
    b = Builder()
    _hilt(b, sp)
    _guard(b, sp)
    _blade(b, sp)
    _pommel(b, sp)
    obj = b.to_object(sp.name, sharp_angle=SHARP_SWORD)
    empties = []
    for name, p in sockets.items():
        e = bpy.data.objects.new(name, None)
        e.empty_display_type = "ARROWS"
        e.empty_display_size = 0.03
        e.location = _u2b(p)
        e.rotation_euler = (math.radians(90.0), 0.0, 0.0)  # C.NODE_FIX: Unity sees an identity frame
        e.parent = obj
        bpy.context.scene.collection.objects.link(e)
        empties.append(e)
    return obj, empties


def build_sword():
    return _sword(SWORD_1H, M.SWORD_SOCKETS)


def build_greatsword():
    return _sword(SWORD_2H, GREATSWORD_SOCKETS)
