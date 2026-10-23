"""METINAR-016 — studio render helpers for the Wandering Merchant previews (Cycles CPU, headless)."""
import math

import bpy
from mathutils import Vector


def setup_scene(samples=40, res=(1280, 960)):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.image_settings.file_format = "PNG"
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("Studio")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.34, 0.36, 0.40, 1)
    bg.inputs[1].default_value = 0.7
    sc.world = world
    return sc


def add_ground(size=8.0, color=(0.13, 0.13, 0.14)):
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, 0))
    g = bpy.context.active_object
    g.name = "Ground"
    mat = bpy.data.materials.new("M_StudioGround")
    mat.use_nodes = True
    p = mat.node_tree.nodes["Principled BSDF"]
    p.inputs["Base Color"].default_value = (*color, 1)
    p.inputs["Roughness"].default_value = 0.9
    g.data.materials.append(mat)
    return g


def add_height_marker(height=1.75, x=0.55):
    """Thin orange pole of exactly `height` metres next to the character."""
    mat = bpy.data.materials.new("M_StudioMarker")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1.0, 0.45, 0.05, 1)
    objs = []
    for loc, scale in (((x, 0.3, height / 2), (0.012, 0.012, height)), ((x, 0.3, height), (0.08, 0.012, 0.012))):
        bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
        o = bpy.context.active_object
        o.scale = scale
        o.data.materials.append(mat)
        objs.append(o)
    return objs


def add_lights():
    bpy.ops.object.light_add(type="SUN", location=(2, -2, 4))
    sun = bpy.context.active_object
    sun.data.energy = 1.8
    sun.rotation_euler = (math.radians(50), math.radians(8), math.radians(35))
    bpy.ops.object.light_add(type="AREA", location=(-2.5, -2.0, 2.2))
    fill = bpy.context.active_object
    fill.data.energy = 160
    fill.data.size = 3
    fill.rotation_euler = (math.radians(60), 0, math.radians(-50))
    bpy.ops.object.light_add(type="AREA", location=(0.8, 2.6, 2.2))
    rim = bpy.context.active_object
    rim.data.energy = 200
    rim.data.size = 2
    rim.rotation_euler = (math.radians(-60), 0, math.radians(170))


def camera(name, location, target=(0, 0, 0.95), lens=50, ortho_scale=None):
    data = bpy.data.cameras.new(name)
    if ortho_scale:
        data.type = "ORTHO"
        data.ortho_scale = ortho_scale
    data.lens = lens
    cam = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(location)
    cam.rotation_euler = (Vector(target) - cam.location).to_track_quat("-Z", "Y").to_euler()
    return cam


def render(cam, path):
    sc = bpy.context.scene
    sc.camera = cam
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
