# Army showcase verification (0.1)

These are the original army-art checks. The playable skirmish added in 0.2 has its own runtime report in `GameplayPreview/verification-report.txt` and notes in `GameplayVerification.md`.

Validated locally with Unity 6000.3.10f1 and Blender 5.2 on Windows.

- Unity script compilation and Windows standalone build completed successfully.
- The scene generator was run repeatedly, including regeneration over existing assets.
- Unity validation passed for 52 unique definitions, 52 prefabs and 52 selectable scene entries: each faction has 16 units and 10 structures.
- Every prefab contains a mesh, valid URP materials, a selection collider and its matching roster definition.
- Blender mesh reports cover 189,676 total model triangles. Explicit triangulation resolves the building import warnings; final Unity imports report no self-intersecting polygons.
- The Windows player exercised overview, both faction views, and selection/focus for a tank, infantry and a superweapon, then exited cleanly without script exceptions.
- Seven GPU camera renders were generated. Overview, faction, tank, infantry and structure renders were inspected; oversized ground labels were corrected before the final pass.
- `git diff --check` passed.

Final logs: `Logs/ArmyBuildPolish.log` and `Logs/ArmyPlayerPolish.log`. Asset checks: `Documentation/AssetValidation.txt`. Blender geometry reports are in the corresponding `ArtSource` folders.

Preview PNGs in `Documentation/Preview` are offscreen renders of the 3D scene. They omit the live IMGUI roster browser. Interactive mouse/keyboard behavior and HUD appearance have not been manually exercised in a visible window; automated player checks cover the public focus/selection paths. The delivered scene and executable include that browser.

These original checks validated the army art/layout prototype. Gameplay validation is recorded separately; character rigging and animation remain outside the demo's scope.
