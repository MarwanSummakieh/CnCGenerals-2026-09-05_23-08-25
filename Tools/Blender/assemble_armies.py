"""Append the 52 authored army meshes into one editable Blender layout.

Run: blender --background --python Tools/Blender/assemble_armies.py
Existing source catalogues are read only. Locations mirror the Unity showcase,
using Blender Z-up, with Unity's ground Z coordinate mapped to Blender Y.
"""
import os
import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
DESTINATION = os.path.join(ROOT, 'ArtSource/NeonFrontier_Armies.blend')
STRUCTURES = ['Command','Power','Refinery','Barracks','Factory','Airfield','Tech','Turret','AirDefense','Superweapon']
UNITS = ['Worker','Rifle','Rocket','Engineer','Commando','Scout','APC','Tank','Heavy','Artillery','AntiAir','Support','Drone','Helicopter','Fighter','Bomber']

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for collection in list(bpy.data.collections):
    bpy.data.collections.remove(collection)

scene = bpy.context.scene
scene.name = 'Neon Frontier - Complete Army Layout'
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
count = 0

def append_models(source, names, collection, positions):
    global count
    with bpy.data.libraries.load(source, link=False) as (available, target):
        missing = [name for name in names if name not in available.objects]
        if missing:
            raise RuntimeError('Missing named model(s) in ' + source + ': ' + ', '.join(missing))
        target.objects = list(names)
    objects = {obj.name: obj for obj in target.objects if obj is not None}
    for name, position in zip(names, positions):
        obj = objects[name]
        assert obj.type == 'MESH', name
        collection.objects.link(obj)
        obj.location = position
        obj.rotation_euler = (0, 0, 0)
        obj.scale = (1, 1, 1)
        bpy.context.view_layer.update()
        bottom = min((obj.matrix_world @ Vector(corner)).z for corner in obj.bound_box)
        obj.location.z -= bottom
        obj['layout_source'] = os.path.relpath(source, ROOT).replace('\\', '/')
        obj['layout_id'] = name
        count += 1

for faction, center in [('Vanguard', -52), ('Dynasty', 52)]:
    collection = bpy.data.collections.new(faction + ' - Complete Army')
    scene.collection.children.link(collection)
    positions = [(center + (i % 5 - 2) * 17, 32 - (i // 5) * 22, 0) for i in range(10)]
    append_models(os.path.join(ROOT, 'ArtSource/Structures', faction + '_Structures.blend'),
                  [faction + '_' + key for key in STRUCTURES], collection, positions)
    positions = [(center + (i % 4 - 1.5) * 20, -13 - (i // 4) * 13, 0) for i in range(16)]
    append_models(os.path.join(ROOT, 'ArtSource/Units', faction + '_Units.blend'),
                  [faction + '_' + key for key in UNITS], collection, positions)

studio = bpy.data.collections.new('Studio - Platform, Lights and Camera')
scene.collection.children.link(studio)

def move_to_studio(obj):
    for collection in list(obj.users_collection):
        collection.objects.unlink(obj)
    studio.objects.link(obj)

floor_mat = bpy.data.materials.new('Atlas Dark Platform')
floor_mat.diffuse_color = (.025, .035, .055, 1)
floor_mat.use_nodes = True
surface = floor_mat.node_tree.nodes.get('Principled BSDF')
surface.inputs['Base Color'].default_value = (.025, .035, .055, 1)
surface.inputs['Metallic'].default_value = .18
surface.inputs['Roughness'].default_value = .72
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, -8, -.42))
floor = bpy.context.object
floor.name = 'Complete Army Display Platform'
floor.dimensions = (194, 111, .72)
floor.data.materials.append(floor_mat)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bevel = floor.modifiers.new('Soft platform edges', 'BEVEL')
bevel.width = .7
bevel.segments = 2
move_to_studio(floor)

world = bpy.data.worlds.new('Neon Frontier Studio World')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (.12, .16, .23, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .6
scene.world = world

bpy.ops.object.light_add(type='AREA', location=(0, -5, 95))
light = bpy.context.object
light.name = 'Army Atlas Softbox'
light.data.energy = 100000
light.data.shape = 'DISK'
light.data.size = 135
move_to_studio(light)
bpy.ops.object.light_add(type='SUN', location=(0, 0, 30))
light = bpy.context.object
light.name = 'Army Atlas Directional Fill'
light.rotation_euler = (.45, -.5, -.5)
light.data.energy = 2
move_to_studio(light)

bpy.ops.object.camera_add(location=(105, -155, 175))
camera = bpy.context.object
camera.name = 'Both Armies - Overview Camera'
camera.rotation_euler = (Vector((0, -8, 1)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 220
camera.data.clip_end = 1000
scene.camera = camera
move_to_studio(camera)

scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.render.resolution_x = 2200
scene.render.resolution_y = 1500
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
scene['asset_count'] = count
scene['layout_description'] = 'Two complete original RTS factions: 16 mobile units and 10 structures each. Static meshes; editable arrangement matches the Unity army atlas.'

# Open the file ready to inspect from the saved presentation camera.
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_perspective = 'CAMERA'
            area.spaces.active.clip_end = 1000
            area.spaces.active.shading.type = 'MATERIAL'

bpy.ops.object.select_all(action='DESELECT')
assert count == 52, count
assert len([o for o in scene.objects if 'layout_id' in o]) == 52
bpy.ops.wm.save_as_mainfile(filepath=DESTINATION)
print('ASSEMBLY_COMPLETE', count, 'army models in', DESTINATION, flush=True)
