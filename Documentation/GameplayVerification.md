# Playable skirmish verification

The entry scene is `Assets/Armies/Scenes/NeonFrontier.unity`. It starts at the main menu and references all 52 original army definitions across two factions. The Windows output is `Builds/NeonFrontierDemo/NeonFrontier.exe`.

## Current reference-directed overhaul

The library preserves all **52 original variants**: 16 mobile unit types and 10 structures for each of two factions. The final supplied references guide graphite tanks with layered wedge turrets and rectangular railguns, sculpted pearl aircraft with dark machinery and amber engines, and stacked industrial buildings with graphite facades, exposed services and cyan window grids. Infantry, bulldozers and other support vehicles retain their military equipment and faction palettes. The 12 updated tank and aircraft variants contain actual detailed mesh geometry, with a current maximum of **78,180 triangles** per unit. No extracted game models, asset downloads or paid assets are required.

The command console provides rendered model portraits, production cards, cancellable queues, research, ranks, radar and a power meter. Factories and barracks have modeled open exits and deployment lanes; completed units move through those exits before continuing toward their rally destination.

The battlefield uses an original 2048 terrain atlas with surface relief, worn concrete hardstands, supply tracks and wheel ruts, muted river water with animated ripple lighting, irregular trees, bank pebbles and perimeter logistics props. Warm shadows and a runtime ACES/color/bloom profile replace the brighter prototype presentation. Combat now includes traveling projectiles, impact damage, muzzle flashes, smoke, fire, debris, vehicle dust, scorch marks and selected mechanical animations.

Gameplay additions include fog of war with persistent terrain exploration, veteran ranks, power management and three army upgrades. Insufficient power reduces recruitment to **30% of normal speed** and disables ground defenses, air defenses and the strategic weapon. Composite Armor adds **25% maximum health** to existing and future mobile units; Advanced Munitions adds **20% weapon damage**; Field Logistics adds **30% production speed**.

Each airfield has **four fixed-wing parking slots**, shared by live assigned aircraft and queued Fighter/Bomber reservations. Cancelling a queued plane frees its reservation. Completed planes park at four distinct exported model anchors. They carry **four ammunition charges**, launch on orders, return to their pad and rearm in **eight seconds with normal power**; power shortages slow service. Helicopters and drones do not consume fixed-wing slots. Stopping a returning plane requests a fresh route home. When its airfield is destroyed, an aircraft seeks another completed allied airfield with free capacity; service is cancelled without a surviving home, and an empty aircraft waits for an available slot rather than regenerating ammunition in the air.

Current integration and screenshot results are recorded below, separately from the historical prototype baseline.

## Verification method

`SkirmishVerification` is inert during normal play. Supplying `-skirmishVerify <directory>` enables runtime integration checks through the game's lifecycle, navigation, construction, production and combat methods. Baseline coverage includes both factions, menu and help/settings return paths, pause/resume, supply income, queued recruitment, rejected river construction, completed worker construction, bridge routing, weapon damage, victory, defeat, rematch and cleanup.

Current additional fixtures cover queue cancellation and refunds, reconnaissance revealing and concealing enemy objects, projectile travel before damage, kill experience, armor research and duplicate-purchase rejection, infrastructure destruction and low-power production. Airfield fixtures exercise four queued reservations, fifth-plane rejection, cancellation releasing a slot, four produced planes occupying distinct physical pads, launch and return orders, stopping during return, and loss of rearming infrastructure.

Some setup is deliberate: the airfield fixture spawns completed infrastructure, then uses the real economy, queues, production, parking and flight methods. A partly spent ammunition value isolates the home-loss rearming regression. Combat fixtures position real units, and result screens deliberately destroy headquarters. These isolate system behavior and do not establish competitive balance, exhaustive input coverage or a complete naturally played match. Barracks production asserts that the recruit uses its imported exit, finishes the deployment lane safely after a halt order, and does not restart its old rally order. Factory and barracks anchor geometry is also covered by the Blender export validation.

The capture path first tries the player's real framebuffer, including its IMGUI interface, and checks brightness and image variation. If a hidden Windows swapchain remains blank, the native fallback renders the camera and the **same live `OnGUI` Repaint** into an explicit GPU target. It requires a recorded repaint and measurable changes in interface regions relative to the world render alone, and labels the capture separately as `offscreen world + actual IMGUI`. The current run validates this path on Direct3D 12. The helper disables an extra sRGB write conversion for the legacy GUI and restores native state in cleanup. Raw portrait textures are saved separately to check color consistency. These are actual controller renders, not evidence of physical mouse/keyboard interaction or swapchain presentation.

If both interface capture paths fail, the runner records an interface-capture failure and may save a separately labeled world-only GPU render. A world-only image does not validate the interface. No replacement web UI or reconstructed screenshot is used as evidence of the game's UI. Run with graphics enabled; omit `-batchmode` and `-nographics`. `-skirmishVerifyDelay 15` allows time to activate the player before checks begin.

Machine-readable results and images are written to the requested output directory. Earlier runs used `Documentation/GameplayPreview`. New overhaul results should use their own output directory so the baseline remains identifiable.

## Current run results

The final reference build completed at **2026-09-07 22:14:04 UTC** (2026-09-08 locally), Unity request `03544228f51a4b169abcaa32d571f19d`. The Windows player used its default **Direct3D 12** renderer on an NVIDIA RTX 3060 Laptop GPU. `Documentation/ReferenceRelease/verification-report.json` records **112/112 checks passed**, **14 valid 1600 × 900 world-and-interface captures**, and **zero runtime errors**, completing at 22:15:50 UTC in 75.6 seconds.

Reviewed captures include setup, both faction command consoles, the field manual, pause/results, bridge navigation, completed construction, tank combat effects, research and the four-plane airfield. Final visual corrections include readable setup map labels, single-line command hotkeys, two-line production names, brighter secondary text, consistent portrait color, separated runway/apron surfaces and a fog veil extending across the surrounding scenery. The final runtime check confirms all four planes park on distinct physical pads, the fifth reservation is rejected, a cancelled reservation can be reused, ordered aircraft launch, repeated halt commands still allow landing, and a destroyed airfield cannot replenish ammunition.

The runtime assembly also passes `dotnet build Assembly-CSharp.csproj --no-restore --verbosity quiet` with zero warnings and errors. The exported library validation reports **52/52 assets**, finite triangulated geometry, valid manifest dimensions and mechanical pivots; structure import validation covers all 20 buildings and the named production/airfield anchors. The immediately preceding `ReferencePolished` build passed the same 112 checks on Direct3D 11 before the final label and fog perimeter adjustments.

Reproduce the current runtime verification with graphics enabled:

```text
Builds/NeonFrontierDemo/NeonFrontier.exe -screen-fullscreen 0 -screen-width 1600 -screen-height 900 -skirmishVerify Documentation/ReferenceRelease -logFile Logs/ReferenceRelease.log
```

Final `NeonFrontier_Data/Managed/Assembly-CSharp.dll` SHA-256: `6597F1C9C5208E51C3E7E324920197D5A7EC491E98434C2CA48F24690F58117E`.

## Downloadable v0.1.0 package

The branded release was rebuilt successfully from a closed Unity editor on 2026-09-08. The command-line build now opens and validates the committed entry scene in its isolated batch process; interactive editor builds continue to preserve open scenes. The launcher waits for the editor process itself, allowing the shared licensing helper to remain running normally.

The installer and portable ZIP contain 191 game files with matching SHA-256 hashes. An isolated current-user installation passed **18 packaging checks**: install/version registration, launch shortcut, all installed payload hashes, portable ZIP integrity, refusal to update a running match, and uninstall removal of the game, registration and shortcut. The installed player completed **112/112 runtime checks** and **14 interface captures** with **zero runtime errors** at 2026-09-08 07:20:39 UTC. The packaging check completed at 07:20:40 UTC. The report and SHA-256 files are in `Documentation/Releases`; installers and game binaries are distributed as GitHub release assets.

The original reference-render reports above remain separate records of the screenshot and model validation. The installer's runtime verification ran from the installed game directory using the packaged native and managed files, without requiring the Unity Editor.

## Historical prototype baseline

The following results predate the military and reference-directed visual/gameplay overhauls. They establish the earlier skirmish baseline and do not validate the new models, interface, effects, power, upgrades, ranks, fog of war, aircraft service or production exits.

The original hidden-player run passed 48 gameplay assertions but returned blank screenshot buffers. Those images were rejected during review, and screenshot-content checks were added to prevent a false visual pass.

The subsequent rendered Windows prototype run passed **67 checks** with **11 valid full-frame screenshots**, including tank traversal of all three concrete bridges and a naturally launched enemy ground assault reaching the western bank. No runtime errors or exceptions were recorded in that run. Menu, setup, manual, pause, both faction HUDs, construction, bridge view, victory, defeat, and return-to-menu images were captured for inspection.

The prototype Windows player was also inspected with desktop capture: the main menu and an active Dynasty match rendered the grass, river, concrete crossings, troop selection, minimap, and command HUD correctly. The automated runner calls the public command methods; it does not claim exhaustive physical mouse/keyboard coverage.

## Scope

This is a playable single-player demo with original RTS art, without a claim of exact C&C Generals reproduction or full feature parity. Campaigns, save/load, multiplayer, transport boarding and stealth/capture abilities are not implemented. Aircraft parking, ammunition, return and rearming are implemented as described above. Infantry have no skeletal animation clips. Faction statistics and AI pressure are initial tuning rather than a claim of competitive balance. The native capture helper depends on internal Unity IMGUI APIs verified for Unity 6000.3.10f1 and is used only by opt-in verification.
