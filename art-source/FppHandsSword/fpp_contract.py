"""METINAR-FPP-ART-001 — technical contract shared with #31 / PR #32 (FppHandsView, FppCombatTimeline).

Contract V3 "v3-a31-retest" (#31 issuecomment-6314043034, A31 owner retest; PR #32 head 8d0acee FppViewFraming,
integration/fpp-art-preview FppPoseContract.RetestV3). Technical prototype values, not final balance. The pose maths
is a line-by-line port of FppPoseContract.SwordPose / LeftGripBlend / LeftHandPose, so the baked clips put the sword
exactly where the code-driven hit geometry expects it. Superseded: V1 (PR #32 52a1375, clips up to cd3d02c) and the
earlier proposal rest (0.06, -0.15, 0.44).

Sword scale: the sword MODEL (SM_Weapon_FppSword, its sockets and mesh) is authored at scale 1 in MODEL_* units;
PF_Player_FppArms nests it under B_Socket_Sword with a uniform localScale SWORD_SCALE around the grip (hands stay
life-size). Contract points in the RightGrip frame (hit blade, left grip, aura burst) are MODEL_* x SWORD_SCALE.

Spaces
- "Unity" = Unity camera-local space of the AR camera: metres, +X right, +Y up, +Z forward (left-handed).
  Quaternions/Euler use Unity's numeric convention: Quaternion.Euler(x, y, z) = Qy * Qx * Qz.
- "Blender" = authoring space: x = Unity x, y = Unity z, z = Unity y (camera looks along Blender +Y, up +Z).
  Unity's FBX import with Bake Axis Conversion maps Blender +Y to +Z and Z to +Y (verified in METINAR-013),
  so no extra rotation is applied on export.
"""
import math

from mathutils import Matrix, Quaternion, Vector

FPS = 100  # 0.72 s and 1.15 s are whole frames only at multiples of 100 fps

# FppCombatTimeline (PR #32)
ATTACK_SECONDS = 0.72
HIT_START_SECONDS = 0.18
HIT_END_SECONDS = 0.42
AURA_CAST_SECONDS = 1.15
AURA_BURST_SECONDS = 0.65
AURA_DURATION_SECONDS = 6.0
IDLE_SECONDS = 2.0  # asset-only loop (the code has no idle motion); starts/ends exactly on the rest pose

CONTRACT_ID = "v3-a31-retest"  # FppPoseContract.RetestV3.Id / FppArtPreviewConfig.BakedContractId

# FppViewFraming (PR #32 8d0acee) / FppPoseContract.RetestV3
SWORD_SCALE = 0.75  # uniform scale of hilt, guard, pommel, blade (and aura ribbons) around the grip
REST_POSITION = Vector((0.09, -0.17, 0.46))
REST_EULER = (15.0, -30.0, -22.0)
LEFT_REST = Vector((-0.08, -0.23, 0.42))
LEFT_REST_EULER = (0.0, 0.0, 12.0)  # unchanged from V1
AURA_POSE = (Vector((0.10, -0.14, 0.50)), (10.0, -30.0, -18.0))  # AuraActivate hold
# shared by every contract (hit window 0.18-0.42 s blends only windup -> end)
ATTACK_WINDUP = (Vector((0.25, -0.08, 0.48)), (0.0, -10.0, -65.0))
ATTACK_END = (Vector((-0.15, -0.22, 0.50)), (70.0, 30.0, 60.0))

# unscaled sword-model points (RightGrip frame at scale 1; the sword FBX sockets carry these)
MODEL_BLADE_BASE = Vector((0.0, 0.13, 0.0))
MODEL_BLADE_TIP = Vector((0.0, 0.94, 0.0))
MODEL_LEFT_GRIP_OFFSET = Vector((0.0, -0.12, 0.0))
MODEL_AURA_BURST_CENTRE = Vector((0.0, 0.38, 0.0))
MODEL_GUARD_CENTRE = Vector((0.0, 0.116, 0.0))

# contract points in the (unscaled) RightGrip frame = model x SWORD_SCALE
BLADE_BASE = MODEL_BLADE_BASE * SWORD_SCALE          # hit segment base (FppPoseContract.HitBladeBase)
BLADE_TIP = MODEL_BLADE_TIP * SWORD_SCALE            # hit segment tip (HitBladeTip)
LEFT_GRIP_OFFSET = MODEL_LEFT_GRIP_OFFSET * SWORD_SCALE  # left hand on the scaled hilt (LeftGrip)
AURA_BURST_CENTRE = MODEL_AURA_BURST_CENTRE * SWORD_SCALE
GUARD_CENTRE = MODEL_GUARD_CENTRE * SWORD_SCALE      # GripGuardCentre

# V1 (PR #32 52a1375) for reference only; not used by the build
V1_REST = (Vector((0.18, -0.19, 0.46)), (15.0, 0.0, -14.0))
V1_LEFT_REST = (Vector((-0.19, -0.25, 0.43)), (0.0, 0.0, 12.0))
V1_AURA_POSE = (Vector((0.02, -0.18, 0.52)), (12.0, 0.0, 0.0))

S = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))  # Unity <-> Blender (its own inverse)


def u_euler(x, y, z):
    qx = Quaternion((1, 0, 0), math.radians(x))
    qy = Quaternion((0, 1, 0), math.radians(y))
    qz = Quaternion((0, 0, 1), math.radians(z))
    return qy @ qx @ qz


def ease(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)  # Mathf.SmoothStep(0, 1, t)


def slerp(a, b, t):
    if a.dot(b) < 0.0:
        b = -b
    return a.slerp(b, t)


def blend(pa, ra, pb, rb, t):
    return pa.lerp(pb, t), slerp(ra, rb, t)


def sword_pose(action, time):
    """Port of FppPoseContract.SwordPose: RightGrip pose (Unity camera-local position, quaternion)."""
    rest = (REST_POSITION.copy(), u_euler(*REST_EULER))
    if action == "Attack":
        wp, wr = ATTACK_WINDUP[0], u_euler(*ATTACK_WINDUP[1])
        ep, er = ATTACK_END[0], u_euler(*ATTACK_END[1])
        if time < HIT_START_SECONDS:
            return blend(rest[0], rest[1], wp, wr, ease(time / HIT_START_SECONDS))
        if time <= HIT_END_SECONDS:
            return blend(wp, wr, ep, er, ease((time - HIT_START_SECONDS) / (HIT_END_SECONDS - HIT_START_SECONDS)))
        return blend(ep, er, rest[0], rest[1], ease((time - HIT_END_SECONDS) / (ATTACK_SECONDS - HIT_END_SECONDS)))
    if action == "Aura":
        b = ease(time / 0.25) if time < 0.9 else 1.0 - ease((time - 0.9) / 0.25)
        return blend(rest[0], rest[1], AURA_POSE[0], u_euler(*AURA_POSE[1]), b)
    return rest


def left_grip_blend(action, time):
    """Port of FppHandsView.LeftGripBlend."""
    if action != "Aura":
        return 0.0
    return ease(time / 0.35) if time < 0.9 else 1.0 - ease((time - 0.9) / 0.25)


def left_hand_pose(action, time):
    """Port of FppHandsView.Render for the LeftHand transform."""
    p, r = sword_pose(action, time)
    g = left_grip_blend(action, time)
    target = p + r @ LEFT_GRIP_OFFSET
    return LEFT_REST.lerp(target, g), slerp(u_euler(*LEFT_REST_EULER), r, g)


# ---------------------------------------------------------------- conversions
def to_blender_point(p):
    return S @ Vector(p)


def to_blender_matrix(pos, quat):
    """4x4 Blender-space matrix of a Unity frame (position + rotation)."""
    m = S @ quat.to_matrix() @ S
    out = m.to_4x4()
    out.translation = S @ Vector(pos)
    return out


# Unity's FBX import maps a node's local axes as vectors through S and then flips the node's local Z to fix the
# handedness, i.e. Unity frame = S * R_blender * diag(1,1,-1). Measured in Unity 6000.6.4f1 (FppArtAssetCheck: an
# identity Blender empty/bone came in as Euler(90,0,0)). Nodes that must equal a contract frame in Unity (sockets)
# therefore carry NODE_FIX = S * diag(1,1,-1) = +90 deg about Blender X on top of to_blender_matrix(frame).
NODE_FIX = Matrix.Rotation(math.radians(90.0), 4, "X")
NODE_FIX_INV = NODE_FIX.inverted()

# sword model -> RightGrip frame (uniform, so it commutes with the axis maps)
SWORD_SCALE_MATRIX = Matrix.Scale(SWORD_SCALE, 4)


def to_blender_node_matrix(pos, quat):
    """Blender matrix for a node (bone/empty) whose imported Unity transform must equal the Unity frame."""
    return to_blender_matrix(pos, quat) @ NODE_FIX


def frame_point(pos, quat, local):
    """Unity-space point of `local` (Unity coordinates in the frame)."""
    return Vector(pos) + quat @ Vector(local)
