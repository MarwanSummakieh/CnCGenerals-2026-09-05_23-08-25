# Layered industrial RTS architecture

Twenty original structures rebuilt from the user's September 7 building reference: dark stacked building masses, cantilever decks, cyan signage, illuminated modular windows and dense rooftop service equipment. Heights stay at the existing RTS scale. Vanguard uses blue graphite; Dynasty uses warm graphite. Models are original procedural geometry, without extracted game assets or external texture dependencies.

`Tools/Blender/generate_structures.py` is the entry point. `Tools/Blender/ref_structures.py` supplies the reference architecture kit, the three operational production layouts and attachment points. The conventional builder functions retain each facility's recognizable functional silhouette while the shared architecture replaces roofs, walls, glazing, materials and exterior services.

## Operational layouts

The airfield has exactly four unobstructed aircraft pads and a separate full-length runway. Its raw footprint is 24 × 34 meters. Pad surfaces and exported anchors are at Blender Z = 0.4 meters. Each pad measures 8 × 5.55 meters. The 7-meter-wide runway is 31 meters long.

| Exported child | Blender local coordinates, meters |
|---|---|
| AircraftPad0 | (5, -10.5, 0.4) |
| AircraftPad1 | (5, -3.5, 0.4) |
| AircraftPad2 | (5, 3.5, 0.4) |
| AircraftPad3 | (5, 10.5, 0.4) |
| AircraftRunwayStart | (-5.5, -14.5, 0.4) |
| AircraftRunwayEnd | (-5.5, 14.5, 0.4) |
| Factory: ProductionExit | (0, -3.1, 0.62) |
| Factory: ProductionLaneEnd | (0, -8.4, 0.62) |
| Barracks: ProductionExit | (0, -3.8, 0.62) |
| Barracks: ProductionLaneEnd | (0, -5.5, 0.62) |

Factory production rails run through the genuinely open workshop and project onto the apron, with a visible gantry crane and fabrication arms. The barracks has an open soldier alcove, a protective overhang, bollards and a clearly lit deployment path.

Anchors are EMPTY objects parented to the building mesh and exported as children with unsuffixed names. Runtime code should discover the imported child transforms; it should not manually assume the FBX axis conversion. FBX exports use -Z forward / Y up, applied meter scale and a ground origin. Catalogue placement happens after export and preserves anchor local coordinates.

## Material contract

Each faction uses five named materials: `{Faction}_StructureArmor`, `StructureSecondary`, `StructureGlow`, `StructureGlass`, and `StructureMetal`. Cyan glow and glazing carry emission in Blender; the Unity material remap supplies corresponding URP materials. The old `Accent` helper aliases the secondary material instead of creating an extra slot.

The generator triangulates every face, removes zero-area geometry, validates the 40,000-triangle limit per structure, saves editable faction `.blend` catalogues, renders PNG overviews and writes `structure_validation.json` including the exact runtime anchor contract.
