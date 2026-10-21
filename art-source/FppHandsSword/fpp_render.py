"""Preview renders (Cycles CPU, headless): phone-like first-person camera at the AR camera origin + studio views."""
import math

import bpy
from mathutils import Vector

# Estimated Galaxy A31 AR framing in PORTRAIT (project default orientation): 26 mm-equivalent main camera,
# 4:3 sensor shown cover-scaled on a 20:9 screen -> ~69.4 deg vertical, ~34.6 deg horizontal. This is an
# ESTIMATE for preview only; the real ARCore intrinsics must be measured on the phone.
A31_PORTRAIT_VFOV = 69.4
A31_PORTRAIT_RES = (540, 1200)


def setup(samples=32):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Room")
    world.use_nodes = True
    nt = world.node_tree
    bg = nt.nodes["Background"]
    grad = nt.nodes.new("ShaderNodeTexGradient")
    coord = nt.nodes.new("ShaderNodeTexCoord")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.inputs["Rotation"].default_value = (0, math.radians(-90), 0)
    nt.links.new(coord.outputs["Generated"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], grad.inputs["Vector"])
    nt.links.new(grad.outputs["Fac"], ramp.inputs["Fac"])
    ramp.color_ramp.elements[0].color = (0.16, 0.17, 0.19, 1)
    ramp.color_ramp.elements[1].color = (0.42, 0.44, 0.47, 1)
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    bg.inputs[1].default_value = 0.7
    sc.world = world
    return sc


def lights():
    bpy.ops.object.light_add(type="SUN", location=(1, -1, 3))
    sun = bpy.context.active_object
    sun.data.energy = 2.2
    sun.rotation_euler = (math.radians(40), math.radians(-25), math.radians(20))
    bpy.ops.object.light_add(type="AREA", location=(-1.2, 0.2, 0.6))
    fill = bpy.context.active_object
    fill.data.energy = 25
    fill.data.size = 1.5
    fill.rotation_euler = (0, math.radians(-70), 0)
    bpy.ops.object.light_add(type="AREA", location=(0.6, 1.4, 0.8))
    rim = bpy.context.active_object
    rim.data.energy = 40
    rim.data.size = 1.0
    rim.rotation_euler = (math.radians(-70), 0, math.radians(160))


def fpp_camera(name="FPP", vfov=A31_PORTRAIT_VFOV):
    data = bpy.data.cameras.new(name)
    data.sensor_fit = "VERTICAL"
    data.angle_y = math.radians(vfov)
    data.clip_start = 0.02
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = (0, 0, 0)
    cam.rotation_euler = (math.radians(90), 0, 0)  # looks along Blender +Y (= Unity camera +Z), up +Z
    return cam


def camera(name, location, target, lens=50):
    data = bpy.data.cameras.new(name)
    data.lens = lens
    data.clip_start = 0.01
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Z").to_euler()
    return cam


def render(cam, path, res):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
