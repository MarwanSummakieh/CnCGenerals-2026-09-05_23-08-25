# Cyberpunk army structure kit

The kit contains twenty original, static RTS structure meshes: ten roles for Vanguard and ten for Dynasty. They use procedural geometry and named materials; no third-party art or external textures are required.

Vanguard uses ivory ceramic armor, deep navy chassis, cyan electrical bands, narrow roof fins, and a clean aerospace silhouette. Dynasty uses charcoal armor, scarlet structural masses, amber power systems, gold edging, and broad stepped roof eaves. Both factions use reinforced chamfered foundations and faceted armor so their outlines remain readable from an RTS camera.

| File suffix | Visible role and identifying features | Triangles per faction |
| --- | --- | ---: |
| Command | Central uplink tower, observation crown, operations wings, antenna and radar | 3,620 |
| Power | Twin exposed reactor coils, containment ribs, cooling fins and transformer building | 4,952 |
| Refinery | Two storage vessels, filtering stacks, crane gantry, freight magnet and weighbridge | 4,996 |
| Barracks | Habitat block, security entrance, window row, rooftop ventilation and pennants | 3,788 |
| Factory | Wide blast-door assembly bay, rooftop service gantry and sawtooth cooling vents | 3,656 |
| Airfield | Circular illuminated VTOL pad, hangar, control tower and fuel cylinders | 4,488 |
| Tech | Elevated laboratory, open radar bowl, quantum core fins and service vessel | 3,968 |
| Turret | Twin heavy railguns with cooling collars, targeting optic and armored bunker | 1,876 |
| AirDefense | Eight individual launch tubes in two raised pods and phased-array radar | 3,660 |
| Superweapon | Tall contained energy core, four armor petals, focal spike and control wings | 5,834 |

## Files and regeneration

- `Assets/Armies/Models/Structures/{Faction}_{Role}.fbx`: one joined mesh each, with its origin at ground center, applied scale, meter units, and material slots.
- `ArtSource/Structures/{Faction}_Structures.blend`: editable catalogue scene for each faction. The individual buildings are separate mesh objects, arranged in two rows. Named materials and camera remain editable.
- `ArtSource/Structures/{Faction}_Structures_Preview.png`: rendered orthographic catalogue.
- `ArtSource/Structures/structure_validation.json`: export counts, vertex counts, triangle counts, bounds, material counts, and file sizes.
- `Tools/Blender/generate_structures.py`: complete deterministic generator.

Run from the repository root:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python Tools/Blender/generate_structures.py
```

The export convention is `-Z` forward and `Y` up. Geometry is authored in Blender meters with Z up before FBX conversion. Airfields have a 15 × 13 meter footprint. Most other structures occupy 7–14 meters and stand 5–10 meters high; command antenna and superweapon tip reach approximately 10.6 meters. Barrel and entry stair projections are included in reported bounds.

## Material integration

Each faction has the stable material names `Armor`, `Secondary`, `Glow`, `Metal`, `Glass`, and `Accent`, prefixed by `Vanguard_` or `Dynasty_`. Glow materials have emission strength 3.4 in Blender. Unity can remap these names to URP materials, preserving faction colors and adding bloom. FBX does not preserve every Blender shader setting, so Unity material remapping is recommended for consistent emission. Glass is opaque tinted glazing, avoiding sorting issues in the RTS view. The assets do not rely on UV textures.

These are static presentation meshes. They do not contain turret animation rigs, construction stages, destruction states, collisions, pathfinding footprints, or gameplay logic. Moving parts are visibly modeled but joined to meet the single-mesh delivery requirement; the generator is the source for splitting those parts during later animation work.

## Validation

Generated and exported with Blender 5.2.1 LTS. All 20 exports are below 20,000 triangles; the largest is 5,834. Every export is explicitly triangulated and checked to contain zero nontriangular or zero-area faces. Collapsed bevel seams are removed before export to prevent Unity's FBX importer from discarding ambiguous polygons. The generated catalogue renders were inspected for silhouette, model completeness, material appearance, and framing. These assets are original faction concepts inspired by broad near-future RTS archetypes, not reproductions of existing Command & Conquer designs.
