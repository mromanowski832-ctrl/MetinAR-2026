"""METINAR-016 — procedural clips for the Wandering Merchant (30 fps, in place, Unity humanoid friendly).

Poses are written as rotations about ARMATURE axes per bone (world delta of a bone = parent delta * own delta),
converted to bone-local quaternions. Every frame the Hips are lifted/lowered so the lowest sole point touches Z = 0.
Clips: Idle, Walk, TurnLeft, TurnRight, Talk, Interact (required by the METINAR-009 NPC rules) + Gesture (optional).
TurnLeft/TurnRight carry a 90 deg yaw on the Hips (humanoid root rotation): with applyRootMotion off only the
stepping shows and gameplay rotates the transform.
"""
import math

import bpy
from mathutils import Euler, Quaternion, Vector

import merchant_model as mm

FPS = 30
# state: (last frame, loop)  -> length = (last - 1) / FPS seconds
CLIPS = {
    "Idle": (73, True),
    "Walk": (31, True),
    "TurnLeft": (31, False),
    "TurnRight": (31, False),
    "Talk": (61, True),
    "Interact": (43, False),
    "Gesture": (49, False),
}

# sole points (armature rest space) per foot bone: heel, ball, toe tip
def _sole_points():
    pts = []
    for side in ("Left", "Right"):
        x = mm.bone_head(side + "Foot").x
        pts += [(side + "Foot", Vector((x, -0.075, 0.0))), (side + "Foot", Vector((x, 0.06, 0.0))),
                (side + "Toes", Vector((x * 1.03, 0.19, 0.005)))]
    return pts


SOLE = _sole_points()


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def env(t, a, b, c, d):
    """0 before a, ramps to 1 at b, holds, ramps back to 0 between c and d."""
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / (b - a))
    if t <= c:
        return 1.0
    return 1.0 - smooth((t - c) / (d - c))


def relaxed():
    """Arms from the A-pose down to the sides, slight elbow bend, hands relaxed."""
    return {
        "LeftUpperArm": (6.0, -44.0, 4.0), "RightUpperArm": (6.0, 44.0, -4.0),
        "LeftLowerArm": (14.0, 0.0, 0.0), "RightLowerArm": (14.0, 0.0, 0.0),
        "LeftHand": (0.0, -6.0, 0.0), "RightHand": (0.0, 6.0, 0.0),
        "LeftShoulder": (0.0, -3.0, 0.0), "RightShoulder": (0.0, 3.0, 0.0),
        "LeftLowerLeg": (-3.0, 0.0, 0.0), "RightLowerLeg": (-3.0, 0.0, 0.0),
        "LeftUpperLeg": (1.5, 0.0, 0.0), "RightUpperLeg": (1.5, 0.0, 0.0),
    }


def add(pose, bone, rot):
    a = pose.get(bone, (0.0, 0.0, 0.0))
    pose[bone] = (a[0] + rot[0], a[1] + rot[1], a[2] + rot[2])


def pose_idle(t, T):
    p = relaxed()
    w = 2 * math.pi * t / T
    add(p, "Chest", (1.4 * math.sin(w), 0, 0))
    add(p, "Spine", (0.6 * math.sin(w), 0, 0))
    add(p, "Head", (1.5 * math.sin(2 * w + 0.6), 0, 7.0 * math.sin(w)))
    add(p, "Neck", (0, 0, 2.0 * math.sin(w)))
    add(p, "Hips", (0, 1.2 * math.sin(w), 0))
    add(p, "LeftUpperArm", (1.5 * math.sin(w + 0.4), 0, 0))
    add(p, "RightUpperArm", (1.5 * math.sin(w + 0.9), 0, 0))
    add(p, "LeftLowerArm", (3.0 * math.sin(w + 0.4), 0, 0))
    add(p, "RightLowerArm", (3.0 * math.sin(w + 0.9), 0, 0))
    return p, 0.0


def pose_walk(t, T):
    p = relaxed()
    ph = 2 * math.pi * t / T
    for side, sgn in (("Left", 1.0), ("Right", -1.0)):
        s = math.sin(ph) * sgn
        c = math.cos(ph) * sgn
        thigh = 24.0 * s
        knee = -(6.0 + 42.0 * max(0.0, c) ** 1.4)
        foot = -(thigh + knee) * (0.85 if c <= 0 else 0.6) + (8.0 * max(0.0, c))
        add(p, side + "UpperLeg", (thigh, 0, 0))
        add(p, side + "LowerLeg", (knee, 0, 0))
        add(p, side + "Foot", (foot, 0, 0))
        add(p, side + "UpperArm", (-16.0 * s, 0, 0))
        add(p, side + "LowerArm", (6.0 + 8.0 * max(0.0, -s), 0, 0))
    add(p, "Hips", (0, 2.5 * math.sin(ph), -5.0 * math.sin(ph)))
    add(p, "Spine", (-3.0, 0, 3.0 * math.sin(ph)))
    add(p, "Chest", (-1.0 + 1.0 * math.sin(2 * ph), 0, 3.5 * math.sin(ph)))
    add(p, "Head", (2.0, 0, -3.0 * math.sin(ph)))
    return p, 0.0


def pose_turn(t, T, sgn):
    """sgn +1 = TurnLeft (counter-clockwise from above), -1 = TurnRight. Two quick steps, inner foot first."""
    p = relaxed()
    u = t / T
    yaw = 90.0 * sgn * smooth((u - 0.08) / 0.8)
    first, second = ("Left", "Right") if sgn > 0 else ("Right", "Left")
    for side, (a, b) in ((first, (0.08, 0.46)), (second, (0.42, 0.82))):
        k = math.sin(math.pi * max(0.0, min(1.0, (u - a) / (b - a)))) if a < u < b else 0.0
        add(p, side + "UpperLeg", (22.0 * k, 0, 0))
        add(p, side + "LowerLeg", (-40.0 * k, 0, 0))
        add(p, side + "Foot", (14.0 * k, 0, 0))
    add(p, "Spine", (0, 0, 6.0 * sgn * math.sin(math.pi * u)))
    add(p, "Head", (0, 0, 10.0 * sgn * math.sin(math.pi * min(1.0, u * 1.3))))
    add(p, "LeftUpperArm", (5.0 * math.sin(math.pi * u), 0, 0))
    add(p, "RightUpperArm", (-5.0 * math.sin(math.pi * u), 0, 0))
    return p, yaw


def pose_talk(t, T):
    p = relaxed()
    w = 2 * math.pi * t / T
    # right forearm raised in front of the chest, hand moving while explaining
    add(p, "RightUpperArm", (24.0 + 6.0 * math.sin(2 * w), -12.0, 8.0))
    add(p, "RightLowerArm", (62.0 + 12.0 * math.sin(2 * w + 0.8), 0, -10.0 * math.sin(w)))
    add(p, "RightHand", (10.0 * math.sin(3 * w), 0, 12.0 * math.sin(2 * w)))
    # left hand resting on the belt
    add(p, "LeftUpperArm", (8.0, 10.0, -6.0))
    add(p, "LeftLowerArm", (38.0, 0, 18.0))
    add(p, "Head", (4.0 * math.sin(2 * w), 0, 5.0 * math.sin(w)))
    add(p, "Neck", (1.5 * math.sin(2 * w + 0.5), 0, 0))
    add(p, "Chest", (1.2 * math.sin(w), 0, 2.5 * math.sin(w)))
    return p, 0.0


def pose_interact(t, T):
    """Reach forward with the right hand (hand over / take an item) and return."""
    p = relaxed()
    e = env(t / T, 0.05, 0.40, 0.62, 0.97)
    add(p, "RightUpperArm", (62.0 * e, -26.0 * e, 6.0 * e))
    add(p, "RightLowerArm", (8.0 * e, 0, 0))
    add(p, "RightHand", (-10.0 * e, 0, 0))
    add(p, "Spine", (-6.0 * e, 0, 4.0 * e))
    add(p, "Chest", (-5.0 * e, 0, 3.0 * e))
    add(p, "Head", (6.0 * e, 0, 0))
    add(p, "RightUpperLeg", (4.0 * e, 0, 0))
    return p, 0.0


def pose_gesture(t, T):
    """Open-armed 'look at my wares' presentation."""
    p = relaxed()
    e = env(t / T, 0.04, 0.30, 0.66, 0.97)
    add(p, "LeftUpperArm", (30.0 * e, 30.0 * e, -18.0 * e))
    add(p, "RightUpperArm", (30.0 * e, -30.0 * e, 18.0 * e))
    add(p, "LeftLowerArm", (26.0 * e, 0, -10.0 * e))
    add(p, "RightLowerArm", (26.0 * e, 0, 10.0 * e))
    add(p, "LeftHand", (0, -18.0 * e, 0))
    add(p, "RightHand", (0, 18.0 * e, 0))
    add(p, "Chest", (4.0 * e, 0, 0))
    add(p, "Head", (6.0 * e * math.sin(math.pi * t / T), 0, 0))
    return p, 0.0


POSERS = {
    "Idle": pose_idle,
    "Walk": pose_walk,
    "TurnLeft": lambda t, T: pose_turn(t, T, 1.0),
    "TurnRight": lambda t, T: pose_turn(t, T, -1.0),
    "Talk": pose_talk,
    "Interact": pose_interact,
    "Gesture": pose_gesture,
}


def _apply(rig, pose, yaw, hips_offset):
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        q_rest = pb.bone.matrix_local.to_quaternion()
        rx, ry, rz = pose.get(pb.name, (0.0, 0.0, 0.0))
        r = Euler((math.radians(rx), math.radians(ry), math.radians(rz)), "YXZ").to_quaternion()
        if pb.name == "Hips" and yaw:
            r = Quaternion((0, 0, 1), math.radians(yaw)) @ r
        pb.rotation_quaternion = q_rest.inverted() @ r @ q_rest
        pb.location = (0, 0, 0)
    hips = rig.pose.bones["Hips"]
    hips.location = hips.bone.matrix_local.to_quaternion().inverted() @ hips_offset


def _lowest_sole(rig):
    bpy.context.view_layer.update()
    low = 1e9
    for bone, p in SOLE:
        pb = rig.pose.bones[bone]
        q = pb.matrix @ pb.bone.matrix_local.inverted() @ p
        low = min(low, q.z)
    return low


def build_actions(rig):
    if rig.animation_data is None:
        rig.animation_data_create()
    actions = {}
    for state, (last, loop) in CLIPS.items():
        act = bpy.data.actions.new("A_WanderingMerchant_" + state)
        act.use_fake_user = True
        rig.animation_data.action = act
        T = (last - 1) / FPS
        prev = {}
        for f in range(1, last + 1):
            t = (f - 1) / FPS
            pose, yaw = POSERS[state](t, T)
            _apply(rig, pose, yaw, Vector((0, 0, 0)))
            dz = -_lowest_sole(rig)
            _apply(rig, pose, yaw, Vector((0, 0, dz)))
            for pb in rig.pose.bones:
                q = pb.rotation_quaternion.copy()
                if pb.name in prev and prev[pb.name].dot(q) < 0.0:
                    q.negate()  # keep the shortest path between keys
                    pb.rotation_quaternion = q
                prev[pb.name] = q
                pb.keyframe_insert("rotation_quaternion", frame=f)
                if pb.name == "Hips":
                    pb.keyframe_insert("location", frame=f)
        actions[state] = act
    rig.animation_data.action = None
    return actions


def min_deformed_z(mesh, rig, action, last, step=2):
    rig.animation_data.action = action
    low = 1e9
    for f in range(1, last + 1, step):
        bpy.context.scene.frame_set(f)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = mesh.evaluated_get(dg)
        m = ev.to_mesh()
        mw = mesh.matrix_world
        low = min(low, min((mw @ v.co).z for v in m.vertices))
        ev.to_mesh_clear()
    rig.animation_data.action = None
    return low
