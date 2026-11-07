"""METINAR-016 — Wandering Merchant: procedural clips Idle / Walk / TurnLeft / TurnRight / Talk / Interact (30 fps).

Idle/Walk/Talk/Interact stay in place. TurnLeft/TurnRight rotate the body 90 degrees about the vertical axis with two
steps (Unity extracts it as root rotation; the NPC integration decides whether to apply it).

Each pose is a function of the normalised loop phase; keys are set on every frame, so the FBX bake is exact.
Rotations are given about armature axes in the bone's rest frame (Z up, character faces -Y, its left is +X)
and mirrored for the right side. Legs use the IK helpers from merchant_model (baked into FK on export).
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector

import merchant_model as mm

FPS = 30
# state: (last frame, loop)
CLIPS = {"Idle": (91, True), "Walk": (37, True), "TurnLeft": (31, False), "TurnRight": (31, False),
         "Talk": (61, True), "Interact": (61, False)}
TURN_DEG = 90.0              # TurnLeft/TurnRight end rotated by this much (root rotation for the integration)
WALK_STEP_M = 0.44           # one step; a full cycle (2 steps) covers 0.88 m in 1.2 s
WALK_SPEED_MPS = 2 * WALK_STEP_M / ((CLIPS["Walk"][0] - 1) / FPS)

AX = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}


def _rest(pb):
    return pb.bone.matrix_local.to_3x3()


def rot(pb, *rots):
    """rots: (axis 'X'|'Y'|'Z', degrees) applied in order about armature axes through the bone head."""
    R = Matrix.Identity(3)
    for axis, deg in rots:
        R = Matrix.Rotation(math.radians(deg), 3, AX[axis]) @ R
    M = _rest(pb)
    pb.rotation_mode = "QUATERNION"
    pb.rotation_quaternion = (M.inverted() @ R @ M).to_quaternion()


def loc(pb, v):
    pb.location = _rest(pb).inverted() @ Vector(v)


def side_rot(rig, side, bone, *rots):
    """Rotation given for the LEFT side; Y and Z angles are mirrored for the right side."""
    s = 1 if side == "Left" else -1
    rot(rig.pose.bones[side + bone], *[(a, d if a == "X" else d * s) for a, d in rots])


def reset(rig):
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)


def _smooth(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


# ------------------------------------------------------------------------------------------------ poses
def arms_relaxed(rig, side, sway=0.0):
    side_rot(rig, side, "Shoulder", ("Y", 6))
    side_rot(rig, side, "UpperArm", ("Y", 74 + sway), ("X", -8))
    side_rot(rig, side, "LowerArm", ("Z", -22))
    side_rot(rig, side, "Hand", ("X", 8))


def arm_on_strap(rig, side, bob=0.0):
    """Hand gripping the backpack strap in front of the chest."""
    side_rot(rig, side, "Shoulder", ("Y", 4))
    side_rot(rig, side, "UpperArm", ("Y", 72 + bob), ("X", -12))
    side_rot(rig, side, "LowerArm", ("Z", -96 + bob * 0.5), ("Y", 6))
    side_rot(rig, side, "Hand", ("Y", -15), ("X", 20))


def pose_idle(rig, ph):
    p = rig.pose.bones
    a = 2 * math.pi * ph
    breath = math.sin(2 * a)
    loc(p["Hips"], (0.008 * math.sin(a), 0.0, -0.012 + 0.003 * breath))
    rot(p["Hips"], ("Y", 1.5 * math.sin(a)))
    rot(p["Spine"], ("X", 1.0 + 0.8 * breath), ("Y", -1.2 * math.sin(a)))
    rot(p["Chest"], ("X", 1.5 * breath), ("Z", 2.0 * math.sin(a)))
    rot(p["Neck"], ("X", 2.0))
    rot(p["Head"], ("Z", 9.0 * math.sin(a)), ("X", -2.0 + 2.5 * math.sin(2 * a + 0.6)), ("Y", 2.0 * math.sin(a + 1.0)))
    arm_on_strap(rig, "Left", bob=1.5 * breath)
    arms_relaxed(rig, "Right", sway=2.0 * math.sin(a + 0.4))
    side_rot(rig, "Right", "LowerArm", ("Z", -24 - 3 * math.sin(a)))
    rot(p["Backpack"], ("X", -0.6 * breath))
    rot(p["Lantern"], ("X", 3.0 * math.sin(a + 0.8)), ("Y", 2.0 * math.sin(a)))
    rot(p["Jaw"], ("X", 0.0))
    feet_planted(rig)


def feet_planted(rig):
    for s in ("L", "R"):
        loc(rig.pose.bones["IK_Foot_" + s], (0, 0, 0))
        rot(rig.pose.bones["IK_Foot_" + s], ("Z", 0))


def pose_talk(rig, ph):
    p = rig.pose.bones
    a = 2 * math.pi * ph
    loc(p["Hips"], (0.006 * math.sin(a), 0.0, -0.014 + 0.003 * math.sin(2 * a)))
    rot(p["Hips"], ("Y", 1.2 * math.sin(a)))
    rot(p["Spine"], ("X", 2.0), ("Z", -3.0 + 2.0 * math.sin(a)))
    rot(p["Chest"], ("X", 2.0 + 1.5 * math.sin(2 * a)), ("Z", -4.0 + 3.0 * math.sin(a + 0.5)))
    rot(p["Neck"], ("X", 3.0))
    rot(p["Head"], ("X", -4.0 + 6.0 * math.sin(2 * a + 0.3)), ("Z", 4.0 * math.sin(a)), ("Y", 4.0 * math.sin(a + 1.2)))
    # jaw: syllables (6 per loop) with an envelope, closed at the loop point
    syl = max(0.0, math.sin(6 * a)) * (0.6 + 0.4 * math.sin(a) ** 2)
    rot(p["Jaw"], ("X", 13.0 * syl))
    # right arm presents / explains (open palm, forearm up), left hand on the strap
    side_rot(rig, "Right", "Shoulder", ("Y", 2), ("Z", -4))
    side_rot(rig, "Right", "UpperArm", ("Y", 48 + 8 * math.sin(a)), ("X", -38 - 10 * math.sin(2 * a + 0.4)))
    side_rot(rig, "Right", "LowerArm", ("Z", -62 - 18 * math.sin(2 * a)), ("Y", -10))
    side_rot(rig, "Right", "Hand", ("X", -35 + 20 * math.sin(2 * a + 0.9)), ("Y", 10 * math.sin(a)))
    arm_on_strap(rig, "Left", bob=1.0 * math.sin(2 * a))
    rot(p["Backpack"], ("X", -0.5 * math.sin(2 * a)))
    rot(p["Lantern"], ("X", 2.5 * math.sin(a + 0.8)))
    feet_planted(rig)


def _foot_track(s):
    """Phase s in [0,1): heel strike at 0 (foot in front). Returns (dy, dz, pitch_deg); -dy is forward."""
    stance = 0.6
    half = WALK_STEP_M / 2
    if s < stance:
        t = s / stance
        dy = -half + 2 * half * t
        pitch = -12 * (1 - _smooth(t / 0.15)) + 18 * _smooth((t - 0.75) / 0.25)
        dz = 0.035 * _smooth((t - 0.75) / 0.25)
    else:
        t = (s - stance) / (1 - stance)
        e = 0.5 - 0.5 * math.cos(math.pi * t)
        dy = half - 2 * half * e
        dz = 0.035 * (1 - t) + 0.075 * math.sin(math.pi * t)
        pitch = 18 * (1 - _smooth(t / 0.4)) - 12 * _smooth((t - 0.6) / 0.4)
    return dy, dz, pitch


def pose_walk(rig, ph):
    p = rig.pose.bones
    a = 2 * math.pi * ph
    loc(p["Hips"], (0.018 * math.sin(a), -0.01, -0.045 - 0.012 * math.cos(2 * a)))
    rot(p["Hips"], ("Z", 5.0 * math.cos(a)), ("Y", 3.0 * math.sin(a)))
    rot(p["Spine"], ("X", 3.0), ("Z", -3.0 * math.cos(a)))
    rot(p["Chest"], ("X", 4.0 + 1.0 * math.cos(2 * a)), ("Z", -3.5 * math.cos(a)), ("Y", -2.0 * math.sin(a)))
    rot(p["Neck"], ("X", -2.0))
    rot(p["Head"], ("X", -3.0 - 1.5 * math.cos(2 * a)), ("Z", 1.5 * math.cos(a)))
    for s, sh in (("L", 0.0), ("R", 0.5)):
        dy, dz, pitch = _foot_track((ph + sh) % 1.0)
        loc(p["IK_Foot_" + s], (0, dy, dz))
        rot(p["IK_Foot_" + s], ("X", pitch))
    # right arm swings opposite to the right leg (forward when the left foot strikes), left hand holds the strap
    swing = math.cos(a)
    side_rot(rig, "Right", "Shoulder", ("Y", 6))
    side_rot(rig, "Right", "UpperArm", ("Y", 74), ("X", -8 - 18 * swing))
    side_rot(rig, "Right", "LowerArm", ("Z", -22 - 12 * max(0.0, swing)))
    side_rot(rig, "Right", "Hand", ("X", 8))
    arm_on_strap(rig, "Left", bob=1.5 * math.cos(2 * a))
    rot(p["Backpack"], ("X", -2.0 * math.cos(2 * a + 0.6)), ("Y", 1.5 * math.sin(a)))
    rot(p["Lantern"], ("X", 10.0 * math.sin(2 * a - 0.9)), ("Y", 5.0 * math.sin(a - 0.6)))
    rot(p["Jaw"], ("X", 0.0))


def _ease(t, t0, t1):
    return _smooth((t - t0) / (t1 - t0))


def _turn(rig, ph, sign):
    """sign +1 = TurnLeft (towards the character's left, +X when facing -Y), -1 = TurnRight."""
    p = rig.pose.bones
    pose_idle(rig, 0.0)
    ang = sign * TURN_DEG
    body = ang * _ease(ph, 0.08, 0.92)
    lead, trail = ("L", "R") if sign > 0 else ("R", "L")
    feet = {lead: (0.10, 0.50), trail: (0.48, 0.90)}
    for s, (t0, t1) in feet.items():
        e = _ease(ph, t0, t1)
        phi = ang * e
        lift = 0.07 * math.sin(math.pi * max(0.0, min(1.0, (ph - t0) / (t1 - t0))))
        rz = Matrix.Rotation(math.radians(phi), 3, "Z")
        for bone in ("IK_Foot_" + s, "Pole_Knee_" + s):
            p0 = Vector(mm.BONE_DATA_ALL[bone][2])
            d = rz @ p0 - p0
            loc(p[bone], (d.x, d.y, d.z + (lift if bone.startswith("IK") else 0.0)))
            rot(p[bone], ("Z", phi))
    dip = -0.02 * math.sin(math.pi * ph)
    loc(p["Hips"], (0.0, 0.0, -0.012 + dip))
    rot(p["Hips"], ("Z", body))
    lead_head = sign * 14 * math.sin(math.pi * min(1.0, ph * 1.4))
    rot(p["Head"], ("Z", lead_head), ("X", -2.0))
    rot(p["Chest"], ("Z", sign * 5 * math.sin(math.pi * ph)))
    rot(p["Lantern"], ("Y", -sign * 8 * math.sin(math.pi * ph)), ("X", 3 * math.sin(2 * math.pi * ph)))


def pose_turn_left(rig, ph):
    _turn(rig, ph, +1)


def pose_turn_right(rig, ph):
    _turn(rig, ph, -1)


def _lerp(a, b, t):
    return a + (b - a) * t


def pose_interact(rig, ph):
    """Offers an item with the right hand (reach forward, palm up, hold, return), slight bow."""
    p = rig.pose.bones
    pose_idle(rig, 0.25 * ph)
    e = _ease(ph, 0.05, 0.38) * (1 - _ease(ph, 0.66, 0.97))
    rot(p["Spine"], ("X", 1.0 + 6 * e))
    rot(p["Chest"], ("X", 1.0 + 6 * e))
    rot(p["Head"], ("X", 10 * e), ("Z", -4 * e))
    side_rot(rig, "Right", "Shoulder", ("Y", _lerp(6, 0, e)), ("Z", _lerp(0, -8, e)))
    side_rot(rig, "Right", "UpperArm", ("Y", _lerp(74, 58, e)), ("X", _lerp(-8, -52, e)))
    side_rot(rig, "Right", "LowerArm", ("Z", _lerp(-24, -36, e)), ("Y", _lerp(0, -10, e)))
    side_rot(rig, "Right", "Hand", ("X", _lerp(8, -70, e)))


POSERS = {"Idle": pose_idle, "Talk": pose_talk, "Walk": pose_walk, "TurnLeft": pose_turn_left,
          "TurnRight": pose_turn_right, "Interact": pose_interact}


def build_actions(rig):
    sc = bpy.context.scene
    sc.render.fps = FPS
    rig.animation_data_create()
    bpy.context.view_layer.objects.active = rig
    actions = {}
    for state, (last, _loop) in CLIPS.items():
        act = bpy.data.actions.new("A_WanderingMerchant_" + state)
        act.use_fake_user = True
        rig.animation_data.action = act
        for f in range(1, last + 1):
            reset(rig)
            POSERS[state](rig, (f - 1) / (last - 1))
            for pb in rig.pose.bones:
                pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
                pb.keyframe_insert("location", frame=f, group=pb.name)
        for fc in act.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR"
        actions[state] = act
    rig.animation_data.action = None
    reset(rig)
    return actions


def min_deformed_z(mesh, rig, action, last, step=2):
    sc = bpy.context.scene
    rig.animation_data.action = action
    lowest = 1e9
    for f in range(1, last + 1, step):
        sc.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        lowest = min(lowest, min((mw @ v.co).z for v in me.vertices))
        ev.to_mesh_clear()
    rig.animation_data.action = None
    return lowest
