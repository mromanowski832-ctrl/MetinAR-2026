"""METINAR-FPP-ART-001 — technical contract shared with #31 / PR #32 (FppHandsView, FppCombatTimeline).

Every value here is copied from PR #32 head 52a1375 (technical prototype values, not final balance) and the
pose maths is a line-by-line port of FppHandsView.SwordPose / LeftGripBlend, so the baked clips put the
sword exactly where the code-driven hit geometry expects it.

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

# FppHandsView (PR #32)
REST_POSITION = Vector((0.18, -0.19, 0.46))
REST_EULER = (15.0, 0.0, -14.0)
LEFT_REST = Vector((-0.19, -0.25, 0.43))
LEFT_REST_EULER = (0.0, 0.0, 12.0)
BLADE_BASE = Vector((0.0, 0.13, 0.0))
BLADE_TIP = Vector((0.0, 0.94, 0.0))
LEFT_GRIP_OFFSET = Vector((0.0, -0.12, 0.0))  # left hand on the hilt, in the RightGrip frame
AURA_BURST_CENTRE = Vector((0.0, 0.38, 0.0))
ATTACK_WINDUP = (Vector((0.25, -0.08, 0.48)), (0.0, -10.0, -65.0))
ATTACK_END = (Vector((-0.15, -0.22, 0.50)), (70.0, 30.0, 60.0))
AURA_POSE = (Vector((0.02, -0.18, 0.52)), (12.0, 0.0, 0.0))

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
    """Port of FppHandsView.SwordPose: RightGrip pose (Unity camera-local position, quaternion)."""
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


def frame_point(pos, quat, local):
    """Unity-space point of `local` (Unity coordinates in the frame)."""
    return Vector(pos) + quat @ Vector(local)
