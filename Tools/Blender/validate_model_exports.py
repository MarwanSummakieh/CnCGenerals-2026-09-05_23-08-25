"""Read every FBX back through Blender and validate geometry, scale and articulation.

Run: blender --background --python Tools/Blender/validate_model_exports.py
The report is written to ArtSource/model_export_validation.json.
"""
import bpy
import json
import math
import os
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '../..'))
report = []
errors = []
for kind, manifest in [('Units', 'Units/unit_asset_manifest.json'), ('Structures', 'Structures/structure_validation.json')]:
    with open(os.path.join(ROOT, 'ArtSource', manifest), encoding='utf-8') as source:
        assets = json.load(source)
    for entry in assets:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = os.path.join(ROOT, 'Assets/Armies/Models', kind, entry['name'] + '.fbx')
        bpy.ops.import_scene.fbx(filepath=path, use_anim=False)
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        points = [o.matrix_world @ v.co for o in meshes for v in o.data.vertices]
        if not points:
            errors.append(entry['name'] + ': no geometry')
            continue
        dims = [max(p[axis] for p in points) - min(p[axis] for p in points) for axis in range(3)]
        finite = all(math.isfinite(n) for p in points for n in p)
        triangulated = all(len(p.vertices) == 3 for o in meshes for p in o.data.polygons)
        # FBX stores baked Y-up meshes; the importer may preserve that orientation.
        expected = sorted(entry['dimensions_m'])
        actual = sorted(dims)
        scale_ok = all(abs(a-b) < .025 for a,b in zip(actual, expected))
        articulated = [o.name for o in meshes if o.name.startswith(('TurretPivot', 'Rotor'))]
        expected_articulation = entry.get('articulated_parts', [])
        articulation_ok = all(any(n.startswith(wanted) for n in articulated) for wanted in expected_articulation)
        if not (finite and triangulated and scale_ok and articulation_ok):
            errors.append(entry['name'] + ': finite=%s triangles=%s scale=%s articulation=%s' % (finite,triangulated,scale_ok,articulation_ok))
        item = {'name': entry['name'], 'mesh_count': len(meshes), 'triangles': sum(len(o.data.polygons) for o in meshes), 'dimensions_m': [round(v,4) for v in dims], 'scale_matches_manifest': scale_ok, 'finite_vertices': finite, 'explicit_triangles': triangulated, 'articulation': articulated}
        report.append(item)
        print('CHECKED', item, flush=True)

with open(os.path.join(ROOT, 'ArtSource/model_export_validation.json'), 'w', encoding='utf-8') as output:
    json.dump({'asset_count': len(report), 'errors': errors, 'assets': report}, output, indent=2)
if errors:
    raise RuntimeError('\n'.join(errors))
print('VALIDATED', len(report), 'FBX assets, no errors', flush=True)
