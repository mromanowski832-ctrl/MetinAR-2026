"""METINAR-046 (#91) — combo / 2H / Aura-2H clip definitions (Unity camera space, metres, 100 fps).

Every clip starts and ends exactly on the stance of its weapon (REST for 1H = contract V3 rest, REST_2H for the
greatsword), so the combo is a cross-fade between clips and every clip "returns to stance" on its own.
Key poses are (time, grip position, blade direction, wrist->knuckle direction); the grip frame is built from the
blade (local +Y) and the hand direction, which by default continues the forearm (shoulder -> grip). Positions and both direction vectors are interpolated with a cubic
Hermite through the keys (Catmull-Rom tangents in time, zero velocity at the first/last key), so the sword
travels on arcs (no linear snapping) and the rotation follows the arc.
Timings follow the Developer's WarriorComboConfig (feat/082-warrior-combo, #82): 1H 0.72/0.72/0.80 s,
2H 1.00/1.00/1.15 s, hit windows as configured there.
The V3 clips Idle / Attack / AuraActivate are NOT defined here (they keep following fpp_contract exactly).
"""
import math

from mathutils import Matrix, Quaternion, Vector

import fpp_contract as C

GS_SCALE = C.SWORD_SCALE           # the greatsword is drawn at the same 0.75 scale around the grip
GS_LEFT_GRIP_MODEL = -0.15         # fpp_gear.GS_LEFT_GRIP (model units), left hand 112.5 mm below the right
GS_LEFT_GRIP = Vector((0.0, GS_LEFT_GRIP_MODEL * GS_SCALE, 0.0))


SQ = 1.0 / math.sqrt(2.0)
# grip-local hand directions (fpp_model.design_to_local with HAND_YAW -45 deg): wrist->knuckle and back of the hand
HAND_DIR_LOCAL = {1: Vector((-SQ, 0.0, SQ)), -1: Vector((SQ, 0.0, SQ))}
HAND_BACK_LOCAL = Vector((-SQ, 0.0, SQ))  # left hand: back of the hand (grip-local)
# approximate shoulders used ONLY to orient the hands of the key poses (the real shoulders are solved later)
KEY_SHOULDER = {1: Vector((0.16, -0.30, 0.02)), -1: Vector((-0.18, -0.22, -0.05))}


def frame_from(blade, hand):
    """Right-hand grip frame from the blade direction (+Y) and the wrist->knuckle direction of the right hand."""
    y = Vector(blade).normalized()
    h = Vector(hand)
    h = (h - y * h.dot(y)).normalized()
    back = y.cross(h)
    x = (back - h) * SQ
    z = (back + h) * SQ
    return Matrix((x, y, z)).transposed().to_quaternion()


def hand_dir(q, side=1):
    return q @ HAND_DIR_LOCAL[side]


def auto_hand(pos, blade, side=1, cock=0.0):
    """Wrist->knuckle direction continuing the forearm (shoulder -> grip), optionally cocked about the back axis."""
    y = Vector(blade).normalized()
    d = Vector(pos) - KEY_SHOULDER[side]
    h = (d - y * d.dot(y)).normalized()
    if cock:
        h = Quaternion(y.cross(h), math.radians(cock)) @ h
    return h


REST_Q = C.u_euler(*C.REST_EULER)
REST_BLADE, REST_HAND = REST_Q @ Vector((0, 1, 0)), hand_dir(REST_Q)


def key(t, pos, blade, left=(0.0, 0.0, 0.0), hand=None):
    """hand=None: the edge (grip-local +X, blade width axis) is aligned with the blade's travel at this key
    (see _resolve_edges), i.e. the sword cuts with an edge, never with the flat."""
    blade = Vector(blade).normalized()
    return [t, Vector(pos), blade, Vector(hand).normalized() if hand is not None else None, Vector(left)]


TIP_ARM = 0.60  # m from the grip, point used for the travel direction


def frame_from_edge(blade, edge):
    y = Vector(blade).normalized()
    x = Vector(edge)
    x = (x - y * x.dot(y)).normalized()
    return Matrix((x, y, x.cross(y).normalized())).transposed().to_quaternion()


def _resolve_edges(keys):
    """Fill hand directions of auto keys: edge along the tip travel (towards the next key for the first strike key,
    centred difference inside the strike, from the previous key at the last one); of +-edge the one whose hand
    direction best continues the forearm is used (double-edged blade)."""
    tips = [k[1] + k[2] * TIP_ARM for k in keys]
    for i, k in enumerate(keys):
        if k[3] is not None:
            continue
        prev_auto = i > 0 and keys[i - 1][3] is None
        if not prev_auto:
            v = tips[i + 1] - tips[i]
        elif i + 1 < len(keys) and keys[i + 1][3] is None:
            v = tips[i + 1] - tips[i - 1]
        else:
            v = tips[i] - tips[i - 1]
        want = auto_hand(k[1], k[2])
        best = None
        for sgn in (1.0, -1.0):
            q = frame_from_edge(k[2], v * sgn)
            h = hand_dir(q)
            score = h.dot(want)
            if best is None or score > best[0]:
                best = (score, h)
        k[3] = best[1]
    return [tuple(k) for k in keys]


def rot_world(axis, deg, v):
    return Quaternion(Vector(axis).normalized(), math.radians(deg)) @ Vector(v)


# 2H stance: sword a little more upright, both hands on the long grip, still in the right half of the view
REST_2H_POS = Vector((0.10, -0.17, 0.45))
REST_2H_BLADE = rot_world((0, 0, 1), 0.0, REST_BLADE)
REST_2H_HAND = rot_world((0, 0, 1), 0.0, REST_HAND)


def _r1(t):
    return key(t, C.REST_POSITION, REST_BLADE, hand=REST_HAND)


def _r2(t):
    return key(t, REST_2H_POS, REST_2H_BLADE, hand=REST_2H_HAND)


L_DOWN = (-0.06, -0.07, -0.05)   # buckler hand pulled down/left/back out of the cut (1H)
L_HALF = (-0.03, -0.035, -0.025)
L_TUCK = (-0.05, -0.10, -0.10)   # buckler tucked low/back under a cut that finishes on the left

# ---------------------------------------------------------------- buckler (K6 / #59 / PR #63)
# 1H always comes with the starter buckler (Lipowy Puklerz, PR #63 SM_Weapon_StarterBuckler, referenced, not modified).
# B_Socket_ShieldL is a child of B_Hand_L; its frame = the #63 pivot convention (centre of the fist grip, handle along
# local +Y, front along local +Z). In the hand it is the LeftHand frame turned about the grip axis so the front faces
# the knuckles (a punch-block pushes the boss straight along the fist). PF_Weapon_StarterBuckler goes under the socket
# with an identity local transform.
SHIELD_REL = Vector((0.0, 0.0, 1.0)).rotation_difference(HAND_DIR_LOCAL[-1])
SHIELD_REL_INV = SHIELD_REL.inverted()


def shield_frame(front, handle):
    """Socket (buckler) frame from its front (+Z) and handle (+Y) directions (camera space)."""
    z = Vector(front).normalized()
    y = Vector(handle)
    y = (y - z * y.dot(z)).normalized()
    x = y.cross(z).normalized()
    return Matrix((x, y, z)).transposed().to_quaternion()


def hand_from_shield(q_shield):
    return q_shield @ SHIELD_REL_INV


# 1H + buckler stance (left hand; the right hand stays exactly on the V3 REST): buckler low at the left edge,
# its boss angled forward-left so only the rim and part of the back show in the lower-left corner; centre clear.
SHIELD_REST_POS = Vector((-0.235, -0.285, 0.400))
SHIELD_REST_FRONT = Vector((-0.30, 0.10, 0.95)).normalized()
SHIELD_REST_HANDLE = Vector((0.20, 0.98, 0.0)).normalized()
# BLOCK (ShieldBlock*): buckler raised in front of the face-left, boss to the front, right hand pulled back and low
SHIELD_BLOCK_POS = Vector((-0.085, -0.135, 0.355))
SHIELD_BLOCK_FRONT = Vector((0.12, 0.12, 0.985)).normalized()
SHIELD_BLOCK_HANDLE = Vector((0.30, 0.95, 0.0)).normalized()
BLOCK_R_POS = C.REST_POSITION + Vector((0.065, -0.070, -0.060))
BLOCK_R_BLADE = rot_world((1, 0, 0), -15.0, rot_world((0, 0, 1), -28.0, REST_BLADE))
BLOCK_R_HAND = rot_world((1, 0, 0), -15.0, rot_world((0, 0, 1), -28.0, REST_HAND))


def lkey(t, pos, front, handle):
    return (t, Vector(pos), Vector(front).normalized(), Vector(handle).normalized())


def _ls(t, off=(0.0, 0.0, 0.0)):
    return lkey(t, SHIELD_REST_POS + Vector(off), SHIELD_REST_FRONT, SHIELD_REST_HANDLE)


def _lb(t, off=(0.0, 0.0, 0.0)):
    return lkey(t, SHIELD_BLOCK_POS + Vector(off), SHIELD_BLOCK_FRONT, SHIELD_BLOCK_HANDLE)

# ---------------------------------------------------------------- clip table (camera space: +X right, +Y up, +Z forward)
# key(t, grip position, blade direction, left-hand offset from LEFT_REST [1H only])
CLIPS = {
    "Attack1H_01": dict(weapon="1H", seconds=0.72, hit=(0.18, 0.42), combo=(0.40, 0.66), keys=[  # L -> R (backhand)
        _r1(0.00),
        key(0.15, (-0.070, -0.070, 0.400), (-0.92, 0.30, 0.25), L_DOWN),
        key(0.22, (-0.040, -0.070, 0.460), (-0.70, 0.60, 0.40), L_DOWN),
        key(0.30, (0.040, -0.070, 0.510), (-0.05, 0.80, 0.60), L_DOWN),
        key(0.38, (0.120, -0.100, 0.500), (0.70, 0.55, 0.45), L_HALF),
        key(0.46, (0.180, -0.170, 0.450), (0.95, 0.05, 0.30)),
        key(0.56, (0.180, -0.190, 0.440), (0.92, -0.15, 0.30)),
        _r1(0.72)]),
    "Attack1H_02": dict(weapon="1H", seconds=0.72, hit=(0.18, 0.42), combo=(0.40, 0.66), keys=[  # R -> L (forehand)
        _r1(0.00),
        key(0.15, (0.200, -0.060, 0.410), (0.92, 0.30, 0.25)),
        key(0.22, (0.170, -0.070, 0.470), (0.70, 0.60, 0.40)),
        key(0.30, (0.090, -0.070, 0.520), (0.05, 0.80, 0.60), L_HALF),
        key(0.38, (0.010, -0.100, 0.510), (-0.70, 0.55, 0.45), L_DOWN),
        key(0.46, (-0.050, -0.170, 0.460), (-0.95, 0.05, 0.30), L_TUCK),
        key(0.56, (-0.050, -0.190, 0.450), (-0.92, -0.15, 0.30), L_TUCK),
        _r1(0.72)]),
    "Attack1H_03": dict(weapon="1H", seconds=0.80, hit=(0.22, 0.48), combo=None, keys=[  # finisher: overhead diagonal
        _r1(0.00),
        key(0.18, (0.130, 0.000, 0.380), (0.35, 0.75, -0.55)),
        key(0.24, (0.130, 0.020, 0.380), (0.30, 0.70, -0.65)),
        key(0.32, (0.100, -0.040, 0.470), (0.25, 0.95, 0.15), L_HALF),
        key(0.40, (0.060, -0.100, 0.520), (-0.15, 0.70, 0.70), L_DOWN),
        key(0.48, (0.010, -0.180, 0.510), (-0.75, -0.25, 0.60), L_DOWN),
        key(0.58, (0.000, -0.210, 0.490), (-0.75, -0.40, 0.50), L_DOWN),
        _r1(0.80)]),
    # ---- 2H: both hands on the grip; heavier = longer, a held wind-up, wider arcs, a held follow-through
    "Attack2H_01": dict(weapon="2H", seconds=1.00, hit=(0.32, 0.62), combo=(0.60, 0.92), keys=[  # L -> R
        _r2(0.00),
        key(0.22, (-0.060, -0.050, 0.380), (-0.90, 0.35, 0.20)),
        key(0.32, (-0.070, -0.040, 0.380), (-0.93, 0.30, 0.12)),
        key(0.42, (-0.040, -0.060, 0.450), (-0.70, 0.60, 0.40)),
        key(0.50, (0.040, -0.070, 0.510), (-0.05, 0.80, 0.60)),
        key(0.58, (0.120, -0.100, 0.500), (0.70, 0.55, 0.45)),
        key(0.68, (0.180, -0.170, 0.450), (0.95, 0.05, 0.30)),
        key(0.78, (0.180, -0.190, 0.440), (0.92, -0.20, 0.30)),
        _r2(1.00)]),
    "Attack2H_02": dict(weapon="2H", seconds=1.00, hit=(0.32, 0.62), combo=(0.60, 0.92), keys=[  # R -> L
        _r2(0.00),
        key(0.22, (0.200, -0.050, 0.390), (0.90, 0.35, 0.20)),
        key(0.32, (0.210, -0.040, 0.390), (0.93, 0.30, 0.12)),
        key(0.42, (0.180, -0.060, 0.460), (0.70, 0.60, 0.40)),
        key(0.50, (0.100, -0.070, 0.520), (0.05, 0.80, 0.60)),
        key(0.58, (0.020, -0.100, 0.510), (-0.70, 0.55, 0.45)),
        key(0.68, (-0.040, -0.170, 0.460), (-0.95, 0.05, 0.30)),
        key(0.78, (-0.040, -0.190, 0.450), (-0.92, -0.20, 0.30)),
        _r2(1.00)]),
    "Attack2H_03": dict(weapon="2H", seconds=1.15, hit=(0.38, 0.72), combo=None, keys=[  # finisher: overhead vertical
        _r2(0.00),
        key(0.28, (0.080, 0.030, 0.370), (0.15, 0.85, -0.50)),
        key(0.38, (0.080, 0.050, 0.360), (0.10, 0.82, -0.56)),
        key(0.48, (0.075, -0.010, 0.500), (0.05, 0.95, 0.30)),
        key(0.58, (0.060, -0.100, 0.530), (-0.05, 0.70, 0.70)),
        key(0.66, (0.050, -0.180, 0.520), (-0.25, 0.00, 0.97)),
        key(0.74, (0.050, -0.250, 0.480), (-0.20, -0.70, 0.65)),
        key(0.86, (0.050, -0.260, 0.470), (-0.20, -0.70, 0.65)),
        _r2(1.15)]),
    # ---- Aura with the greatsword (#93: steps 2-3 = a re-grip only, both hands already on the hilt)
    "AuraActivate2H": dict(weapon="2H", seconds=C.AURA_CAST_SECONDS, hit=None, combo=None, keys=[
        _r2(0.00),
        key(0.25, (0.10, -0.12, 0.50), REST_2H_BLADE, hand=REST_2H_HAND),
        key(0.45, (0.10, -0.125, 0.495), REST_2H_BLADE, hand=REST_2H_HAND),
        key(0.65, (0.10, -0.11, 0.515), REST_2H_BLADE, hand=REST_2H_HAND),
        key(0.90, (0.10, -0.12, 0.50), REST_2H_BLADE, hand=REST_2H_HAND),
        _r2(1.15)]),
}

def _rb(t, off=(0.0, 0.0, 0.0)):
    return key(t, BLOCK_R_POS + Vector(off), BLOCK_R_BLADE, hand=BLOCK_R_HAND)


# ---- buckler-only clips (1H). ShieldBlock = raise (ends on the held block), ShieldBlockHold = loop, ShieldBlockEnd = back
CLIPS.update({
    "ShieldBlock": dict(weapon="1H", seconds=0.20, hit=None, combo=None, keys=[_r1(0.00), _rb(0.10), _rb(0.20)],
                        left_keys=[_ls(0.00), _lb(0.12, (0.0, 0.006, 0.004)), _lb(0.20)]),
    "ShieldBlockHold": dict(weapon="1H", seconds=1.00, hit=None, combo=None, loop=True,
                            keys=[_rb(0.00), _rb(0.50, (0.0, -0.003, 0.0)), _rb(1.00)],
                            left_keys=[_lb(0.00), _lb(0.50, (0.002, -0.004, 0.002)), _lb(1.00)]),
    "ShieldBlockEnd": dict(weapon="1H", seconds=0.30, hit=None, combo=None, keys=[_rb(0.00), _r1(0.30)],
                           left_keys=[_lb(0.00), _ls(0.30)]),
})
for _d in CLIPS.values():
    _d["keys"] = _resolve_edges(_d["keys"])
    if _d["weapon"] == "1H" and "left_keys" not in _d:  # combos: buckler stance + the per-key offset
        _d["left_keys"] = [_ls(k[0], k[4]) for k in _d["keys"]]
SHIELD_IDLE = "ShieldIdle"
AURA_1H = "AuraActivate1H"       # Aura with the 1H sword while the buckler is equipped (K6 rule 5)
LOOP_STATES = {"Idle2H", SHIELD_IDLE, "ShieldBlockHold"}
IDLE_2H = "Idle2H"
IDLE_2H_SECONDS = C.IDLE_SECONDS

AURA_EVENTS = [("AuraRaise", 0.00), ("AuraReach", 0.05), ("AuraGrip", 0.35), ("AuraStabilize", 0.40), ("AuraChargeStart", 0.45),
               ("AuraSpread", 0.55), ("AuraFlash", C.AURA_BURST_SECONDS), ("AuraSustain", 0.75), ("AuraReturn", 0.90),
               ("Recovered", C.AURA_CAST_SECONDS)]


BLOCK_EVENTS = {"ShieldBlock": [("BlockRaise", 0.0), ("BlockActive", 0.12), ("BlockHold", 0.20)],
                "ShieldBlockHold": [("BlockHold", 0.0)],
                "ShieldBlockEnd": [("BlockRelease", 0.0), ("Recovered", 0.30)]}


def events(state):
    """[(event id, seconds)] for every clip (OnFppAnimEvent(string id))."""
    if state in ("AuraActivate", "AuraActivate2H", AURA_1H):
        return list(AURA_EVENTS)
    if state in BLOCK_EVENTS:
        return list(BLOCK_EVENTS[state])
    if state == "Attack":
        return [("Windup", 0.0), ("HitStart", C.HIT_START_SECONDS), ("HitEnd", C.HIT_END_SECONDS), ("Recovered", C.ATTACK_SECONDS)]
    if state in CLIPS:
        d = CLIPS[state]
        out = [("Windup", 0.0)]
        if d["hit"]:
            out += [("HitStart", d["hit"][0]), ("HitEnd", d["hit"][1])]
        if d["combo"]:
            out += [("ComboWindowOpen", d["combo"][0]), ("ComboWindowClose", d["combo"][1])]
        out.append(("Recovered", d["seconds"]))
        return sorted(out, key=lambda e: e[1])
    if state in ("Idle", IDLE_2H, SHIELD_IDLE):
        return [("Breath", 0.0)]
    return []


def weapon_of(state):
    if state in CLIPS:
        return CLIPS[state]["weapon"]
    return "2H" if state == IDLE_2H else "1H"


def has_buckler(state):
    """K6: every 1H state of #91 holds the buckler (the V3 Idle/Attack/AuraActivate stay buckler-free); 2H never."""
    return state in all_states() and weapon_of(state) == "1H"


def is_loop(state):
    return state in LOOP_STATES


# ---------------------------------------------------------------- sampling
def _hermite(keys, t, idx):
    ts = [k[0] for k in keys]
    vals = [k[idx] for k in keys]
    n = len(keys)
    t = min(max(t, ts[0]), ts[-1])
    i = max(0, min(n - 2, max(j for j in range(n - 1) if ts[j] <= t)))

    def tangent(j):
        if j == 0 or j == n - 1:
            return vals[j] * 0.0
        a, b = vals[j - 1], vals[j + 1]
        # hold keys (equal neighbours) stay still; otherwise Catmull-Rom in time
        return (b - a) / (ts[j + 1] - ts[j - 1])

    h = ts[i + 1] - ts[i]
    s = (t - ts[i]) / h
    s2, s3 = s * s, s * s * s
    h00, h10, h01, h11 = 2 * s3 - 3 * s2 + 1, s3 - 2 * s2 + s, -2 * s3 + 3 * s2, s3 - s2
    return vals[i] * h00 + tangent(i) * (h10 * h) + vals[i + 1] * h01 + tangent(i + 1) * (h11 * h)


def sword_pose(state, t):
    keys = CLIPS[state]["keys"]
    p = _hermite(keys, t, 1)
    q = frame_from(_hermite(keys, t, 2), _hermite(keys, t, 3))
    return p, q


def idle2h_pose(t):
    ph = 2.0 * math.pi * t / IDLE_2H_SECONDS
    p = REST_2H_POS + Vector((0.0, 0.0035 * math.sin(ph), -0.0012 * math.sin(ph)))
    q = frame_from(REST_2H_BLADE, REST_2H_HAND) @ C.u_euler(0.9 * math.sin(ph), 0.0, 0.0)
    return p, q


def breath(t):
    return math.sin(2.0 * math.pi * t / C.IDLE_SECONDS)


def shield_idle_right(t):
    ph = breath(t)
    return C.REST_POSITION + Vector((0.0, 0.0035 * ph, -0.001 * ph)), REST_Q @ C.u_euler(1.0 * ph, 0.0, 0.0)


def right_pose(state, t):
    if state == IDLE_2H:
        return idle2h_pose(t)
    if state == SHIELD_IDLE:
        return shield_idle_right(t)
    if state == AURA_1H:
        return C.sword_pose("Aura", t)  # V3 Aura sword path, unchanged
    return sword_pose(state, t)


def shield_stance():
    """(position, socket quaternion) of the buckler in the 1H stance."""
    return SHIELD_REST_POS.copy(), shield_frame(SHIELD_REST_FRONT, SHIELD_REST_HANDLE)


def left_shield_pose(state, t):
    """LeftHand frame of a buckler clip (the socket = hand frame @ SHIELD_REL)."""
    if state == SHIELD_IDLE:
        ph = breath(t)
        p, qs = shield_stance()
        return p + Vector((0.0, 0.003 * ph, 0.0)), hand_from_shield(qs @ C.u_euler(0.8 * ph, 0.0, 0.0))
    keys = CLIPS[state]["left_keys"]
    p = _hermite(keys, t, 1)
    return p, hand_from_shield(shield_frame(_hermite(keys, t, 2), _hermite(keys, t, 3)))


def aura1h_left(t):
    """K6 rule 5: from the buckler stance onto the V3 left grip of the 1H hilt and back (V3 blend weights)."""
    p, r = C.sword_pose("Aura", t)
    g = C.left_grip_blend("Aura", t)
    sp, sq = shield_stance()
    return sp.lerp(p + r @ C.LEFT_GRIP_OFFSET, g), C.slerp(hand_from_shield(sq), r, g)


def shield_hang(state, t):
    """0 = buckler in the fist (socket rest), 1 = buckler parked on the outer left forearm (Aura 1H two-hand grip)."""
    if state != AURA_1H:
        return 0.0
    if t < 0.90:
        return min(1.0, max(0.0, (t - 0.08) / 0.20))   # 0.08-0.28 s (eased per path segment)
    return 1.0 - min(1.0, max(0.0, (t - 0.90) / 0.20))  # 0.90-1.10 s


def left_grip_2h(p, q):
    """Left hand on the greatsword grip: GS_LEFT_GRIP below the right hand, turned about the hilt axis so the
    left wrist points back towards the left shoulder (no locked mirror of the right hand)."""
    lp = p + q @ GS_LEFT_GRIP
    y = q @ Vector((0, 1, 0))
    d = lp - KEY_SHOULDER[-1]
    target = (d - y * d.dot(y)).normalized()
    cur = q @ HAND_DIR_LOCAL[-1]
    cur = (cur - y * cur.dot(y)).normalized()
    ang = math.atan2(cur.cross(target).dot(y), cur.dot(target))
    return lp, Quaternion(y, ang) @ q


def left_pose(state, t):
    if weapon_of(state) == "2H":
        p, q = right_pose(state, t)
        return left_grip_2h(p, q)
    if state == AURA_1H:
        return aura1h_left(t)
    return left_shield_pose(state, t)


def stance(weapon):
    if weapon == "2H":
        return REST_2H_POS.copy(), frame_from(REST_2H_BLADE, REST_2H_HAND)
    return C.REST_POSITION.copy(), REST_Q.copy()


def all_states():
    return list(CLIPS) + [IDLE_2H, SHIELD_IDLE, AURA_1H]


def seconds(state):
    if state in (IDLE_2H, SHIELD_IDLE):
        return C.IDLE_SECONDS
    if state == AURA_1H:
        return C.AURA_CAST_SECONDS
    return CLIPS[state]["seconds"]
