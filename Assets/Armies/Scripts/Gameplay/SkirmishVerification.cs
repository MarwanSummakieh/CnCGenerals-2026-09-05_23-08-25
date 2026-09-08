using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonFrontier
{
    /// <summary>
    /// Standalone verification runner. Inert unless launched with -skirmishVerify OUTPUT_DIRECTORY.
    /// Exercises the same controller and simulation entry points used by the menu and commands.
    /// </summary>
    public sealed class SkirmishVerification : MonoBehaviour
    {
        [Serializable]
        sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        sealed class ScreenshotResult
        {
            public string file;
            public string screen;
            public string captureKind;
            public bool includesInterface;
            public bool nonblank;
            public int width;
            public int height;
            public int attempts;
            public float meanBrightness;
            public float brightnessDeviation;
            public float litFraction;
        }

        struct FrameMetrics
        {
            public float Mean, Deviation, LitFraction, Range;
            public bool Nonblank => Mean > .005f && Deviation > .012f && LitFraction > .01f && Range > .06f;
            public override string ToString() => $"mean={Mean:0.0000}, deviation={Deviation:0.0000}, lit={LitFraction:P1}, range={Range:0.0000}";
        }

        [Serializable]
        sealed class VerificationReport
        {
            public string status = "running";
            public string startedUtc;
            public string finishedUtc;
            public string failure;
            public float wallSeconds;
            public float startDelaySeconds;
            public string platform;
            public List<CheckResult> checks = new List<CheckResult>();
            public List<ScreenshotResult> screenshots = new List<ScreenshotResult>();
            public List<string> runtimeErrors = new List<string>();
        }

        GameFlowController flow;
        VerificationReport report;
        string outputDirectory;
        float startedAt, startDelaySeconds = 3;
        bool active, finished;
        const float Acceleration = 8f;
        const float WatchdogSeconds = 300f;

        IEnumerator Start()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int argumentIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "-skirmishVerify", StringComparison.OrdinalIgnoreCase));
            if (argumentIndex < 0) { enabled = false; yield break; }
            startedAt = Time.realtimeSinceStartup;
            active = true;
            report = new VerificationReport { startedUtc = DateTime.UtcNow.ToString("O"), platform = Application.platform.ToString() };
            Exception setupFailure = null;
            try
            {
                if (argumentIndex + 1 >= arguments.Length || arguments[argumentIndex + 1].StartsWith("-", StringComparison.Ordinal))
                    throw new ArgumentException("-skirmishVerify requires an output directory argument.");
                outputDirectory = Path.GetFullPath(arguments[argumentIndex + 1]);
                Directory.CreateDirectory(outputDirectory);
                int delayIndex = Array.FindIndex(arguments, argument => string.Equals(argument, "-skirmishVerifyDelay", StringComparison.OrdinalIgnoreCase));
                if (delayIndex >= 0 && (delayIndex + 1 >= arguments.Length ||
                    !float.TryParse(arguments[delayIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out startDelaySeconds) ||
                    float.IsNaN(startDelaySeconds) || startDelaySeconds < 0 || startDelaySeconds > 60))
                    throw new ArgumentException("-skirmishVerifyDelay requires a number from 0 to 60 seconds.");
                report.startDelaySeconds = startDelaySeconds;
                flow = GetComponent<GameFlowController>();
                Application.logMessageReceived += ReceiveLog;
                WriteReport();
            }
            catch (Exception exception)
            {
                setupFailure = exception;
            }
            if (setupFailure != null)
            {
                report.failure = setupFailure.ToString();
                Finish(false);
                yield break;
            }
            // Give the launcher's window activation a chance to complete before framebuffer checks.
            if (startDelaySeconds > 0) yield return new WaitForSecondsRealtime(startDelaySeconds);
            yield return RunGuarded(Verify());
        }

        // Flatten nested test enumerators so all assertion exceptions produce a report and exit code.
        IEnumerator RunGuarded(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            while (stack.Count > 0 && !finished)
            {
                bool moved = false;
                object current = null;
                Exception failure = null;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) current = stack.Peek().Current;
                }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    report.failure = failure.ToString();
                    Finish(false);
                    yield break;
                }
                if (!moved) { stack.Pop(); continue; }
                if (current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return current;
            }
            if (!finished)
            {
                Check("No runtime errors or exceptions", report.runtimeErrors.Count == 0, string.Join("\n", report.runtimeErrors));
                Finish(report.checks.All(check => check.passed));
            }
        }

        IEnumerator Verify()
        {
            Require("Rendered standalone verification", !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null,
                "Run the player with graphics enabled and omit -batchmode/-nographics so screenshots include the actual IMGUI.");
            Require("Controller and runtime initialized", flow && flow.Simulation && flow.sceneCamera, "GameFlowController.Start created the battlefield and simulation.");
            Require("Full roster assigned", flow.roster != null && flow.roster.Length == 52 && flow.roster.All(definition => definition && definition.prefab), "52 army definitions with prefab references.");
            yield return new WaitForSecondsRealtime(.7f);
            Require("Startup opens the main menu", flow.State == GameFlowController.ScreenState.MainMenu, flow.State.ToString());
            yield return Capture("01-main-menu");

            flow.OpenSettings();
            Require("Settings opens from main menu", flow.State == GameFlowController.ScreenState.Settings, "Existing settings entry point.");
            flow.CloseSubpage();
            Require("Settings returns to main menu", flow.State == GameFlowController.ScreenState.MainMenu, "Return screen preserved.");
            flow.OpenManual();
            Require("Field manual opens", flow.State == GameFlowController.ScreenState.Manual, "Existing manual entry point.");
            yield return Capture("02-field-manual");
            flow.CloseSubpage();
            Require("Field manual returns to main menu", flow.State == GameFlowController.ScreenState.MainMenu, "Return screen preserved.");

            flow.OpenSetup();
            flow.Configure("Vanguard", 0);
            Require("Skirmish setup selects Vanguard", flow.State == GameFlowController.ScreenState.Setup && flow.ChosenFaction == "Vanguard" && flow.Difficulty == 0,
                "Vanguard / relaxed difficulty.");
            yield return Capture("03-skirmish-setup");
            flow.Deploy();
            yield return Await(() => flow.State == GameFlowController.ScreenState.Playing, 8, "Vanguard deploy reaches the playable match");
            var sim = flow.Simulation;
            Require("Opposing headquarters deployed", sim.PlayerHQ && sim.EnemyHQ && sim.PlayerHQ.IsAlive && sim.EnemyHQ.IsAlive &&
                sim.PlayerHQ.Definition.faction == "Vanguard" && sim.EnemyHQ.Definition.faction == "Dynasty", "Vanguard player versus Dynasty opponent.");
            Require("Starting army contains builder and scout", FindUnit(0, "Worker") && FindUnit(0, "Scout"), "Both playable RTS roles are present.");
            flow.SelectUnit(sim.PlayerHQ);
            yield return new WaitForSecondsRealtime(.45f);
            yield return Capture("04-vanguard-match");

            flow.PauseMatch();
            Require("Pause stops the simulation", flow.State == GameFlowController.ScreenState.Paused && !sim.Running && Mathf.Approximately(Time.timeScale, 0), "Paused state and zero timescale.");
            float pausedElapsed = sim.Elapsed;
            int pausedCredits = sim.Credits;
            yield return Capture("05-paused");
            flow.OpenSettings();
            yield return new WaitForSecondsRealtime(.2f);
            flow.CloseSubpage();
            Require("Paused settings returns to pause", flow.State == GameFlowController.ScreenState.Paused && !sim.Running, "Match remains paused while browsing settings.");
            flow.OpenManual();
            yield return new WaitForSecondsRealtime(.2f);
            flow.CloseSubpage();
            Require("Paused manual returns to pause", flow.State == GameFlowController.ScreenState.Paused && !sim.Running, "Match remains paused while browsing the field manual.");
            yield return new WaitForSecondsRealtime(.3f);
            Require("Paused economy and elapsed time stay frozen", Mathf.Abs(sim.Elapsed - pausedElapsed) < .001f && sim.Credits == pausedCredits,
                $"Elapsed {pausedElapsed:0.000} -> {sim.Elapsed:0.000}; credits {pausedCredits} -> {sim.Credits}.");
            flow.ResumeMatch();
            Require("Resume restores play", flow.State == GameFlowController.ScreenState.Playing && sim.Running && Mathf.Approximately(Time.timeScale, 1), "Normal simulation resumes.");
            Time.timeScale = Acceleration;
            int creditsBeforeIncome = sim.Credits;
            yield return Await(() => sim.Credits > creditsBeforeIncome, 6, "Resource economy produces actual income");
            Check("Income increases spendable credits", sim.Credits > creditsBeforeIncome, $"{creditsBeforeIncome} -> {sim.Credits} credits.");

            // Start from the real deployed scout and use the production movement/pathfinding code.
            var scout = FindUnit(0, "Scout");
            var crossingTarget = new Vector3(42, 0, 6);
            crossingTarget.y = DemoBattlefield.GroundHeight(crossingTarget);
            flow.SelectUnit(scout);
            sim.MoveUnits(new[] { scout }, crossingTarget);
            float crossingDeadline = Time.realtimeSinceStartup + 12;
            bool stayedWalkable = true, crossedRiver = false, capturedBridge = false;
            int movementSamples = 0;
            Vector3 crossingStart = scout.Position;
            while (scout && scout.IsAlive && Vector3.Distance(scout.Position, crossingTarget) > 4 && Time.realtimeSinceStartup < crossingDeadline)
            {
                movementSamples++;
                if (!DemoBattlefield.IsWalkable(scout.Position, scout.Radius)) stayedWalkable = false;
                float riverDistance = Mathf.Abs(scout.Position.x - DemoBattlefield.RiverCenter(scout.Position.z));
                if (riverDistance < DemoBattlefield.RiverHalfWidth) crossedRiver = true;
                if (!capturedBridge && scout.Position.x > -26)
                {
                    capturedBridge = true;
                    Time.timeScale = 0;
                    flow.FocusCamera(new Vector3(DemoBattlefield.RiverCenter(6), 0, 6), 35);
                    yield return new WaitForSecondsRealtime(.5f);
                    yield return Capture("06-concrete-bridge");
                    Time.timeScale = Acceleration;
                }
                yield return null;
            }
            Require("Ground scout reaches the opposite river bank", scout && scout.IsAlive && Vector3.Distance(scout.Position, crossingTarget) <= 4,
                scout ? $"Scout moved from {crossingStart} to {scout.Position}; destination {crossingTarget}." : "Scout was destroyed before crossing.");
            Require("River crossing remains on walkable ground", stayedWalkable && crossedRiver && movementSamples > 5,
                $"{movementSamples} actual movement samples, entered river corridor={crossedRiver}, all footprint samples walkable={stayedWalkable}.");
            sim.StopUnits(new[] { scout });

            var producer = FindUnit(0, "Barracks") ?? sim.PlayerHQ;
            var recruit = sim.GetAvailableProduction(producer).FirstOrDefault(definition => definition.tier == 1 && string.IsNullOrEmpty(sim.GetRequirement(definition)));
            Require("A starting producer offers recruitable units", producer && recruit, producer ? producer.Definition.displayName : "No producer.");
            int recruitCountBefore = CountUnits(0, recruit.id);
            int creditsBeforeQueue = sim.Credits;
            Require("Queue accepts an affordable recruit", sim.QueueUnit(producer, recruit), recruit.displayName);
            Require("Production charges the recruit cost", sim.Credits == creditsBeforeQueue - recruit.cost, $"{creditsBeforeQueue} - {recruit.cost} = {sim.Credits}.");
            Require("Recruit enters the production queue", producer.Production.Any(order => order.Definition == recruit), "Real production order present.");

            var worker = FindUnit(0, "Worker");
            var buildingDefinition = flow.roster.First(definition => definition.id == "Vanguard_Power");
            var riverPosition = new Vector3(DemoBattlefield.RiverCenter(30), 0, 30);
            int beforeRiverCount = sim.Units.Count;
            int beforeRiverCredits = sim.Credits;
            bool canBuildInRiver = sim.CanBuild(worker, buildingDefinition, riverPosition, out string riverReason);
            bool placedInRiver = sim.PlaceBuilding(worker, buildingDefinition, riverPosition);
            Require("River construction is rejected without spending", !canBuildInRiver && !placedInRiver && sim.Units.Count == beforeRiverCount && sim.Credits == beforeRiverCredits,
                "Placement refusal: " + riverReason);

            var validPosition = FindBuildPosition(worker, buildingDefinition);
            int countBeforeFoundation = sim.Units.Count;
            Require("Worker places a valid foundation", sim.PlaceBuilding(worker, buildingDefinition, validPosition), validPosition.ToString());
            var foundation = sim.Units.FirstOrDefault(unit => unit && unit.Team == 0 && unit.Definition == buildingDefinition && Vector3.Distance(unit.Position, validPosition) < 3);
            Require("Construction creates an unfinished foundation", foundation && foundation.BuildProgress < 1 && sim.Units.Count == countBeforeFoundation + 1,
                foundation ? "Build progress: " + foundation.BuildProgress.ToString("0.00") : "No foundation created.");
            flow.SelectUnit(worker);
            flow.FocusCamera(validPosition, 43);
            yield return Await(() => CountUnits(0, recruit.id) > recruitCountBefore, 12, "Queued production spawns an actual recruit");
            Require("Completed recruit leaves its production queue", !producer.Production.Any(order => order.Definition == recruit), "The one queued recruit was produced.");
            var deployedRecruit = sim.Units.Last(unit => unit && unit.Team == 0 && unit.Definition == recruit);
            Require("Recruit deploys through the modeled barracks exit", deployedRecruit.EgressRemaining > 0 &&
                producer.GetComponentsInChildren<Transform>().Any(t => t.name == "ProductionExit"), "Live production uses the imported exit and deployment lane.");
            sim.StopUnits(new[] { deployedRecruit });
            yield return Await(() => deployedRecruit.EgressRemaining <= 0, 5, "Halted recruit safely clears its production doorway");
            Require("Halt during deployment does not restart the rally order", !deployedRecruit.HasMoveOrder &&
                Vector3.Distance(deployedRecruit.Position, deployedRecruit.EgressTo) < .6f, "The recruit finishes the exit lane and guards there.");
            yield return Await(() => foundation && foundation.IsAlive && foundation.BuildProgress >= 1, 15, "Worker completes the placed foundation");
            Require("Completed structure has live health", foundation.Health > 0 && foundation.IsStructure, foundation.Definition.displayName);
            Time.timeScale = 1;
            yield return Capture("07-completed-construction");

            // Position an existing opposing scout as a deterministic combat fixture, then issue a real attack.
            // No health, weapon damage, cooldown or combat result is injected for this damage check.
            var enemyScout = FindUnit(1, "Scout");
            Require("Combat fixture has opposing scouts", scout && scout.IsAlive && enemyScout && enemyScout.IsAlive, "Existing player and enemy scouts.");
            sim.StopUnits(new[] { scout, enemyScout });
            Vector3 attackerPosition = new Vector3(-62, 0, -82), targetPosition = new Vector3(-54, 0, -82);
            attackerPosition.y = DemoBattlefield.GroundHeight(attackerPosition);
            targetPosition.y = DemoBattlefield.GroundHeight(targetPosition);
            scout.transform.position = attackerPosition;
            enemyScout.transform.position = targetPosition;
            float healthBeforeCombat = enemyScout.Health;
            // Teleported test fixtures need one reconnaissance refresh before issuing a visible-target order.
            yield return Await(() => sim.IsVisible(enemyScout), 5, "Relocated combat fixture enters allied sight");
            sim.AttackUnits(new[] { scout }, enemyScout);
            Debug.Log($"COMBAT PROBE: order={scout.CurrentOrder}; explicit={scout.ExplicitAttack}; target={scout.Target}; pos={scout.Position}; cooldown={scout.WeaponCooldown}; visible={sim.IsVisible(enemyScout)}; dt={Time.deltaTime}");
            Time.timeScale = Acceleration;
            float combatDeadline = Time.realtimeSinceStartup + 5;
            while (enemyScout && enemyScout.Health >= healthBeforeCombat && Time.realtimeSinceStartup < combatDeadline)
            {
                yield return new WaitForSecondsRealtime(.5f);
                Debug.Log($"COMBAT PROBE TICK: elapsed={sim.Elapsed}; order={scout.CurrentOrder}; explicit={scout.ExplicitAttack}; target={scout.Target}; cooldown={scout.WeaponCooldown}; visible={sim.IsVisible(enemyScout)}; shots={sim.VisualEffects.ActiveProjectiles}; impacts={sim.VisualEffects.ImpactCount}; targetHP={enemyScout.Health}; dt={Time.deltaTime}; from={scout.Position}; to={enemyScout.Position}");
            }
            Require("Issued attack deals actual weapon damage", !enemyScout || enemyScout.Health < healthBeforeCombat, "Live combat and projectile damage.");
            Check("Combat lowers opposing health", !enemyScout || enemyScout.Health < healthBeforeCombat,
                $"Enemy scout health {healthBeforeCombat:0.0} -> {(enemyScout ? enemyScout.Health : 0):0.0}.");
            Time.timeScale = 1;

            yield return VerifyBridgeRoutesAndOpponent();

            var previousPlayerHQ = sim.PlayerHQ;
            sim.EnemyHQ.ApplyDamage(sim.EnemyHQ.MaxHealth * 2, scout);
            yield return Await(() => flow.State == GameFlowController.ScreenState.Victory, 3, "Enemy headquarters destruction opens victory");
            Require("Victory stops the match", !sim.Running && Mathf.Approximately(Time.timeScale, 0), "Result state freezes simulation.");
            yield return Capture("08-victory");

            flow.Deploy();
            yield return Await(() => flow.State == GameFlowController.ScreenState.Playing, 8, "Play again restarts after victory");
            Require("Rematch replaces the old army and headquarters", sim.PlayerHQ && sim.PlayerHQ != previousPlayerHQ && sim.PlayerHQ.IsAlive && sim.EnemyHQ.IsAlive && sim.Elapsed < 3,
                "New live headquarters and fresh match timer.");
            sim.PlayerHQ.ApplyDamage(sim.PlayerHQ.MaxHealth * 2, sim.EnemyHQ);
            yield return Await(() => flow.State == GameFlowController.ScreenState.Defeat, 3, "Player headquarters destruction opens defeat");
            Require("Defeat stops the match", !sim.Running && Mathf.Approximately(Time.timeScale, 0), "Result state freezes simulation.");
            yield return Capture("09-defeat");
            yield return ReturnAndCheckCleanup("Defeat return to menu clears the army");

            flow.OpenSetup();
            flow.Configure("Dynasty", 1);
            flow.Deploy();
            yield return Await(() => flow.State == GameFlowController.ScreenState.Playing, 8, "Dynasty faction deploys successfully");
            Require("Dynasty reverses the faction matchup", sim.PlayerHQ && sim.EnemyHQ && sim.PlayerHQ.Definition.faction == "Dynasty" && sim.EnemyHQ.Definition.faction == "Vanguard",
                "Dynasty player versus Vanguard opponent.");
            flow.SelectUnit(FindUnit(0, "Factory") ?? sim.PlayerHQ);
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("10-dynasty-match");
            var dynastyHQ = sim.PlayerHQ;
            flow.Deploy();
            yield return Await(() => flow.State == GameFlowController.ScreenState.Playing, 8, "A running skirmish can restart cleanly");
            Require("Restart preserves faction and resets match state", sim.PlayerHQ && sim.PlayerHQ != dynastyHQ && sim.PlayerHQ.Definition.faction == "Dynasty" && sim.Elapsed < 3 && flow.Selection.Count == 0,
                "New Dynasty army, fresh timer, empty selection.");
            yield return VerifyMilitarySystems();
            yield return VerifyAirfieldOperations();
            yield return ReturnAndCheckCleanup("Final return to menu clears all runtime units");
            yield return Capture("11-returned-main-menu");
        }

        IEnumerator VerifyMilitarySystems()
        {
            var sim = flow.Simulation;
            Require("Starting base has a working power budget", sim.PowerGenerated() == 30 && sim.PowerConsumed() == 15 && sim.HasPower(),
                $"Power {sim.PowerGenerated()}/{sim.PowerConsumed()}.");
            Require("Enemy headquarters begins concealed by fog", !sim.IsVisible(sim.EnemyHQ) && sim.IsVisible(sim.PlayerHQ) && sim.FogTexture,
                "Sight is shared by allies and hidden enemy objects remain in the simulation.");
            var barracks = FindUnit(0, "Barracks");
            var rifle = flow.roster.First(d => d.faction == sim.PlayerFaction && SkirmishSimulation.DefinitionKey(d) == "Rifle");
            int cash = sim.Credits;
            Require("Queue accepts a unit for cancellation", sim.QueueUnit(barracks, rifle), "Real production API.");
            Require("Cancelling production refunds its full cost", sim.CancelProduction(barracks, 0) && sim.Credits == cash && barracks.Production.Count == 0,
                "Cancelled before production advanced.");
            Require("Invalid production cancellation is harmless", !sim.CancelProduction(barracks, 0) && sim.Credits == cash, "No double refund.");
            var scout = FindUnit(0, "Scout"); Vector3 origin = scout.Position;
            scout.transform.position = sim.EnemyHQ.Position + Vector3.left * 12;
            sim.StopUnits(new[] { scout });
            yield return new WaitForSeconds(.3f);
            Require("Reconnaissance reveals the enemy headquarters", sim.IsVisible(sim.EnemyHQ), "Move an actual scout into sight range.");
            scout.transform.position = origin; sim.StopUnits(new[] { scout });
            yield return new WaitForSeconds(.3f);
            Require("Lost vision conceals enemy headquarters again", !sim.IsVisible(sim.EnemyHQ), "Exploration persists but enemy objects are hidden.");

            var tank = FindUnit(0, "Tank"); var enemyTank = FindUnit(1, "Tank");
            Vector3 a = new Vector3(-64, 0, -82), b = new Vector3(-45, 0, -80);
            a.y = DemoBattlefield.GroundHeight(a); b.y = DemoBattlefield.GroundHeight(b);
            tank.transform.position = a; enemyTank.transform.position = b;
            sim.StopUnits(new[] { tank, enemyTank });
            yield return new WaitForSeconds(.3f);
            float before = enemyTank.Health; int impacts = sim.VisualEffects.ImpactCount;
            sim.VisualEffects.Fire(tank, enemyTank, 30, 0);
            Require("A visible shell exists before its damage arrives", sim.VisualEffects.ActiveProjectiles > 0 && enemyTank.Health == before, "Projectile damage is deferred until impact.");
            yield return Await(() => sim.VisualEffects.ImpactCount > impacts && enemyTank.Health < before, 3, "Shell impact applies damage and emits effects");
            flow.SelectUnit(tank); flow.FocusCamera((a + b) * .5f, 27);
            yield return Await(() => Mathf.Abs(flow.sceneCamera.orthographicSize - 27) < .5f && Vector3.Distance(flow.CameraFocus, (a + b) * .5f) < 1, 5, "Combat inspection camera settles");
            sim.VisualEffects.Destruction(enemyTank);
            yield return new WaitForSeconds(.12f);
            yield return Capture("12-combat-fidelity");
            enemyTank.ApplyDamage(enemyTank.MaxHealth * 2, tank);
            Require("Confirmed kills award veteran experience", tank.Experience > 0, "Experience comes from a defeated opposing unit.");

            var worker = FindUnit(0, "Worker");
            var techDefinition = flow.roster.First(d => d.faction == sim.PlayerFaction && SkirmishSimulation.DefinitionKey(d) == "Tech");
            var techPosition = FindBuildPosition(worker, techDefinition);
            Require("Technology center foundation is accepted", sim.PlaceBuilding(worker, techDefinition, techPosition), techPosition.ToString());
            var tech = FindUnit(0, "Tech");
            Time.timeScale = Acceleration;
            yield return Await(() => tech && tech.BuildProgress >= 1, 18, "Technology center completes through worker construction");
            yield return Await(() => sim.Credits >= sim.UpgradeCost("Armor"), 8, "Economy funds the armor upgrade");
            Time.timeScale = 1;
            float hp = tank.MaxHealth;
            Require("Armor research applies to existing troops", sim.PurchaseUpgrade("Armor") && Mathf.Approximately(tank.MaxHealth, hp * 1.25f), "Existing troop max health increases by 25%.");
            cash = sim.Credits;
            Require("Research cannot be purchased twice", !sim.PurchaseUpgrade("Armor") && sim.Credits == cash && sim.HasUpgrade("Armor"), "No repeat spending or stacking.");
            flow.SelectUnit(tech); flow.FocusCamera(tech.Position, 38);
            yield return new WaitForSeconds(.45f);
            yield return Capture("13-research-command");
            var power = FindUnit(0, "Power"); power.ApplyDamage(power.MaxHealth * 2);
            Require("Destroying power causes a real shortage", !sim.HasPower() && sim.PowerGenerated() == 0 && sim.PowerConsumed() > 0,
                "Infrastructure destruction changes the simulated power budget.");
            Require("Research requires a powered base", sim.UpgradeRequirement("Weapons").Contains("power"), sim.UpgradeRequirement("Weapons"));
            Require("Queue can retain work during power shortage", sim.QueueUnit(barracks, rifle), "Barracks production is slowed rather than discarded.");
            float remaining = barracks.Production[0].Remaining, start = sim.Elapsed;
            yield return new WaitForSeconds(1);
            float delta = remaining - barracks.Production[0].Remaining;
            Require("Low power slows production to thirty percent", Mathf.Abs(delta - (sim.Elapsed - start) * .3f) < .1f, $"Progress={delta:0.000}; elapsed={sim.Elapsed - start:0.000}.");
        }

        IEnumerator VerifyAirfieldOperations()
        {
            flow.Deploy();
            yield return Await(() => flow.State == GameFlowController.ScreenState.Playing, 8, "Fresh match for airfield operations");
            var sim = flow.Simulation;
            ArmyDefinition Definition(string key) => flow.roster.First(d => d.faction == sim.PlayerFaction && SkirmishSimulation.DefinitionKey(d) == key);
            // Completed infrastructure is a documented fixture; all plane funding, queues,
            // production, slot assignment, flight and service use the real runtime systems.
            var airfield = sim.Spawn(Definition("Airfield"), 0, new Vector3(-73, 0, 57));
            sim.Spawn(Definition("Tech"), 0, new Vector3(-133, 0, 45));
            var fighter = Definition("Fighter");
            Require("Airfield model carries four physical pad anchors", airfield.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("AircraftPad")) == 4,
                "Imported Blender attachment points used for actual parking.");
            Time.timeScale = Acceleration;
            yield return Await(() => sim.Credits >= fighter.cost * 4, 15, "Supply economy funds four aircraft");
            for (int i = 0; i < 4; i++) Require("Queue reserves plane slot " + (i + 1), sim.QueueUnit(airfield, fighter), "Queues and live aircraft share four slots.");
            Require("Four queued planes prevent a fifth order", !sim.CanQueueUnit(airfield, fighter, out var fullReason) && fullReason.Contains("four plane slots"), fullReason);
            Require("Cancelling a plane releases its reserved slot", sim.CancelProduction(airfield, 3) && sim.AirfieldSlotsUsed(airfield) == 3, "Slot reservation refunded with order.");
            Require("Freed plane slot can be queued again", sim.QueueUnit(airfield, fighter), "Fourth slot re-reserved.");
            yield return Await(() => sim.Units.Count(u => u && u.IsAlive && u.HomeAirfield == airfield) == 4, 27, "Four actual planes finish production at the airfield");
            Time.timeScale = 1;
            var planes = sim.Units.Where(u => u && u.IsAlive && u.HomeAirfield == airfield).ToArray();
            Require("Four planes occupy distinct physical parking pads", planes.Select(p => p.AirfieldSlot).Distinct().Count() == 4 &&
                planes.All(p => Vector3.Distance(p.Position, sim.AirfieldPadPosition(airfield, p.AirfieldSlot)) < .6f), "Each finished aircraft parks at its own exported model anchor.");
            flow.SelectUnit(airfield); flow.FocusCamera(airfield.Position, 36);
            yield return Await(() => Mathf.Abs(flow.sceneCamera.orthographicSize - 36) < .5f && Vector3.Distance(flow.CameraFocus, airfield.Position) < 1, 5, "Airfield inspection camera settles");
            yield return Capture("14-four-plane-airfield");
            var plane = planes[0]; var pad = sim.AirfieldPadPosition(airfield, plane.AirfieldSlot);
            sim.MoveUnits(new[] { plane }, airfield.Position + new Vector3(0, 0, -43));
            Time.timeScale = Acceleration;
            yield return Await(() => Vector3.Distance(plane.Position, pad) > 15 && plane.Altitude > 5, 5, "Ordered plane launches from its parking pad");
            sim.StopUnits(new[] { plane });
            sim.StopUnits(new[] { plane });
            yield return Await(() => !plane.ReturningToAirfield && Vector3.Distance(plane.Position, pad) < .6f, 8, "Stopping a returning aircraft still allows it to land");
            plane.AircraftAmmo = 2; // Simulate a partly spent sortie to isolate rearming infrastructure behavior.
            sim.StopUnits(new[] { plane });
            yield return Await(() => plane.RearmRemaining > 0, 3, "Partly spent aircraft begins rearming on its pad");
            airfield.ApplyDamage(airfield.MaxHealth * 2);
            float until = sim.Elapsed + 10;
            yield return Await(() => sim.Elapsed >= until, 4, "Advance beyond normal rearming time after airfield destruction");
            Require("Destroyed airfields cannot rearm surviving aircraft", plane.AircraftAmmo == 2 && plane.RearmRemaining == 0 && !plane.HomeAirfield,
                "No healing or ammunition regeneration without a surviving airfield.");
            Time.timeScale = 1;
        }

        IEnumerator VerifyBridgeRoutesAndOpponent()
        {
            var sim = flow.Simulation;
            var tank = FindUnit(0, "Tank");
            Require("Wider ground-unit navigation fixture exists", tank && tank.IsAlive, "Use the actual deployed tank and its full navigation radius.");
            Vector3 originalPosition = tank.Position;
            float started = Time.realtimeSinceStartup;
            float deadline = started + 20f;
            float startingElapsed = sim.Elapsed, crossingElapsed = 0;
            bool opponentCrossed = false, opponentStayedWalkable = true;
            string crossingUnit = "none";
            var easternUnits = new HashSet<int>();

            // Track only units actually observed in the eastern base. The earlier scout combat
            // fixture was repositioned to the west and cannot satisfy this natural-assault check.
            void ObserveOpponent()
            {
                foreach (RtsUnit unit in sim.Units)
                {
                    if (!unit || !unit.IsAlive || unit.Team != 1 || unit.IsStructure || unit.IsAircraft || unit.IsWorker) continue;
                    int id = unit.GetInstanceID();
                    if (unit.Position.x > 50) easternUnits.Add(id);
                    if (!easternUnits.Contains(id)) continue;
                    if (!DemoBattlefield.IsWalkable(unit.Position, unit.Radius)) opponentStayedWalkable = false;
                    if (!opponentCrossed && unit.Position.x + unit.Radius <
                        DemoBattlefield.RiverCenter(unit.Position.z) - DemoBattlefield.RiverHalfWidth)
                    {
                        opponentCrossed = true;
                        crossingElapsed = sim.Elapsed;
                        crossingUnit = unit.Definition.displayName;
                    }
                }
            }

            Time.timeScale = Acceleration;
            ObserveOpponent();
            foreach (float bridgeZ in DemoBattlefield.BridgeZ)
            {
                Require("Tank survives to test crossing at z=" + bridgeZ, tank && tank.IsAlive, "Each crossing uses a live tank.");
                Vector3 origin = new Vector3(-40, 0, bridgeZ);
                Vector3 destination = new Vector3(40, 0, bridgeZ);
                origin.y = DemoBattlefield.GroundHeight(origin);
                destination.y = DemoBattlefield.GroundHeight(destination);
                tank.transform.position = origin;
                sim.StopUnits(new[] { tank });
                sim.MoveUnits(new[] { tank }, destination);
                bool stayedWalkable = true, enteredRiver = false;
                int samples = 0;
                while (tank && tank.IsAlive && Vector3.Distance(tank.Position, destination) > 2.5f &&
                    Time.realtimeSinceStartup < deadline)
                {
                    samples++;
                    if (!DemoBattlefield.IsWalkable(tank.Position, tank.Radius)) stayedWalkable = false;
                    if (Mathf.Abs(tank.Position.x - DemoBattlefield.RiverCenter(tank.Position.z)) < DemoBattlefield.RiverHalfWidth)
                        enteredRiver = true;
                    ObserveOpponent();
                    yield return null;
                }
                bool arrived = tank && tank.IsAlive && Vector3.Distance(tank.Position, destination) <= 2.5f;
                Require("Tank crosses concrete bridge at z=" + bridgeZ,
                    arrived && enteredRiver && stayedWalkable && samples > 5,
                    $"Reached east bank={arrived}; entered river corridor={enteredRiver}; full radius walkable={stayedWalkable}; samples={samples}; radius={tank.Radius:0.00}.");
                sim.StopUnits(new[] { tank });
            }
            tank.transform.position = originalPosition;
            sim.StopUnits(new[] { tank });

            // The difficulty timer, economic production and attack orders are never injected.
            // Existing match time continues during the bridge sweep, keeping this phase bounded.
            while (!opponentCrossed && sim.Running && Time.realtimeSinceStartup < deadline)
            {
                ObserveOpponent();
                yield return null;
            }
            ObserveOpponent();
            Require("Enemy first assault naturally crosses onto the western bank", opponentCrossed && opponentStayedWalkable,
                $"Observed {easternUnits.Count} enemy ground units leaving their actual eastern base; first crossing={crossingUnit} at {crossingElapsed:0.0}s; " +
                $"walkable footprints={opponentStayedWalkable}; match time {startingElapsed:0.0} to {sim.Elapsed:0.0}s; extra wall time={Time.realtimeSinceStartup - started:0.0}s.");
            Time.timeScale = 1;
        }

        IEnumerator ReturnAndCheckCleanup(string checkName)
        {
            flow.ReturnToMenu();
            yield return null;
            yield return null;
            Require(checkName, flow.State == GameFlowController.ScreenState.MainMenu && !flow.Simulation.Running && flow.Simulation.Units.Count == 0 &&
                flow.Selection.Count == 0 && FindObjectsByType<RtsUnit>(FindObjectsSortMode.None).Length == 0 && Mathf.Approximately(Time.timeScale, 1),
                "No remaining RtsUnit objects, simulation entries, selected units or paused timescale.");
        }

        Vector3 FindBuildPosition(RtsUnit worker, ArmyDefinition definition)
        {
            // Search around the deployed construction rig, using the actual placement validator.
            foreach (float distance in new[] { 18f, 26f, 34f, 42f, 50f, 60f })
                for (int direction = 0; direction < 16; direction++)
                {
                    float angle = direction * Mathf.PI * 2 / 16;
                    var position = worker.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                    position.y = DemoBattlefield.GroundHeight(position);
                    if (flow.Simulation.CanBuild(worker, definition, position, out _)) return position;
                }
            throw new InvalidOperationException("The deployed builder has no valid affordable construction position in the surrounding field.");
        }

        RtsUnit FindUnit(int team, string key) => flow.Simulation.Units.FirstOrDefault(unit => unit && unit.IsAlive && unit.Team == team && unit.Key == key);
        int CountUnits(int team, string definitionId) => flow.Simulation.Units.Count(unit => unit && unit.IsAlive && unit.Team == team && unit.Definition.id == definitionId);

        IEnumerator Await(Func<bool> condition, float timeout, string name)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(name, condition(), "Timeout budget: " + timeout.ToString("0") + " real seconds.");
        }

        IEnumerator Capture(string name)
        {
            const int maxAttempts = 4;
            string lastFailure = "Framebuffer unavailable.";
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                yield return new WaitForEndOfFrame();
                Texture2D texture = null;
                bool captured = false;
                try
                {
                    texture = ScreenCapture.CaptureScreenshotAsTexture();
                    if (!texture || texture.width < 640 || texture.height < 360)
                        throw new InvalidOperationException("Framebuffer unavailable or too small for review.");
                    FrameMetrics metrics = MeasureFrame(texture);
                    lastFailure = metrics.ToString();
                    if (metrics.Nonblank)
                    {
                        SaveCapture(texture, name + ".png", "full-frame", true, attempt, metrics);
                        captured = true;
                    }
                }
                catch (Exception exception) { lastFailure = exception.Message; }
                finally { if (texture) Destroy(texture); }
                if (captured)
                {
                    Check("Visible full-frame capture: " + name, true, "Includes live interface; " + lastFailure);
                    yield break;
                }
                if (attempt < maxAttempts) yield return new WaitForSecondsRealtime(.3f);
            }

            // Hidden swapchains can be blank while IMGUI still repaints. Render the same live UI
            // into a world render target and prove that the interface changed the pixels.
            bool composited = false;
            yield return CaptureOffscreenInterface(name, result => composited = result);
            if (composited) yield break;
            // A useful world render must never turn an unavailable interface screenshot into a pass.
            Check("Visible full-frame capture: " + name, false,
                "Framebuffer remained blank/unavailable after four attempts. Activate the player window before verification. " + lastFailure);
            try { CaptureWorldFallback(name); }
            catch (Exception exception) { Check("World-only fallback capture: " + name, false, exception.Message); }
        }

        IEnumerator CaptureOffscreenInterface(string name, Action<bool> completed)
        {
            var target = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var world = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            var composite = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(flow.sceneCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target; world.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); world.Apply();
                RenderTexture.active = previous;
                if (name == "04-vanguard-match") CapturePortraitSources();
                int serial = flow.VerificationRepaintSerial;
                flow.VerificationFrameTarget = target;
                bool rendered = OffscreenGuiRenderer.TryRender(target, flow.DrawVerificationInterface, out string diagnostic);
                Debug.Log("OFFSCREEN INTERFACE: " + diagnostic);
                if (!rendered || flow.VerificationRepaintSerial == serial) { completed(false); yield break; }
                RenderTexture.active = target; composite.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); composite.Apply();
                int changed = 0, samples = 0;
                for (int y = 12; y < target.height; y += 17) for (int x = 12; x < target.width; x += 23)
                {
                    if (flow.State == GameFlowController.ScreenState.Playing && y > target.height * .3f && y < target.height * .93f) continue;
                    Color delta = world.GetPixel(x, y) - composite.GetPixel(x, y);
                    if (Mathf.Abs(delta.r) + Mathf.Abs(delta.g) + Mathf.Abs(delta.b) > .06f) changed++;
                    samples++;
                }
                var metrics = MeasureFrame(composite);
                bool passed = metrics.Nonblank && changed > samples * .06f;
                Debug.Log($"OFFSCREEN PROBE: serial={flow.VerificationRepaintSerial}; changed={changed}/{samples}; {metrics}");
                if (passed)
                {
                    SaveCapture(composite, name + ".png", "offscreen world + actual IMGUI", true, 1, metrics);
                    Check("Visible offscreen interface capture: " + name, true, $"Same OnGUI Repaint draws into the camera target; changed {changed}/{samples} interface-region samples versus world alone.");
                }
                completed(passed);
            }
            finally
            {
                flow.VerificationFrameTarget = null;
                RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(world); Destroy(composite);
            }
        }

        void CapturePortraitSources()
        {
            var studio = flow.GetComponent<TacticalPortraitRenderer>();
            if (!studio) return;
            RenderTexture previous = RenderTexture.active;
            foreach (string id in new[] { "Vanguard_Tank", "Vanguard_Fighter", "Vanguard_Command" })
            {
                var definition = flow.roster.FirstOrDefault(d => d.id == id);
                var source = studio.Get(definition) as RenderTexture;
                if (!source) continue;
                var pixels = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
                try
                {
                    RenderTexture.active = source;
                    pixels.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); pixels.Apply();
                    File.WriteAllBytes(Path.Combine(outputDirectory, "portrait-source-" + id + ".png"), pixels.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; Destroy(pixels); }
            }
        }

        void CaptureWorldFallback(string name)
        {
            // Same explicit URP GPU-render path used by ArmyShowcaseCapture for hidden player windows.
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(flow.sceneCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                FrameMetrics metrics = MeasureFrame(pixels);
                SaveCapture(pixels, name + "-world-only-fallback.png", "world-only-fallback", false, 1, metrics);
                Check("World-only fallback capture: " + name, metrics.Nonblank, "3D camera render only; does not include or verify the interface. " + metrics);
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Destroy(target);
                Destroy(pixels);
            }
        }

        static FrameMetrics MeasureFrame(Texture2D texture)
        {
            int samples = 0, lit = 0;
            float sum = 0, sumSquares = 0, minimum = 1, maximum = 0;
            int strideX = Mathf.Max(1, texture.width / 80), strideY = Mathf.Max(1, texture.height / 50);
            for (int y = strideY / 2; y < texture.height; y += strideY)
                for (int x = strideX / 2; x < texture.width; x += strideX)
                {
                    Color color = texture.GetPixel(x, y);
                    float brightness = color.r * .2126f + color.g * .7152f + color.b * .0722f;
                    sum += brightness;
                    sumSquares += brightness * brightness;
                    minimum = Mathf.Min(minimum, brightness);
                    maximum = Mathf.Max(maximum, brightness);
                    if (brightness > .035f) lit++;
                    samples++;
                }
            float mean = sum / Mathf.Max(1, samples);
            return new FrameMetrics
            {
                Mean = mean,
                Deviation = Mathf.Sqrt(Mathf.Max(0, sumSquares / Mathf.Max(1, samples) - mean * mean)),
                LitFraction = lit / (float)Mathf.Max(1, samples),
                Range = maximum - minimum
            };
        }

        void SaveCapture(Texture2D texture, string file, string kind, bool includesInterface, int attempts, FrameMetrics metrics)
        {
            File.WriteAllBytes(Path.Combine(outputDirectory, file), texture.EncodeToPNG());
            report.screenshots.Add(new ScreenshotResult
            {
                file = file,
                screen = flow.State.ToString(),
                captureKind = kind,
                includesInterface = includesInterface,
                nonblank = metrics.Nonblank,
                width = texture.width,
                height = texture.height,
                attempts = attempts,
                meanBrightness = metrics.Mean,
                brightnessDeviation = metrics.Deviation,
                litFraction = metrics.LitFraction
            });
            WriteReport();
        }

        void Check(string name, bool passed, string detail)
        {
            report.checks.Add(new CheckResult { name = name, passed = passed, detail = detail });
            WriteReport();
        }

        void Require(string name, bool passed, string detail)
        {
            Check(name, passed, detail);
            if (!passed) throw new InvalidOperationException(name + ": " + detail);
        }

        void ReceiveLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                report.runtimeErrors.Add(type + ": " + message + "\n" + stackTrace);
        }

        void Update()
        {
            if (!active || finished || Time.realtimeSinceStartup - startedAt < WatchdogSeconds + startDelaySeconds) return;
            report.failure = "Runtime verification exceeded its 180 second wall-clock watchdog. If screenshots stalled, run the rendered player without -batchmode or -nographics.";
            Finish(false);
        }

        void Finish(bool success)
        {
            if (finished) return;
            finished = true;
            Application.logMessageReceived -= ReceiveLog;
            Time.timeScale = 1;
            report.status = success ? "passed" : "failed";
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.wallSeconds = Time.realtimeSinceStartup - startedAt;
            try { WriteReport(); }
            catch (Exception exception) { Debug.LogError("Could not write verification report: " + exception); success = false; }
            Debug.Log("NEON FRONTIER VERIFICATION: " + report.status.ToUpperInvariant() + " / " + report.checks.Count + " checks / " + outputDirectory + " / " + report.failure);
            enabled = false;
            if (!Application.isEditor) Application.Quit(success ? 0 : 1);
        }

        void WriteReport()
        {
            if (string.IsNullOrEmpty(outputDirectory) || report == null) return;
            report.wallSeconds = Time.realtimeSinceStartup - startedAt;
            File.WriteAllText(Path.Combine(outputDirectory, "verification-report.json"), JsonUtility.ToJson(report, true));
            var text = new StringBuilder();
            text.AppendLine("NEON FRONTIER / RUNTIME VERIFICATION");
            text.AppendLine("Status: " + report.status);
            text.AppendLine("Started: " + report.startedUtc);
            text.AppendLine("Wall time: " + report.wallSeconds.ToString("0.0") + " seconds");
            text.AppendLine("Window activation delay: " + report.startDelaySeconds.ToString("0.0") + " seconds");
            foreach (var check in report.checks) text.AppendLine((check.passed ? "PASS " : "FAIL ") + check.name + " — " + check.detail);
            foreach (var screenshot in report.screenshots) text.AppendLine("IMAGE " + screenshot.file + " — " + screenshot.screen + " / " + screenshot.width + " × " + screenshot.height +
                " / " + screenshot.captureKind + " / nonblank=" + screenshot.nonblank + " / includes interface=" + screenshot.includesInterface);
            foreach (string error in report.runtimeErrors) text.AppendLine("RUNTIME ERROR " + error);
            if (!string.IsNullOrEmpty(report.failure)) text.AppendLine("FAILURE " + report.failure);
            File.WriteAllText(Path.Combine(outputDirectory, "verification-report.txt"), text.ToString());
        }

        void OnDestroy() { Application.logMessageReceived -= ReceiveLog; }
    }
}
