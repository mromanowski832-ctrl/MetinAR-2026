"""Helpers shared by the Blender-side scripts (run inside Blender's Python)."""
from __future__ import annotations

import os
import sys

import bpy  # type: ignore


def script_args() -> list[str]:
    argv = sys.argv
    return argv[argv.index("--") + 1:] if "--" in argv else []


def enable_gr2_addon() -> str:
    """Register the io_scene_gr2 add-on from GR2_ADDON_DIR (folder that contains io_scene_gr2/)."""
    addon_dir = os.environ.get("GR2_ADDON_DIR")
    if not addon_dir or not os.path.isdir(os.path.join(addon_dir, "io_scene_gr2")):
        raise SystemExit("GR2_ADDON_DIR must point at the folder containing io_scene_gr2/")
    if addon_dir not in sys.path:
        sys.path.insert(0, addon_dir)
    import io_scene_gr2  # type: ignore

    try:
        io_scene_gr2.register()
    except ValueError:  # already registered in this session
        pass
    return ".".join(str(v) for v in io_scene_gr2.bl_info["version"])


def reset_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export_fbx(path: str, objects, *, with_animation: bool, mesh: bool = True) -> None:
    """FBX export with the settings from docs/METINAR-009-ASSET-PIPELINE.md section 3.1."""
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"ARMATURE", "MESH"} if mesh else {"ARMATURE"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        use_space_transform=True,
        bake_space_transform=False,
        add_leaf_bones=False,
        primary_bone_axis="Y",
        secondary_bone_axis="X",
        use_armature_deform_only=False,
        bake_anim=with_animation,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        path_mode="STRIP",
    )
