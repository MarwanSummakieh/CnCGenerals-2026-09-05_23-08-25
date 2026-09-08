# Reference-directed model fidelity pass

The final library preserves **32 unit IDs and 20 structure IDs** across two factions: **52 original variants**. The supplied tank, aircraft and industrial architecture references guide their forms and materials. All detail is actual mesh geometry. No extracted game assets, paid assets, third-party downloads or external model textures are required.

## Final visual direction

- **Tanks:** broad graphite hulls, layered wedge turrets, oversized rectangular railguns with open muzzles, dense inset panels, grilles, fasteners, protected optics, smoke launchers and roof weapons. Individual track shoes, pads, sprockets and bolted road wheels remain visible below the side armor. Heavy tanks use a wider chassis and twin railguns.
- **Aircraft:** sculpted pearl shells, segmented sweeping wings, dark recessed machinery, black canopies and amber engine accents. Fighters, bombers, helicopters and drones have distinct proportions and geometry.
- **Structures:** stacked dark industrial masses, cantilevered service decks, cyan office grids and signs, exposed services and roof machinery. Factories and barracks have modeled open exits and deployment lanes. The airfield combines a runway with four numbered aircraft pads.
- **Other military units:** human infantry with fatigues, webbing, helmets and visible faces; tracked bulldozers with roll cages and hydraulic blades; wheeled reconnaissance and support vehicles with tire treads, stamped rims, brush guards and mirrors. These retain the desert/olive and olive/oxide-red faction palettes.

The 12 reference unit variants are Tank, Heavy, Fighter, Bomber, Helicopter and Drone for each faction. Current exported triangle counts are:

| Type | Triangles per faction variant |
| --- | ---: |
| Tank | 69,956 |
| Heavy | 78,180 |
| Fighter | 37,710 |
| Bomber | 45,478 |
| Helicopter | 42,718 |
| Drone | 55,982 |

The maximum current unit is **78,180 triangles**; the generator enforces a limit below 90,000. Exact dimensions and all 32 counts are in `ArtSource/Units/unit_asset_manifest.json`.

## Runtime integration

Tank, Heavy, APC and Scout export a child mesh named `TurretPivot`; the helicopter exports `Rotor`. Their local FBX axes are Y-up and their origins are at the rotation centers. The body remains the root mesh. Runtime code finds these descendant transforms and rotates them around local Y.

The material namespaces separate the final designs:

- `{Faction}_TankArmor`, `_TankSecondary`, `_TankMetal`, `_TankRubber`, `_TankGlow`, `_TankGlass`.
- `{Faction}_AircraftArmor`, `_AircraftSecondary`, `_AircraftMetal`, `_AircraftGlass`, `_AircraftGlow`, `_AircraftSensor`.
- `{Faction}_StructureArmor`, `_StructureSecondary`, `_StructureMetal`, `_StructureGlass`, `_StructureGlow`.
- Existing `{Faction}_Armor`, `_Secondary`, `_Glow`, `_Metal`, `_Glass`, `_Rubber`, `_Skin` for infantry and other military vehicles.

The original `_Glow` slot is painted identification. The dedicated tank, aircraft and structure glow slots supply their reference-specific optics, engine lighting and illuminated architecture; they should not all receive the same global emission setting.

Each airfield reserves **four Fighter/Bomber slots including queued planes**. A completed plane parks on its assigned pad and carries four ammunition charges. It returns to land and rearm after firing; rearming takes eight seconds with normal power and slows under a power shortage. Helicopters and drones do not consume fixed-wing parking slots. Factory and barracks production emerges through the modeled exits, follows the deployment lanes, then continues to its rally destination.

The models have mechanical pivots, but no infantry skeletons or skeletal animation clips. Their surface detail is geometry and material separation rather than model texture atlases or normal maps. The library and skirmish do not claim exact C&C Generals reproduction or full feature parity.

## Reproduction and verification

Run with Blender 5.2:

```text
blender --background --python Tools/Blender/generate_units.py -- --render
blender --background --python Tools/Blender/generate_structures.py
blender --background --python Tools/Blender/validate_model_exports.py
```

For only the 12 reference unit variants:

```text
blender --background --python Tools/Blender/generate_units.py -- --filter Tank,Heavy,Fighter,Bomber,Helicopter,Drone --render
```

Filtered generation merges its entries into the complete manifest and saves `ArtSource/Units/{Faction}_Reference_Units.blend`. `ref_tanks.py`, `ref_aircraft.py` and `ref_structures.py` contain the respective reference-directed builders. The current individual FBXs are in `Assets/Armies/Models`.

Generators explicitly triangulate geometry, remove zero-area faces and enforce triangle budgets. The export validator reads all 52 FBXs back, checks finite coordinates, explicit triangles, manifest dimensions and mechanical pivot names, and writes `ArtSource/model_export_validation.json`. This describes the checks; current build and runtime results are recorded separately in `Documentation/GameplayVerification.md`.

Editable sources and actual mesh preview PNGs are in `ArtSource/Units` and `ArtSource/Structures`. The full unit catalogues preserve the other ten unit types and earlier versions of the replaced models; the separate reference catalogues contain the current six updated types per faction. The earlier combined `ArtSource/NeonFrontier_Armies.blend` is a historical layout.
