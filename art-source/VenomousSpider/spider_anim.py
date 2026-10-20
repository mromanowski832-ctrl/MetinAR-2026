"""METINAR-013 — Venomous Spider animation clips (30 fps, in place, keyed on Blender control rig).

Body/abdomen/head/fang/coxa bones are keyed directly; legs are driven by IK foot targets + poles
(Blender-only control bones). The FBX exporter samples the evaluated pose, so the exported clips contain
baked deform-bone animation only.
"""
import math

import bpy
from mathutils import Euler, Quaternion, Vector

import spider_model as sm

FPS = 30
CLIPS = {  # state: (last frame, loop)
    "Idle": (73, True),
    "Walk": (31, True),
    "Attack": (37, False),
    "Hit": (13, False),
    "Death": (61, False),
}
ATTACK_HIT_FRAME = 18  # frame where the fang strike lands (animation-event candidate)

LEG_IDS = [("%s%02d" % (side, i + 1), side, sx, i) for i in range(4) for side, sx in sm.SIDES]
GROUP_A = {"L01", "R02", "L03", "R04"}


def keyed_bones(rig):
    names = ["B_Body", "B_Abdomen", "B_Head", "B_Fang_L", "B_Fang_R"]
    names += [b.name for b in rig.data.bones if b.name.endswith("_Coxa")]
    names += [b.name for b in rig.data.bones if b.name.startswith("IK_Foot_")]
    return names


def rot(x=0.0, y=0.0, z=0.0):
    """Armature-space rotation delta in degrees. x > 0 = front (-Y) up, y = roll about the length axis, z = yaw."""
    return Euler((-math.radians(x), math.radians(y), math.radians(z)), "XYZ").to_quaternion()


def apply_pose(rig, frame, pose):
    """pose: bone -> dict(rot=Quaternion, loc=Vector, scale=float). Unlisted keyed bones go to rest."""
    for name in keyed_bones(rig):
        pb = rig.pose.bones[name]
        rest = rig.data.bones[name].matrix_local.to_3x3()
        inv = rest.inverted()
        spec = pose.get(name, {})
        q = spec.get("rot", Quaternion())
        loc = spec.get("loc", Vector((0, 0, 0)))
        s = spec.get("scale", 1.0)
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = (inv @ q.to_matrix() @ rest).to_quaternion()
        pb.location = inv @ Vector(loc)
        pb.scale = (s, s, s)
        for path in ("rotation_quaternion", "location", "scale"):
            pb.keyframe_insert(path, frame=frame, group=name)


def foot(leg_id, dx=0.0, dy=0.0, dz=0.0):
    return {"IK_Foot_" + leg_id: {"loc": Vector((dx, dy, dz))}}


def merge(*dicts):
    out = {}
    for d in dicts:
        out.update(d)
    return out


def idle_pose(f):
    t = (f - 1) / 72.0
    breath = math.sin(2 * math.pi * t)
    pose = {
        "B_Body": {"loc": Vector((0, 0, 0.008 * breath)), "rot": rot(x=-1.2 * breath)},
        "B_Abdomen": {"scale": 1.0 + 0.022 * (0.5 + 0.5 * breath), "rot": rot(x=2.0 * breath, z=1.5 * math.sin(4 * math.pi * t))},
        "B_Head": {"rot": rot(z=3.0 * math.sin(2 * math.pi * t + 1.0))},
        "B_Fang_L": {"rot": rot(y=-4.0 * max(0.0, math.sin(6 * math.pi * t)))},
        "B_Fang_R": {"rot": rot(y=4.0 * max(0.0, math.sin(6 * math.pi * t)))},
    }
    # front legs tap once each per loop
    for leg_id, start in (("L01", 18), ("R01", 48)):
        if start <= f <= start + 10:
            k = math.sin(math.pi * (f - start) / 10.0)
            pose.update(foot(leg_id, dy=-0.02 * k, dz=0.05 * k))
    return pose


def walk_pose(f):
    stride, lift = 0.16, 0.07
    p0 = (f - 1) / 30.0
    pose = {
        "B_Body": {"loc": Vector((0, 0, 0.010 * math.cos(4 * math.pi * p0))), "rot": rot(y=1.5 * math.sin(2 * math.pi * p0))},
        "B_Abdomen": {"rot": rot(z=2.5 * math.sin(2 * math.pi * p0), x=1.5 * math.cos(4 * math.pi * p0))},
    }
    for leg_id, side, sx, i in LEG_IDS:
        p = (p0 + (0.0 if leg_id in GROUP_A else 0.5)) % 1.0
        if p < 0.5:  # stance: foot slides back (in place)
            dy = -stride / 2 + stride * (p / 0.5)
            dz = 0.0
        else:  # swing: lift and bring forward
            q = (p - 0.5) / 0.5
            dy = stride / 2 - stride * (0.5 - 0.5 * math.cos(math.pi * q))
            dz = lift * math.sin(math.pi * q)
        pose.update(foot(leg_id, dy=dy, dz=dz))
    return pose


def attack_keys():
    rest = {}
    windup = merge(
        {"B_Body": {"rot": rot(x=24), "loc": Vector((0, 0.05, 0.06))},
         "B_Abdomen": {"rot": rot(x=-14)},
         "B_Head": {"rot": rot(x=8)},
         "B_Fang_L": {"rot": rot(y=-22)}, "B_Fang_R": {"rot": rot(y=22)}},
        foot("L01", dx=-0.06, dy=-0.02, dz=0.40), foot("R01", dx=0.06, dy=-0.02, dz=0.40),
        foot("L02", dy=0.03, dz=0.06), foot("R02", dy=0.03, dz=0.06),
    )
    strike = merge(
        {"B_Body": {"rot": rot(x=-12), "loc": Vector((0, -0.13, -0.035))},
         "B_Abdomen": {"rot": rot(x=8)},
         "B_Head": {"rot": rot(x=-10)},
         "B_Fang_L": {"rot": rot(y=10)}, "B_Fang_R": {"rot": rot(y=-10)}},
        foot("L01", dx=0.02, dy=-0.20, dz=0.0), foot("R01", dx=-0.02, dy=-0.20, dz=0.0),
        foot("L02", dy=-0.06), foot("R02", dy=-0.06),
    )
    hold = merge(
        {"B_Body": {"rot": rot(x=-6), "loc": Vector((0, -0.08, -0.02))},
         "B_Fang_L": {"rot": rot(y=4)}, "B_Fang_R": {"rot": rot(y=-4)}},
        foot("L01", dy=-0.16), foot("R01", dy=-0.16), foot("L02", dy=-0.04), foot("R02", dy=-0.04),
    )
    return [(1, rest), (13, windup), (16, windup), (ATTACK_HIT_FRAME, strike), (24, hold), (37, rest)]


def hit_keys():
    jolt = merge(
        {"B_Body": {"rot": rot(x=11, y=-4), "loc": Vector((0, 0.065, 0.02))},
         "B_Abdomen": {"rot": rot(z=9, x=-6)},
         "B_Head": {"rot": rot(x=8)},
         "B_Fang_L": {"rot": rot(y=-15)}, "B_Fang_R": {"rot": rot(y=15)}},
        foot("L01", dz=0.08, dy=0.02), foot("R01", dz=0.08, dy=0.02),
    )
    settle = {"B_Body": {"rot": rot(x=-3), "loc": Vector((0, 0.02, -0.01))}}
    return [(1, {}), (4, jolt), (8, settle), (13, {})]


def death_keys(rig):
    stagger = merge(
        {"B_Body": {"rot": rot(x=12, y=8), "loc": Vector((0, 0.06, 0.03))},
         "B_Abdomen": {"rot": rot(z=10)},
         "B_Fang_L": {"rot": rot(y=-20)}, "B_Fang_R": {"rot": rot(y=20)}},
        foot("L01", dz=0.12), foot("R01", dz=0.10),
    )
    curl = {}
    collapse = {"B_Body": {"rot": rot(x=-2, y=7), "loc": Vector((0, 0.02, -0.13))},
                "B_Abdomen": {"rot": rot(x=-6, z=-6)},
                "B_Head": {"rot": rot(x=20)},
                "B_Fang_L": {"rot": rot(x=-50, y=6)}, "B_Fang_R": {"rot": rot(x=-50, y=-6)}}
    for leg_id, side, sx, i in LEG_IDS:
        rest_foot = rig.data.bones["IK_Foot_" + leg_id].head_local
        target = Vector((rest_foot.x * 0.45, rest_foot.y * 0.55 + 0.02, 0.13 + 0.03 * (i % 2)))
        d = target - rest_foot
        curl.update(foot(leg_id, dx=d.x, dy=d.y, dz=d.z))
    final = merge(collapse, curl)
    half = merge({"B_Body": {"rot": rot(x=2, y=6), "loc": Vector((0, 0.04, -0.07))},
                  "B_Head": {"rot": rot(x=10)},
                  "B_Fang_L": {"rot": rot(x=-30, y=-6)}, "B_Fang_R": {"rot": rot(x=-30, y=6)}},
                 {k: {"loc": v["loc"] * 0.5} for k, v in curl.items()})
    return [(1, {}), (9, stagger), (24, half), (40, final), (61, final)]


def build_actions(rig):
    rig.animation_data_create()
    actions = {}
    for state, (last, loop) in CLIPS.items():
        act = bpy.data.actions.new("A_VenomousSpider_" + state)
        rig.animation_data.action = act
        if state == "Idle":
            for f in range(1, last + 1, 4):
                apply_pose(rig, f, idle_pose(f))
            apply_pose(rig, last, idle_pose(1))
        elif state == "Walk":
            for f in range(1, last + 1):
                apply_pose(rig, f, walk_pose(f))
        elif state == "Attack":
            for f, pose in attack_keys():
                apply_pose(rig, f, pose)
        elif state == "Hit":
            for f, pose in hit_keys():
                apply_pose(rig, f, pose)
        elif state == "Death":
            for f, pose in death_keys(rig):
                apply_pose(rig, f, pose)
        if state == "Walk":
            for fc in act.fcurves:
                for kp in fc.keyframe_points:
                    kp.interpolation = "LINEAR"
        act.use_fake_user = True
        act["metinar_loop"] = loop
        act["metinar_last_frame"] = last
        actions[state] = act
    rig.animation_data.action = None
    return actions


def min_deformed_z(mesh_obj, rig, action, last):
    """Lowest deformed vertex over the clip (ground-penetration check)."""
    rig.animation_data.action = action
    lowest = 1e9
    deps = bpy.context.evaluated_depsgraph_get()
    for f in range(1, last + 1):
        bpy.context.scene.frame_set(f)
        ev = mesh_obj.evaluated_get(deps)
        me = ev.to_mesh()
        lowest = min(lowest, min((ev.matrix_world @ v.co).z for v in me.vertices))
        ev.to_mesh_clear()
    return lowest
