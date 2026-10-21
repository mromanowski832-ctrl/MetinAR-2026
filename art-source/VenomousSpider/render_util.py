"""METINAR-013 — studio render helpers (Cycles CPU, headless)."""
import math

import bpy
from mathutils import Vector


def setup_scene(samples=48, res=(1280, 960)):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Studio")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.32, 0.34, 0.37, 1)
    bg.inputs[1].default_value = 0.6
    sc.world = world
    return sc


def add_ground(size=6.0, color=(0.22, 0.22, 0.23)):
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, 0))
    g = bpy.context.active_object
    g.name = "Ground"
    mat = bpy.data.materials.new("M_Ground")
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = 0.9
    g.data.materials.append(mat)
    return g


def add_span_marker(span=1.3, y=0.9):
    """Thin orange bar of exactly `span` metres on the ground, with end ticks."""
    mat = bpy.data.materials.new("M_Marker")
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (1.0, 0.45, 0.05, 1)
    objs = []
    for loc, scale in (((0, y, 0.004), (span, 0.012, 0.008)), ((-span / 2, y, 0.02), (0.012, 0.05, 0.04)), ((span / 2, y, 0.02), (0.012, 0.05, 0.04))):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object
        o.scale = scale
        o.data.materials.append(mat)
        objs.append(o)
    return objs


def add_lights():
    bpy.ops.object.light_add(type="SUN", location=(2, -2, 4))
    sun = bpy.context.active_object
    sun.data.energy = 1.6
    sun.rotation_euler = (math.radians(45), math.radians(10), math.radians(35))
    bpy.ops.object.light_add(type="AREA", location=(-2.5, -1.5, 2.0))
    fill = bpy.context.active_object
    fill.data.energy = 120
    fill.data.size = 3
    fill.rotation_euler = (math.radians(60), 0, math.radians(-60))
    bpy.ops.object.light_add(type="AREA", location=(0.5, 2.5, 1.6))
    rim = bpy.context.active_object
    rim.data.energy = 150
    rim.data.size = 2
    rim.rotation_euler = (math.radians(-60), 0, math.radians(170))


def camera(name, location, target=(0, 0, 0.25), lens=50, ortho_scale=None):
    cam_data = bpy.data.cameras.new(name)
    if ortho_scale:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho_scale
    cam_data.lens = lens
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(location)
    d = Vector(target) - cam.location
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return cam


def render(cam, path):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
