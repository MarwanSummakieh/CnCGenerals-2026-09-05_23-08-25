# Reference-directed unit art

32 original unit variants across two factions, within the complete 52-asset unit and structure library. All existing IDs remain stable. The supplied visual references guide 12 detailed unit variants: Tank, Heavy, Fighter, Bomber, Helicopter and Drone for both factions.

Tanks use broad graphite hulls, layered wedge armor and oversized rectangular railguns. Aircraft use sculpted pearl shells, sweeping segmented wings, dark recessed machinery, black canopies and amber engine accents. Infantry, bulldozers, reconnaissance and other support vehicles retain their distinct military equipment and desert/olive or olive/oxide-red faction palettes. Structures use their own dark industrial and cyan-lit palette, described in `Documentation/MilitaryArtPass.md`.

## Geometry and articulation

- Tanks have individually modeled tread shoes, rubber pads, bolted road wheels, drive sprockets, segmented skirts, access panels, grilles and fasteners. Wedge turrets carry protected optics, smoke launchers, roof weapons and rectangular cannons with open framed muzzles; Heavy uses twin cannons.
- Aircraft have actual segmented shell and wing geometry around recessed mechanical assemblies. Fighter, Bomber, Helicopter and Drone retain distinct silhouettes and proportions.
- Tank, Heavy, APC and Scout export a separate `TurretPivot` mesh. Helicopter exports `Rotor`, centered on its main shaft. These local Y-up pivots support runtime mechanical rotation; no skeletal animation clips are supplied.
- Infantry have human proportions, visible faces, fatigues, webbing, cargo pockets, protective goggles and conventional rifles or shoulder rockets.
- Workers are tracked construction bulldozers with armored glazing, roll cages, hydraulic blade arms and exhaust stacks.
- Tire treads, wheel bolts, brush guards, mirrors, intakes and service equipment remain geometry on the other military vehicles.

## Materials

- Tanks: `{Faction}_TankArmor`, `_TankSecondary`, `_TankMetal`, `_TankRubber`, `_TankGlow`, `_TankGlass`.
- Aircraft: `{Faction}_AircraftArmor`, `_AircraftSecondary`, `_AircraftMetal`, `_AircraftGlass`, `_AircraftGlow`, `_AircraftSensor`.
- Other military units: `{Faction}_Armor`, `_Secondary`, `_Glow`, `_Metal`, `_Glass`, `_Rubber`, `_Skin`.
- The original `_Glow` slot represents painted identification. Dedicated tank and aircraft glow slots supply small optics and engine accents. Preserve these separate material settings when remapping in Unity.

## Files and regeneration

- Current Unity assets: `Assets/Armies/Models/Units/{Faction}_{ID}.fbx`.
- Editable current reference variants: `ArtSource/Units/{Faction}_Reference_Units.blend`. Full unit catalogues retain the other ten types and earlier versions of the replaced models.
- Complete regeneration: `blender --background --python Tools/Blender/generate_units.py -- --render`.
- Reference-only regeneration: `blender --background --python Tools/Blender/generate_units.py -- --filter Tank,Heavy,Fighter,Bomber,Helicopter,Drone --render`. This merges the manifest and preserves separate source catalogues.
- `ref_tanks.py` and `ref_aircraft.py` supply the reference builders. FBX meshes use meters, a ground origin and standard -Z forward / Y-up conversion from Blender +Y nose / +Z up.
- Faces are explicitly triangulated and zero-area faces removed. The enforced limit is below 90,000 triangles per complete unit. The current maximum is 78,180 triangles (Heavy).
- All detail is original mesh geometry and material separation. No paid assets, asset downloads, extracted game models, model texture atlases or normal maps are required.

## Gameplay use

Airfields have four fixed-wing parking slots, with queued Fighters and Bombers reserving capacity. Planes park when produced, carry four ammunition charges, and return to land and rearm in eight seconds with normal power. A power shortage slows rearming. Helicopters and drones do not consume fixed-wing slots. Factory and barracks units emerge through their modeled exits and deployment lanes before continuing to rally points.

This is original art for a playable demo, without a claim of exact C&C Generals reproduction or full feature parity. Current build and gameplay verification results are recorded separately in `Documentation/GameplayVerification.md`.

## Export manifest

| Asset | Triangles | Dimensions (m, Blender X/Y/Z) |
|---|---:|---|
| Dynasty_APC | 12236 | [3.49, 5.159, 3.174] |
| Dynasty_AntiAir | 17664 | [3.686, 4.708, 3.346] |
| Dynasty_Artillery | 18068 | [3.686, 4.844, 3.152] |
| Dynasty_Bomber | 45478 | [11.969, 6.96, 2.35] |
| Dynasty_Commando | 2544 | [0.962, 2.138, 2.148] |
| Dynasty_Drone | 55982 | [5.061, 3.41, 1.152] |
| Dynasty_Engineer | 2424 | [0.962, 1.578, 2.148] |
| Dynasty_Fighter | 37710 | [10.329, 6.96, 2.35] |
| Dynasty_Heavy | 78180 | [4.543, 8.478, 3.737] |
| Dynasty_Helicopter | 42718 | [9.352, 9.62, 3.44] |
| Dynasty_Rifle | 2504 | [0.962, 1.808, 2.148] |
| Dynasty_Rocket | 2620 | [0.991, 1.613, 2.148] |
| Dynasty_Scout | 8436 | [2.87, 3.461, 2.48] |
| Dynasty_Support | 10972 | [3.49, 4.86, 3.344] |
| Dynasty_Tank | 69956 | [4.244, 8.021, 3.627] |
| Dynasty_Worker | 15516 | [3.366, 4.478, 3.4] |
| Vanguard_APC | 12044 | [3.49, 5.159, 3.174] |
| Vanguard_AntiAir | 17664 | [3.686, 4.708, 3.346] |
| Vanguard_Artillery | 17284 | [3.686, 6.248, 3.324] |
| Vanguard_Bomber | 45478 | [11.3, 6.96, 2.35] |
| Vanguard_Commando | 2544 | [0.962, 2.138, 2.148] |
| Vanguard_Drone | 55982 | [4.78, 3.41, 1.152] |
| Vanguard_Engineer | 2424 | [0.962, 1.578, 2.148] |
| Vanguard_Fighter | 37710 | [9.755, 6.96, 2.35] |
| Vanguard_Heavy | 78180 | [4.543, 8.478, 3.737] |
| Vanguard_Helicopter | 42718 | [9.352, 9.62, 3.44] |
| Vanguard_Rifle | 2504 | [0.962, 1.808, 2.148] |
| Vanguard_Rocket | 2620 | [0.991, 1.613, 2.148] |
| Vanguard_Scout | 8244 | [2.87, 3.461, 2.48] |
| Vanguard_Support | 10972 | [3.49, 4.86, 3.344] |
| Vanguard_Tank | 69956 | [4.244, 8.021, 3.627] |
| Vanguard_Worker | 15516 | [3.366, 4.478, 3.4] |
