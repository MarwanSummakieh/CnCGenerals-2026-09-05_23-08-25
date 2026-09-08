using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonFrontier
{
    /// <summary>
    /// A self-contained RTS demo: economy, prerequisites, production, construction, bridge-aware
    /// ground movement, combat and an opponent which spends its own resources. Atlas descriptions
    /// remain concept text; transport, stealth and other original special abilities are not implied.
    /// </summary>
    public sealed class SkirmishSimulation : MonoBehaviour
    {
        public const int UnitCap = 80;
        public readonly List<RtsUnit> Units = new List<RtsUnit>(192);
        public bool Running { get; set; }
        public int Credits { get; private set; }
        public int IncomePerMinute => IncomeForTeam(0);
        public float Elapsed { get; private set; }
        public int Kills { get; private set; }
        public int Losses { get; private set; }
        public string PlayerFaction { get; private set; }
        public string EnemyFaction { get; private set; }
        public RtsUnit PlayerHQ { get; private set; }
        public RtsUnit EnemyHQ { get; private set; }
        public string Notice { get; private set; } = "";
        public BattlefieldEffects VisualEffects { get; private set; }
        BattlefieldVisibility visibility;
        public Texture2D FogTexture => visibility ? visibility.Texture : null;
        public bool IsVisible(RtsUnit unit, int team = 0) => unit && unit.IsAlive && (unit.Team == team || IsPointVisible(unit.Position, team));
        public bool IsPointVisible(Vector3 point, int team = 0) => !visibility || visibility.IsVisible(point, team);
        public event Action<bool> MatchEnded;
        readonly HashSet<string> researched = new HashSet<string>();

        const float GridStep = 4f;
        const int GridWidth = 75, GridHeight = 53;
        const int GridSize = GridWidth * GridHeight;
        readonly Dictionary<string, ArmyDefinition> definitions = new Dictionary<string, ArmyDefinition>();
        readonly List<ArmyDefinition> roster = new List<ArmyDefinition>(52);
        readonly List<RtsUnit> commandScratch = new List<RtsUnit>(UnitCap);
        readonly List<ShotEffect> effects = new List<ShotEffect>(80);
        readonly float[] routeCost = new float[GridSize];
        readonly int[] routeParent = new int[GridSize];
        readonly int[] routeVisited = new int[GridSize];
        readonly int[] routeClosed = new int[GridSize];
        readonly List<RouteEntry> routeHeap = new List<RouteEntry>(GridSize);
        readonly List<Vector3> reverseRoute = new List<Vector3>(256);
        readonly Dictionary<int, bool[]> walkabilityCache = new Dictionary<int, bool[]>();
        Transform unitsRoot, effectsRoot;
        Material friendlyMaterial, enemyMaterial, impactMaterial;
        float incomeClock, aiClock, nextAttack, noticeClock;
        int enemyCredits, difficulty, structureVersion, routeSearch, wave;
        bool ended;

        public void Initialize(ArmyDefinition[] availableRoster, string faction, int selectedDifficulty)
        {
            Clear();
            PlayerFaction = faction == "Dynasty" ? "Dynasty" : "Vanguard";
            EnemyFaction = PlayerFaction == "Vanguard" ? "Dynasty" : "Vanguard";
            difficulty = Mathf.Clamp(selectedDifficulty, 0, 2);
            if (availableRoster != null)
                foreach (ArmyDefinition definition in availableRoster)
                    if (definition != null && definition.prefab != null)
                    {
                        roster.Add(definition);
                        definitions[definition.id] = definition;
                    }
            EnsureMaterials();
            SetEffectColor(friendlyMaterial, PlayerFaction == "Vanguard" ? new Color(.1f, 1f, .86f) : new Color(1f, .34f, .1f));
            SetEffectColor(enemyMaterial, EnemyFaction == "Vanguard" ? new Color(.1f, 1f, .86f) : new Color(1f, .34f, .1f));
            unitsRoot = new GameObject("Skirmish armies").transform;
            unitsRoot.SetParent(transform, false);
            effectsRoot = new GameObject("Combat effects").transform;
            effectsRoot.SetParent(transform, false);
            VisualEffects = effectsRoot.gameObject.AddComponent<BattlefieldEffects>();
            VisualEffects.Initialize(this);
            Credits = 5000;
            enemyCredits = 5000;
            nextAttack = difficulty == 0 ? 100 : difficulty == 1 ? 75 : 55;
            SpawnBase(0);
            SpawnBase(1);
            var fog = new GameObject("Battlefield reconnaissance"); fog.transform.SetParent(effectsRoot, false);
            visibility = fog.AddComponent<BattlefieldVisibility>(); visibility.Initialize(); visibility.Tick(Units, 0, true);
            if (PlayerHQ == null || EnemyHQ == null)
            {
                SetNotice("Cannot start: one or both headquarters prefabs are missing.");
                return;
            }
            Running = true;
            SetNotice("Establish your economy, cross the concrete bridges, and destroy the enemy headquarters.");
        }

        public void Clear()
        {
            Running = false;
            foreach (RtsUnit unit in Units)
                if (unit != null) unit.gameObject.SetActive(false);
            if (unitsRoot != null) ReleaseObject(unitsRoot.gameObject);
            if (effectsRoot != null) ReleaseObject(effectsRoot.gameObject);
            unitsRoot = effectsRoot = null;
            Units.Clear();
            effects.Clear();
            researched.Clear();
            VisualEffects = null;
            visibility = null;
            roster.Clear();
            definitions.Clear();
            walkabilityCache.Clear();
            PlayerHQ = EnemyHQ = null;
            Credits = enemyCredits = Kills = Losses = wave = structureVersion = 0;
            Elapsed = incomeClock = aiClock = noticeClock = 0;
            ended = false;
            Notice = "";
        }

        void OnDestroy()
        {
            Clear();
            if (friendlyMaterial != null) ReleaseObject(friendlyMaterial);
            if (enemyMaterial != null) ReleaseObject(enemyMaterial);
            if (impactMaterial != null) ReleaseObject(impactMaterial);
        }

        void Update()
        {
            foreach (RtsUnit unit in Units) if (unit != null) unit.RefreshSelection();
            if (!Running || ended) return;
            float dt = Mathf.Min(Time.deltaTime, .1f);
            if (visibility) visibility.Tick(Units, dt);
            VisualEffects?.Tick(dt);
            Elapsed += dt;
            incomeClock += dt;
            aiClock += dt;
            if (noticeClock > 0 && (noticeClock -= dt) <= 0) Notice = "";
            if (incomeClock >= 5f)
            {
                incomeClock -= 5f;
                Credits += IncomeForTeam(0) / 12;
                enemyCredits += IncomeForTeam(1) / 12;
                // Visible difficulty pacing, bounded to less than one rifle each five seconds.
                if (difficulty == 2) enemyCredits += 60;
            }
            // Do not iterate newly produced units until next frame. Death cleanup happens afterwards.
            int count = Units.Count;
            for (int i = 0; i < count && Running; i++)
            {
                RtsUnit unit = Units[i];
                if (unit == null || !unit.IsAlive) continue;
                if (unit.BuildProgress < 1f) { TickConstruction(unit, dt); continue; }
                if (unit.EgressRemaining > 0) { TickProductionExit(unit, dt); continue; }
                TickProduction(unit, dt);
                if (unit.IsFixedWing && TickAirOperations(unit, dt)) continue;
                TickCombat(unit, dt);
                if (!unit.IsStructure) TickMovement(unit, dt);
            }
            for (int i = Units.Count - 1; i >= 0; i--)
                if (Units[i] == null || !Units[i].IsAlive) Units.RemoveAt(i);
            TickEffects(dt);
            if (aiClock >= 3f && Running) { aiClock = 0; TickOpponent(); }
        }

        void SpawnBase(int team)
        {
            float side = team == 0 ? 1 : -1;
            RtsUnit command = Spawn(Find(team, "Command"), team, new Vector3(-116 * side, 0, -39 * side));
            if (team == 0) PlayerHQ = command; else EnemyHQ = command;
            Spawn(Find(team, "Power"), team, new Vector3(-135 * side, 0, -64 * side));
            Spawn(Find(team, "Refinery"), team, new Vector3(-134 * side, 0, -14 * side));
            Spawn(Find(team, "Barracks"), team, new Vector3(-105 * side, 0, -5 * side));
            Spawn(Find(team, "Factory"), team, new Vector3(-94 * side, 0, -43 * side));
            Spawn(Find(team, "Worker"), team, new Vector3(-113 * side, 0, -66 * side));
            for (int i = 0; i < 4; i++)
                Spawn(Find(team, "Rifle"), team, new Vector3((-80 + i % 2 * 4) * side, 0, (-15 + i / 2 * 4) * side));
            for (int i = 0; i < 2; i++)
                Spawn(Find(team, "Rocket"), team, new Vector3(-85 * side, 0, (-15 + i * 4) * side));
            Spawn(Find(team, "Scout"), team, new Vector3(-73 * side, 0, -24 * side));
            Spawn(Find(team, "Tank"), team, new Vector3(-82 * side, 0, -26 * side));
        }

        ArmyDefinition Find(int team, string key)
        {
            definitions.TryGetValue((team == 0 ? PlayerFaction : EnemyFaction) + "_" + key, out ArmyDefinition result);
            return result;
        }

        public int UnitCount(int team)
        {
            int count = 0;
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == team && !unit.IsStructure) count++;
            return count;
        }

        int ReservedUnitCount(int team)
        {
            int count = UnitCount(team);
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == team) count += unit.Production.Count;
            return count;
        }

        int IncomeForTeam(int team)
        {
            int income = 0;
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == team && unit.BuildProgress >= 1)
                {
                    if (unit.Key == "Command") income += 900;
                    if (unit.Key == "Refinery") income += 1500;
                }
            return income;
        }

        public int PowerGenerated(int team = 0)
        {
            int power = 0;
            foreach (var unit in Units)
                if (unit && unit.IsAlive && unit.Team == team && unit.BuildProgress >= 1 && unit.Key == "Power") power += 30;
            return power;
        }

        public int PowerConsumed(int team = 0)
        {
            int power = 0;
            foreach (var unit in Units)
                if (unit && unit.IsAlive && unit.Team == team && unit.BuildProgress >= 1 && unit.IsStructure)
                    switch (unit.Key)
                    {
                        case "Factory": case "Airfield": power += 8; break;
                        case "Tech": case "AirDefense": power += 6; break;
                        case "Superweapon": power += 15; break;
                        case "Refinery": case "Turret": power += 4; break;
                        case "Barracks": power += 3; break;
                    }
            return power;
        }

        public bool HasPower(int team = 0) => PowerGenerated(team) >= PowerConsumed(team);
        public bool HasUpgrade(string key, int team = 0) => researched.Contains(team + ":" + key);
        public int UpgradeCost(string key) => key == "Armor" ? 1500 : key == "Weapons" ? 1800 : key == "Logistics" ? 1200 : 0;
        public string UpgradeRequirement(string key, int team = 0)
        {
            if (UpgradeCost(key) == 0) return "Unknown research";
            if (!Running) return "Match is paused";
            if (HasUpgrade(key, team)) return "Already researched";
            if (!HasBuilding(team, "Tech")) return "Requires technology center";
            if (!HasPower(team)) return "Requires sufficient power";
            return Money(team) < UpgradeCost(key) ? "Insufficient credits" : "";
        }
        public bool PurchaseUpgrade(string key, int team = 0)
        {
            string reason = UpgradeRequirement(key, team);
            if (reason.Length > 0) { if (team == 0) SetNotice(reason); return false; }
            Spend(team, UpgradeCost(key));
            researched.Add(team + ":" + key);
            if (key == "Armor")
                foreach (var unit in Units)
                    if (unit && unit.IsAlive && unit.Team == team && !unit.IsStructure)
                    { unit.MaxHealth *= 1.25f; unit.Health *= 1.25f; }
            if (team == 0) SetNotice(key + " research complete. Army upgrade applied.");
            return true;
        }

        public bool CancelProduction(RtsUnit producer, int index)
        {
            if (!Running || !producer || !producer.IsAlive || producer.Team != 0 || index < 0 || index >= producer.Production.Count) return false;
            Credits += producer.Production[index].Definition.cost;
            producer.Production.RemoveAt(index);
            producer.CurrentOrder = producer.Production.Count == 0 ? "Operational" : "Producing";
            SetNotice("Production cancelled. Credits refunded.");
            return true;
        }

        bool HasBuilding(int team, string key, bool includeConstruction = false)
        {
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == team && unit.Key == key &&
                    (includeConstruction || unit.BuildProgress >= 1)) return true;
            return false;
        }

        public List<ArmyDefinition> GetBuildOptions()
        {
            var options = new List<ArmyDefinition>(10);
            foreach (ArmyDefinition definition in roster)
                if (definition.faction == PlayerFaction && definition.category == "Structure") options.Add(definition);
            return options;
        }

        public List<ArmyDefinition> GetAvailableProduction(RtsUnit producer)
        {
            var options = new List<ArmyDefinition>(16);
            if (producer == null || !producer.IsAlive || !producer.IsStructure) return options;
            foreach (ArmyDefinition definition in roster)
                if (definition.faction == producer.Definition.faction && ProducerKey(definition) == producer.Key)
                    options.Add(definition);
            return options;
        }

        public string GetRequirement(ArmyDefinition definition, int team = 0)
        {
            if (definition == null) return "No blueprint selected";
            if (!HasBuilding(team, "Command")) return "Requires headquarters";
            string key = DefinitionKey(definition);
            if (definition.category == "Structure")
            {
                if ((key == "Factory" || key == "Turret") && !HasBuilding(team, "Barracks"))
                    return "Requires barracks";
                if ((key == "Factory" || key == "Airfield" || key == "Tech" || key == "AirDefense" || key == "Superweapon") &&
                    !HasBuilding(team, "Power")) return "Requires power plant";
                if ((key == "Airfield" || key == "Tech") && !HasBuilding(team, "Factory"))
                    return "Requires vehicle factory";
                if ((key == "AirDefense" || key == "Superweapon") && !HasBuilding(team, "Tech"))
                    return "Requires technology center";
                if (key == "Superweapon" && !HasBuilding(team, "Airfield")) return "Requires airfield (tier 3)";
            }
            else
            {
                if (definition.tier >= 2 && !HasBuilding(team, "Tech")) return "Requires technology center";
                if (definition.tier >= 3 && !HasBuilding(team, "Airfield")) return "Requires airfield (tier 3)";
            }
            return "";
        }

        static string ProducerKey(ArmyDefinition definition)
        {
            if (definition == null || definition.category == "Structure") return "";
            if (DefinitionKey(definition) == "Worker") return "Command";
            if (definition.category == "Infantry") return "Barracks";
            if (definition.category == "Aircraft") return "Airfield";
            return "Factory";
        }

        public bool CanQueueUnit(RtsUnit producer, ArmyDefinition definition, out string reason)
        {
            reason = "";
            if (!Running) reason = "Match is paused";
            else if (producer == null || !producer.IsAlive || !producer.IsStructure) reason = "Select a production building";
            else if (producer.BuildProgress < 1) reason = "Construction in progress";
            else if (definition == null || definition.prefab == null ||
                     definition.faction != producer.Definition.faction || ProducerKey(definition) != producer.Key)
                reason = "This building cannot produce that unit";
            else if (producer.Production.Count >= 5) reason = "Production queue full (5)";
            else if (producer.Key == "Airfield" && IsPlaneDefinition(definition) && AirfieldSlotsUsed(producer) >= 4)
                reason = "Airfield full: four plane slots, including queued aircraft";
            else if (ReservedUnitCount(producer.Team) >= UnitCap) reason = "Army limit reached (80, including queues)";
            else if ((reason = GetRequirement(definition, producer.Team)).Length == 0 &&
                     Money(producer.Team) < definition.cost) reason = "Insufficient credits";
            return reason.Length == 0;
        }

        static bool IsPlaneDefinition(ArmyDefinition definition) => DefinitionKey(definition) == "Fighter" || DefinitionKey(definition) == "Bomber";
        public int AirfieldSlotsUsed(RtsUnit airfield)
        {
            if (!airfield || !airfield.IsAlive || airfield.Key != "Airfield") return 0;
            int count = 0;
            foreach (var unit in Units) if (unit && unit.IsAlive && unit.IsFixedWing && unit.HomeAirfield == airfield) count++;
            foreach (var order in airfield.Production) if (IsPlaneDefinition(order.Definition)) count++;
            return count;
        }
        static Transform FacilityAnchor(RtsUnit facility, string name)
        {
            foreach (Transform child in facility.GetComponentsInChildren<Transform>()) if (child.name == name) return child;
            return null;
        }
        public Vector3 AirfieldPadPosition(RtsUnit airfield, int slot)
        {
            var anchor = FacilityAnchor(airfield, "AircraftPad" + slot);
            return anchor ? anchor.position : airfield.Position + airfield.transform.right * 5 + airfield.transform.forward * (-10 + slot * 6.7f) + Vector3.up * .4f;
        }
        bool AssignAirfield(RtsUnit plane, RtsUnit preferred = null)
        {
            foreach (var candidate in Units)
            {
                if (!candidate || !candidate.IsAlive || candidate.Team != plane.Team || candidate.Key != "Airfield" || candidate.BuildProgress < 1 || preferred && candidate != preferred) continue;
                if (AirfieldSlotsUsed(candidate) >= 4) continue;
                for (int slot = 0; slot < 4; slot++)
                {
                    bool taken = Units.Exists(u => u && u.IsAlive && u != plane && u.IsFixedWing && u.HomeAirfield == candidate && u.AirfieldSlot == slot);
                    if (taken) continue;
                    plane.HomeAirfield = candidate; plane.AirfieldSlot = slot; return true;
                }
            }
            return false;
        }
        void ReturnAircraft(RtsUnit plane)
        {
            if (!plane.HomeAirfield || !plane.HomeAirfield.IsAlive)
            {
                plane.HomeAirfield = null; plane.AirfieldSlot = -1;
                if (!AssignAirfield(plane)) { plane.CurrentOrder = "Awaiting an available airfield"; plane.Target = null; return; }
            }
            plane.ReturningToAirfield = true; plane.Target = null; plane.ExplicitAttack = plane.AttackMoving = false;
            plane.CurrentOrder = "Returning to airfield";
            SetDestination(plane, AirfieldPadPosition(plane.HomeAirfield, plane.AirfieldSlot), true);
        }
        bool TickAirOperations(RtsUnit plane, float dt)
        {
            if (!plane.HomeAirfield || !plane.HomeAirfield.IsAlive)
            {
                plane.HomeAirfield = null; plane.AirfieldSlot = -1;
                if (AssignAirfield(plane)) ReturnAircraft(plane);
                else
                {
                    plane.RearmRemaining = 0; plane.ReturningToAirfield = false;
                    plane.Altitude = Mathf.MoveTowards(plane.Altitude, 10, dt * 7);
                    var airborne = plane.Position; airborne.y = DemoBattlefield.GroundHeight(airborne) + plane.Altitude; plane.transform.position = airborne;
                    if (plane.AircraftAmmo == 0) { plane.Target = null; plane.CurrentOrder = "Awaiting an available airfield"; return true; }
                }
            }
            if (plane.ReturningToAirfield && plane.HomeAirfield)
            {
                var pad = AirfieldPadPosition(plane.HomeAirfield, plane.AirfieldSlot);
                float distance = Mathf.Sqrt(FlatDistanceSquared(plane.Position, pad));
                float desired = distance < 8 ? Mathf.Lerp(.4f, 10, distance / 8) : 10;
                plane.Altitude = Mathf.MoveTowards(plane.Altitude, desired, dt * 9);
                TickMovement(plane, dt);
                if (distance < .9f)
                {
                    plane.transform.position = Vector3.MoveTowards(plane.Position, pad, dt * 8);
                    if (Vector3.Distance(plane.Position, pad) < .25f)
                    {
                        plane.transform.position = pad; plane.transform.rotation = plane.HomeAirfield.transform.rotation;
                        plane.Altitude = .4f; plane.ReturningToAirfield = false; plane.HasMoveOrder = false; plane.Path.Clear();
                        plane.RearmRemaining = plane.AircraftAmmo < 4 ? 8 : 0;
                        plane.CurrentOrder = plane.RearmRemaining > 0 ? "Rearming at airfield" : "Parked / ready for sortie";
                    }
                }
                return true;
            }
            if (plane.RearmRemaining > 0)
            {
                plane.RearmRemaining = Mathf.Max(0, plane.RearmRemaining - dt * (HasPower(plane.Team) ? 1 : .3f));
                plane.CurrentOrder = "Rearming: " + Mathf.CeilToInt(plane.RearmRemaining) + "s";
                if (plane.RearmRemaining <= 0)
                {
                    plane.AircraftAmmo = 4; plane.Health = Mathf.Min(plane.MaxHealth, plane.Health + plane.MaxHealth * .2f);
                    plane.CurrentOrder = "Parked / ready for sortie";
                    if (plane.SortieTarget && plane.SortieTarget.IsAlive && IsVisible(plane.SortieTarget, plane.Team))
                    { plane.Target = plane.SortieTarget; plane.ExplicitAttack = true; plane.SortieTarget = null; }
                }
                return true;
            }
            if (!plane.HasMoveOrder && !plane.Target)
            {
                if (plane.Altitude > 1 && plane.HomeAirfield) ReturnAircraft(plane);
                return true;
            }
            plane.Altitude = Mathf.MoveTowards(plane.Altitude, 10, dt * 7);
            var position = plane.Position; position.y = DemoBattlefield.GroundHeight(position) + plane.Altitude; plane.transform.position = position;
            return plane.Altitude < 4;
        }

        public bool QueueUnit(RtsUnit producer, ArmyDefinition definition)
        {
            if (!CanQueueUnit(producer, definition, out string reason))
            {
                if (producer == null || producer.Team == 0) SetNotice(reason);
                return false;
            }
            Spend(producer.Team, definition.cost);
            producer.Production.Add(new ProductionOrder(definition, ProductionTime(definition)));
            if (producer.Team == 0) SetNotice(definition.displayName + " added to production.");
            return true;
        }

        public bool CanBuild(RtsUnit worker, ArmyDefinition definition, Vector3 position, out string reason)
        {
            reason = "";
            if (!Running) reason = "Match is paused";
            else if (worker == null || !worker.IsAlive || !worker.IsWorker) reason = "Select a construction worker";
            else if (worker.Construction != null && worker.Construction.IsAlive && worker.Construction.BuildProgress < 1)
                reason = "This worker is already constructing a building";
            else if (definition == null || definition.category != "Structure" || definition.prefab == null ||
                     definition.faction != worker.Definition.faction) reason = "Select an allied building blueprint";
            else if ((reason = GetRequirement(definition, worker.Team)).Length > 0) { }
            else if (Money(worker.Team) < definition.cost) reason = "Insufficient credits";
            else if (!CanOccupy(position, Footprint(definition) * .57f, null)) reason = "Requires clear grass with space around the building";
            else if (TroopsOccupyBuildingSite(position, Footprint(definition) * .57f))
                reason = "Move ground units clear of the building site";
            else if (Mathf.Abs(position.x - DemoBattlefield.RiverCenter(position.z)) < 26)
                reason = "Keep river banks and bridge approaches clear";
            else
            {
                bool nearBase = false;
                int buildings = 0;
                foreach (RtsUnit unit in Units)
                    if (unit != null && unit.IsAlive && unit.Team == worker.Team && unit.IsStructure)
                    {
                        buildings++;
                        if (unit.BuildProgress >= 1 && FlatDistanceSquared(unit.Position, position) <= 55 * 55)
                            nearBase = true;
                    }
                if (buildings >= 30) reason = "Base limit reached (30 structures)";
                else if (!nearBase) reason = "Build within 55 meters of a completed allied structure";
                else if (FlatDistanceSquared(worker.Position, position) > 100 * 100)
                    reason = "Move your worker closer (within 100 meters)";
            }
            return reason.Length == 0;
        }

        bool TroopsOccupyBuildingSite(Vector3 position, float radius)
        {
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && !unit.IsStructure && !unit.IsAircraft &&
                    FlatDistanceSquared(position, unit.Position) < Mathf.Pow(radius + unit.Radius + .6f, 2)) return true;
            return false;
        }

        public bool PlaceBuilding(RtsUnit worker, ArmyDefinition definition, Vector3 position)
        {
            if (!CanBuild(worker, definition, position, out string reason))
            {
                if (worker == null || worker.Team == 0) SetNotice(reason);
                return false;
            }
            Spend(worker.Team, definition.cost);
            RtsUnit building = Spawn(definition, worker.Team, position, false);
            building.Builder = worker;
            worker.Construction = building;
            worker.AcceptsIdleConstruction = true;
            worker.Target = null;
            worker.ExplicitAttack = worker.AttackMoving = false;
            worker.CurrentOrder = "Constructing " + definition.displayName;
            SetDestination(worker, ApproachPoint(worker.Position, building), true);
            if (worker.Team == 0) SetNotice("Construction started: " + definition.displayName + ".");
            return true;
        }

        public void MoveUnits(IReadOnlyList<RtsUnit> selection, Vector3 destination, bool attackMove = false)
        {
            if (!Running || selection == null) return;
            int moving = 0;
            for (int i = 0; i < selection.Count; i++)
                if (selection[i] != null && selection[i].IsAlive && !selection[i].IsStructure) moving++;
            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(moving)));
            int index = 0;
            for (int i = 0; i < selection.Count; i++)
            {
                RtsUnit unit = selection[i];
                if (unit == null || !unit.IsAlive) continue;
                if (unit.IsStructure) { unit.RallyPoint = DemoBattlefield.ClampToMap(destination); continue; }
                if (unit.IsFixedWing)
                {
                    if (unit.AircraftAmmo <= 0 || unit.RearmRemaining > 0) continue;
                    unit.ReturningToAirfield = false; unit.SortieTarget = null;
                }
                ReleaseConstruction(unit);
                unit.AcceptsIdleConstruction = false;
                Vector3 offset = new Vector3((index % columns - (columns - 1) * .5f) * 4.8f, 0,
                    (index / columns - (Mathf.CeilToInt(moving / (float)columns) - 1) * .5f) * 4.8f);
                index++;
                unit.Target = null;
                unit.AttackMoving = attackMove && CanFire(unit);
                unit.ExplicitAttack = false;
                unit.GuardPosition = destination;
                unit.CurrentOrder = attackMove ? "Attack moving" : "Moving";
                if (unit.EgressRemaining > 0)
                { unit.EgressDestination = destination + offset; unit.HaltAfterEgress = false; continue; }
                SetDestination(unit, destination + offset, true);
            }
        }

        public void AttackUnits(IReadOnlyList<RtsUnit> selection, RtsUnit target)
        {
            if (!Running || selection == null || target == null || !target.IsAlive) return;
            for (int i = 0; i < selection.Count; i++)
            {
                RtsUnit unit = selection[i];
                if (unit == null || !unit.IsAlive || unit.Team == target.Team || !CanTarget(unit, target)) continue;
                if (unit.IsFixedWing)
                {
                    if (unit.AircraftAmmo <= 0 || unit.RearmRemaining > 0) continue;
                    unit.ReturningToAirfield = false; unit.SortieTarget = target;
                }
                ReleaseConstruction(unit);
                unit.AcceptsIdleConstruction = false;
                unit.Target = target;
                unit.HaltAfterEgress = false;
                unit.ExplicitAttack = true;
                unit.AttackMoving = false;
                unit.HasMoveOrder = false;
                unit.Path.Clear();
                unit.CurrentOrder = "Attacking " + target.Definition.displayName;
                unit.RepathCooldown = 0;
            }
        }

        public void StopUnits(IReadOnlyList<RtsUnit> selection)
        {
            if (!Running || selection == null) return;
            for (int i = 0; i < selection.Count; i++)
            {
                RtsUnit unit = selection[i];
                if (unit == null || !unit.IsAlive) continue;
                if (unit.IsFixedWing) unit.SortieTarget = null;
                if (unit.EgressRemaining > 0) unit.HaltAfterEgress = true;
                ReleaseConstruction(unit);
                unit.AcceptsIdleConstruction = false;
                unit.Target = null;
                unit.HasMoveOrder = unit.AttackMoving = unit.ExplicitAttack = false;
                unit.Path.Clear();
                unit.GuardPosition = unit.Position;
                unit.CurrentOrder = unit.IsStructure ? "Operational" : "Guarding";
                if (unit.IsFixedWing && unit.HomeAirfield && unit.HomeAirfield.IsAlive)
                { unit.ReturningToAirfield = false; ReturnAircraft(unit); }
            }
        }

        public void ReceiveDamage(RtsUnit target, float amount, RtsUnit attacker = null)
        {
            if (target == null || !target.IsAlive || amount <= 0 || ended) return;
            target.Health = Mathf.Max(0, target.Health - amount);
            if (target.Health > 0) return;
            if (attacker && attacker.IsAlive && attacker.Team != target.Team && !attacker.IsStructure)
                attacker.Experience += Mathf.Max(50, target.Definition.cost * .45f);
            if (target.Team == 0) Losses++; else Kills++;
            if (target.IsStructure) InvalidateRoutes();
            ReleaseConstruction(target);
            VisualEffects?.Destruction(target);
            target.gameObject.SetActive(false);
            Destroy(target.gameObject, .05f);
            if (target.Key == "Command")
            {
                RtsUnit remainingHQ = null;
                foreach (RtsUnit candidate in Units)
                    if (candidate != null && candidate.IsAlive && candidate.Team == target.Team &&
                        candidate.Key == "Command" && candidate.BuildProgress >= 1) { remainingHQ = candidate; break; }
                if (target.Team == 0) PlayerHQ = remainingHQ; else EnemyHQ = remainingHQ;
                if (remainingHQ == null)
                {
                    ended = true;
                    Running = false;
                    bool victory = target.Team == 1;
                    SetNotice(victory ? "Enemy headquarters destroyed. Victory!" : "Our headquarters has been destroyed.");
                    MatchEnded?.Invoke(victory);
                }
            }
        }

        void TickConstruction(RtsUnit building, float dt)
        {
            if (building.Builder == null || !building.Builder.IsAlive || building.Builder.Construction != building)
            {
                building.Builder = null;
                // A surviving idle worker can finish abandoned foundations without charging twice.
                foreach (RtsUnit worker in Units)
                    if (worker != null && worker.IsAlive && worker.Team == building.Team && worker.IsWorker &&
                        worker.Construction == null && !worker.HasMoveOrder &&
                        worker.AcceptsIdleConstruction &&
                        FlatDistanceSquared(worker.Position, building.Position) < 90 * 90)
                    {
                        building.Builder = worker;
                        worker.Construction = building;
                        worker.CurrentOrder = "Constructing " + building.Definition.displayName;
                        SetDestination(worker, ApproachPoint(worker.Position, building), true);
                        break;
                    }
                building.CurrentOrder = "Waiting for a construction worker";
                if (building.Builder == null) return;
            }
            if (FlatDistanceSquared(building.Builder.Position, building.Position) >
                Mathf.Pow(building.Radius + building.Builder.Radius + 4f, 2))
            {
                building.CurrentOrder = "Waiting for worker to arrive";
                return;
            }
            float addition = dt / building.BuildDuration;
            building.BuildProgress = Mathf.Clamp01(building.BuildProgress + addition);
            building.Health = Mathf.Min(building.MaxHealth, building.Health + addition * building.MaxHealth * .85f);
            building.CurrentOrder = "Under construction";
            if (building.BuildProgress >= 1)
            {
                building.CurrentOrder = "Operational";
                RtsUnit worker = building.Builder;
                worker.Construction = null;
                worker.CurrentOrder = "Guarding";
                worker.HasMoveOrder = false;
                worker.Path.Clear();
                building.Builder = null;
                if (building.Team == 0) SetNotice(building.Definition.displayName + " is operational.");
            }
        }

        void ReleaseConstruction(RtsUnit worker)
        {
            if (worker.Construction == null) return;
            worker.Construction.Builder = null;
            worker.Construction = null;
        }

        void TickProduction(RtsUnit producer, float dt)
        {
            if (producer.Production.Count == 0) return;
            ProductionOrder order = producer.Production[0];
            // Production requires live prerequisites, making infrastructure raids consequential.
            if (GetRequirement(order.Definition, producer.Team).Length > 0)
            {
                producer.CurrentOrder = "Production paused: missing prerequisite";
                return;
            }
            float speed = (HasPower(producer.Team) ? 1f : .3f) * (HasUpgrade("Logistics", producer.Team) ? 1.3f : 1f);
            order.Remaining = Mathf.Max(0, order.Remaining - dt * speed);
            producer.CurrentOrder = (HasPower(producer.Team) ? "Producing " : "Low power: producing ") + order.Definition.displayName;
            if (order.Remaining > 0) return;
            Vector3 direction = producer.transform.forward;
            direction.y = 0;
            if (direction.sqrMagnitude < 1) direction = producer.Team == 0 ? Vector3.right : Vector3.left;
            Vector3 spawnPosition = producer.Position + direction.normalized * (producer.Radius + Footprint(order.Definition) + 2);
            var exit = FacilityAnchor(producer, "ProductionLaneEnd");
            if (exit) spawnPosition = exit.position;
            RtsUnit unit = Spawn(order.Definition, producer.Team, FindOpenPosition(spawnPosition, Footprint(order.Definition) * .42f));
            producer.Production.RemoveAt(0);
            producer.CurrentOrder = producer.Production.Count == 0 ? "Operational" : "Producing";
            if (unit != null)
            {
                if (unit.IsFixedWing && AssignAirfield(unit, producer))
                {
                    unit.transform.position = AirfieldPadPosition(producer, unit.AirfieldSlot);
                    unit.transform.rotation = producer.transform.rotation; unit.Altitude = .4f;
                    unit.CurrentOrder = "Parked / ready for sortie";
                    if (unit.Team == 0) SetNotice(unit.Definition.displayName + " ready in airfield slot " + (unit.AirfieldSlot + 1));
                    return;
                }
                unit.CurrentOrder = "Moving to rally point";
                unit.GuardPosition = producer.RallyPoint;
                var doorway = FacilityAnchor(producer, "ProductionExit");
                if (!unit.IsAircraft && doorway && exit)
                {
                    Vector3 lane = exit.position - doorway.position; lane.y = 0;
                    if (lane.sqrMagnitude < .1f) lane = producer.transform.forward;
                    Vector3 outside = exit.position;
                    float clearance = producer.Radius + unit.Radius + 1;
                    if (FlatDistanceSquared(outside, producer.Position) < clearance * clearance) outside = producer.Position + lane.normalized * clearance;
                    unit.EgressFrom = doorway.position;
                    unit.EgressTo = FindOpenPosition(outside, unit.Radius);
                    unit.EgressDestination = producer.RallyPoint;
                    unit.EgressDuration = unit.EgressRemaining = Mathf.Max(1.8f, Vector3.Distance(unit.EgressFrom, unit.EgressTo) / 4);
                    unit.transform.position = unit.EgressFrom;
                    unit.transform.rotation = Quaternion.LookRotation(lane.normalized);
                    unit.CurrentOrder = producer.Key == "Factory" ? "Rolling out of vehicle factory" : "Deploying from barracks";
                    return;
                }
                SetDestination(unit, producer.RallyPoint, true);
                if (unit.Team == 0) SetNotice(unit.Definition.displayName + " ready.");
            }
        }

        void TickProductionExit(RtsUnit unit, float dt)
        {
            unit.EgressRemaining = Mathf.Max(0, unit.EgressRemaining - dt);
            Vector3 position = Vector3.Lerp(unit.EgressFrom, unit.EgressTo, 1 - unit.EgressRemaining / unit.EgressDuration);
            position.y = Mathf.Max(position.y, DemoBattlefield.GroundHeight(position));
            unit.transform.position = position;
            if (unit.EgressRemaining <= 0)
            {
                if (unit.HaltAfterEgress)
                { unit.CurrentOrder = "Guarding"; unit.GuardPosition = unit.Position; }
                else if (unit.ExplicitAttack && unit.Target && unit.Target.IsAlive)
                    unit.CurrentOrder = "Attacking " + unit.Target.Definition.displayName;
                else
                { unit.CurrentOrder = unit.AttackMoving ? "Attack moving" : "Moving to rally point"; SetDestination(unit, unit.EgressDestination, true); }
            }
        }

        int Money(int team) => team == 0 ? Credits : enemyCredits;
        void Spend(int team, int amount) { if (team == 0) Credits -= amount; else enemyCredits -= amount; }
        void SetNotice(string message) { Notice = message; noticeClock = 8f; }

        static float ProductionTime(ArmyDefinition definition)
        {
            return Mathf.Clamp(4f + definition.cost / 110f, 5.5f, 32f);
        }

        internal RtsUnit Spawn(ArmyDefinition definition, int team, Vector3 position, bool completed = true)
        {
            if (definition == null || definition.prefab == null) return null;
            GameObject model = Instantiate(definition.prefab, unitsRoot);
            model.name = (team == 0 ? "PLAYER | " : "ENEMY | ") + definition.displayName;
            model.transform.position = Vector3.zero;
            model.transform.rotation = Quaternion.identity;
            ArmyEntity entity = model.GetComponent<ArmyEntity>();
            Bounds bounds = entity != null ? entity.DisplayBounds : new Bounds(Vector3.zero, Vector3.one * 5);
            float footprint = Footprint(definition);
            float scale = footprint / Mathf.Max(bounds.size.x, bounds.size.z, .1f);
            model.transform.localScale *= scale;
            bounds = entity != null ? entity.DisplayBounds : new Bounds(Vector3.zero, Vector3.one * footprint);
            RtsUnit unit = model.AddComponent<RtsUnit>();
            unit.Simulation = this;
            unit.Definition = definition;
            unit.Team = team;
            unit.Key = DefinitionKey(definition);
            unit.MaxHealth = Mathf.Max(1, definition.health);
            if (!unit.IsStructure && HasUpgrade("Armor", team)) unit.MaxHealth *= 1.25f;
            unit.Health = completed ? unit.MaxHealth : unit.MaxHealth * .15f;
            unit.BuildProgress = completed ? 1 : 0;
            unit.BuildDuration = Mathf.Clamp(9f + definition.cost / 85f, 14, 65);
            unit.Radius = footprint * (unit.IsStructure ? .57f : .38f);
            unit.Altitude = unit.IsAircraft ? (unit.Key == "Drone" ? 7f : 10f) : 0;
            unit.ScreenHeight = Mathf.Max(2, bounds.max.y + .7f);
            unit.GuardPosition = position;
            unit.RallyPoint = DemoBattlefield.ClampToMap(position + (team == 0 ? Vector3.right : Vector3.left) * 22);
            unit.CurrentOrder = unit.IsStructure ? "Operational" : "Guarding";
            unit.WeaponCooldown = UnityEngine.Random.Range(.1f, .8f);
            unit.AcquireCooldown = UnityEngine.Random.Range(0, .5f);
            position = DemoBattlefield.ClampToMap(position);
            position.y = DemoBattlefield.GroundHeight(position) + unit.Altitude;
            model.transform.position = position;
            model.transform.rotation = Quaternion.Euler(0, team == 0 ? 90 : -90, 0);
            var ringObject = new GameObject("Selection circle");
            ringObject.transform.SetParent(model.transform, false);
            unit.SelectionRing = ringObject.AddComponent<LineRenderer>();
            unit.SelectionRing.sharedMaterial = team == 0 ? friendlyMaterial : enemyMaterial;
            unit.SelectionRing.useWorldSpace = false;
            unit.SelectionRing.loop = true;
            unit.SelectionRing.widthMultiplier = .12f;
            unit.SelectionRing.positionCount = 40;
            unit.SelectionRing.enabled = false;
            unit.SelectionRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // Keep a constant world-size line despite models having different import scales.
            ringObject.transform.localScale = new Vector3(1f / model.transform.lossyScale.x,
                1f / model.transform.lossyScale.y, 1f / model.transform.lossyScale.z);
            float ringRadius = unit.Radius + .65f;
            for (int i = 0; i < 40; i++)
            {
                float angle = i * Mathf.PI * 2 / 40;
                unit.SelectionRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * ringRadius, 0, Mathf.Sin(angle) * ringRadius));
            }
            Units.Add(unit);
            model.AddComponent<UnitPresentation>().Initialize(unit, VisualEffects);
            if (team != 0 && visibility && !IsVisible(unit))
                foreach (var renderer in model.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            if (unit.IsStructure) InvalidateRoutes();
            return unit;
        }

        public static string DefinitionKey(ArmyDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.id)) return "";
            int split = definition.id.IndexOf('_');
            return split >= 0 ? definition.id.Substring(split + 1) : definition.id;
        }

        public static float Footprint(ArmyDefinition definition)
        {
            string key = DefinitionKey(definition);
            switch (key)
            {
                case "Command": return 15;
                case "Power": return 10;
                case "Refinery": return 13;
                case "Barracks": return 12;
                case "Factory": return 15;
                case "Airfield": return 34;
                case "Tech": return 12;
                case "Turret": case "AirDefense": return 6;
                case "Superweapon": return 14;
                case "Worker": return 3.8f;
                case "Scout": return 3.8f;
                case "APC": case "Support": case "AntiAir": return 4.7f;
                case "Tank": return 5.2f;
                case "Heavy": return 6;
                case "Artillery": return 5.4f;
                case "Drone": return 3;
                case "Helicopter": return 6;
                case "Fighter": return 7;
                case "Bomber": return 8;
                default: return 1.7f;
            }
        }

        static float FlatDistanceSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }

        // Movement uses a small grid A* with cached terrain/building occupancy. Ground units never
        // teleport through the river: each movement segment is checked against its actual footprint.
        void InvalidateRoutes() { structureVersion++; walkabilityCache.Clear(); }

        bool CanOccupy(Vector3 point, float radius, RtsUnit ignore)
        {
            if (!DemoBattlefield.IsWalkable(point, radius)) return false;
            foreach (RtsUnit unit in Units)
                if (unit != null && unit != ignore && unit.IsAlive && unit.IsStructure &&
                    FlatDistanceSquared(point, unit.Position) < Mathf.Pow(radius + unit.Radius + .6f, 2)) return false;
            return true;
        }

        bool SegmentClear(Vector3 from, Vector3 to, float radius)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / 1.8f));
            for (int i = 1; i <= samples; i++)
                if (!CanOccupy(Vector3.Lerp(from, to, i / (float)samples), radius, null)) return false;
            return true;
        }

        Vector3 FindOpenPosition(Vector3 point, float radius)
        {
            point = DemoBattlefield.ClampToMap(point);
            if (CanOccupy(point, radius, null)) return point;
            for (int ring = 1; ring <= 22; ring++)
                for (int spoke = 0; spoke < 16; spoke++)
                {
                    float angle = spoke * Mathf.PI / 8;
                    Vector3 candidate = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * ring * 2f;
                    if (CanOccupy(candidate, radius, null)) return candidate;
                }
            return point;
        }

        Vector3 ApproachPoint(Vector3 source, RtsUnit target)
        {
            Vector3 direction = source - target.Position;
            direction.y = 0;
            if (direction.sqrMagnitude < .1f) direction = Vector3.right;
            return FindOpenPosition(target.Position + direction.normalized * (target.Radius + 3f), 1.6f);
        }

        void SetDestination(RtsUnit unit, Vector3 destination, bool replace)
        {
            destination = DemoBattlefield.ClampToMap(destination);
            if (!unit.IsAircraft) destination = FindOpenPosition(destination, unit.Radius);
            destination.y = 0;
            if (replace) { unit.Destination = destination; unit.HasMoveOrder = true; }
            unit.Path.Clear();
            unit.PathIndex = 0;
            unit.PathVersion = structureVersion;
            unit.RepathCooldown = 1f;
            unit.StuckTime = 0;
            if (unit.IsAircraft || SegmentClear(unit.Position, destination, unit.Radius))
            { unit.Path.Add(destination); return; }
            BuildRoute(unit, destination);
        }

        Vector3 GridPoint(int index)
        {
            return new Vector3(-148 + index % GridWidth * GridStep, 0, -104 + index / GridWidth * GridStep);
        }

        int GridIndex(Vector3 point)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((point.x + 148) / GridStep), 0, GridWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((point.z + 104) / GridStep), 0, GridHeight - 1);
            return z * GridWidth + x;
        }

        bool[] Walkability(float radius)
        {
            int bucket = Mathf.CeilToInt(radius * 2f);
            if (walkabilityCache.TryGetValue(bucket, out bool[] result)) return result;
            result = new bool[GridSize];
            for (int i = 0; i < result.Length; i++) result[i] = CanOccupy(GridPoint(i), bucket * .5f, null);
            walkabilityCache[bucket] = result;
            return result;
        }

        int NearestWalkableNode(Vector3 point, bool[] walkable, float radius, bool requireLink)
        {
            int center = GridIndex(point), best = -1;
            float bestDistance = float.PositiveInfinity;
            int cx = center % GridWidth, cz = center / GridWidth;
            for (int ring = 0; ring <= 8; ring++)
            {
                for (int z = Mathf.Max(0, cz - ring); z <= Mathf.Min(GridHeight - 1, cz + ring); z++)
                    for (int x = Mathf.Max(0, cx - ring); x <= Mathf.Min(GridWidth - 1, cx + ring); x++)
                    {
                        int candidate = z * GridWidth + x;
                        if (!walkable[candidate]) continue;
                        float distance = FlatDistanceSquared(point, GridPoint(candidate));
                        if (distance < bestDistance && (!requireLink || SegmentClear(point, GridPoint(candidate), radius)))
                        { bestDistance = distance; best = candidate; }
                    }
                if (best >= 0) return best;
            }
            return best;
        }

        void BuildRoute(RtsUnit unit, Vector3 destination)
        {
            bool[] walkable = Walkability(unit.Radius);
            int start = NearestWalkableNode(unit.Position, walkable, unit.Radius, true);
            int goal = NearestWalkableNode(destination, walkable, unit.Radius, true);
            if (start < 0 || goal < 0) { unit.CurrentOrder = "Route blocked"; return; }
            routeSearch++;
            routeHeap.Clear();
            routeVisited[start] = routeSearch;
            routeCost[start] = 0;
            routeParent[start] = -1;
            HeapPush(new RouteEntry(start, RouteHeuristic(start, goal)));
            bool reached = false;
            while (routeHeap.Count > 0)
            {
                int current = HeapPop().Index;
                if (routeClosed[current] == routeSearch) continue;
                if (current == goal) { reached = true; break; }
                routeClosed[current] = routeSearch;
                int cx = current % GridWidth, cz = current / GridWidth;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || x >= GridWidth || z < 0 || z >= GridHeight) continue;
                        int next = z * GridWidth + x;
                        if (!walkable[next] || routeClosed[next] == routeSearch) continue;
                        if (dx != 0 && dz != 0 && (!walkable[cz * GridWidth + x] || !walkable[z * GridWidth + cx])) continue;
                        float cost = routeCost[current] + (dx != 0 && dz != 0 ? 1.414214f : 1f);
                        if (routeVisited[next] == routeSearch && routeCost[next] <= cost) continue;
                        routeVisited[next] = routeSearch;
                        routeCost[next] = cost;
                        routeParent[next] = current;
                        HeapPush(new RouteEntry(next, cost + RouteHeuristic(next, goal)));
                    }
            }
            if (!reached) { unit.CurrentOrder = "Route blocked"; return; }
            reverseRoute.Clear();
            reverseRoute.Add(destination);
            for (int node = goal; node >= 0; node = routeParent[node]) reverseRoute.Add(GridPoint(node));
            Vector3 anchor = unit.Position;
            // Smooth only over segments that the full unit footprint can traverse.
            int waypoint = reverseRoute.Count - 1;
            while (waypoint >= 0)
            {
                int next = waypoint;
                for (int i = Mathf.Max(0, waypoint - 20); i < waypoint; i++)
                    if (SegmentClear(anchor, reverseRoute[i], unit.Radius)) { next = i; break; }
                unit.Path.Add(reverseRoute[next]);
                anchor = reverseRoute[next];
                waypoint = next - 1;
            }
        }

        static float RouteHeuristic(int from, int to)
        {
            int dx = Mathf.Abs(from % GridWidth - to % GridWidth), dz = Mathf.Abs(from / GridWidth - to / GridWidth);
            return Mathf.Max(dx, dz) + .414214f * Mathf.Min(dx, dz);
        }

        struct RouteEntry
        {
            internal int Index;
            internal float Score;
            internal RouteEntry(int index, float score) { Index = index; Score = score; }
        }

        void HeapPush(RouteEntry entry)
        {
            int index = routeHeap.Count;
            routeHeap.Add(entry);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (routeHeap[parent].Score <= entry.Score) break;
                routeHeap[index] = routeHeap[parent];
                index = parent;
            }
            routeHeap[index] = entry;
        }

        RouteEntry HeapPop()
        {
            RouteEntry result = routeHeap[0], tail = routeHeap[routeHeap.Count - 1];
            routeHeap.RemoveAt(routeHeap.Count - 1);
            if (routeHeap.Count == 0) return result;
            int index = 0;
            while (index * 2 + 1 < routeHeap.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < routeHeap.Count && routeHeap[child + 1].Score < routeHeap[child].Score) child++;
                if (routeHeap[child].Score >= tail.Score) break;
                routeHeap[index] = routeHeap[child];
                index = child;
            }
            routeHeap[index] = tail;
            return result;
        }

        void TickMovement(RtsUnit unit, float dt)
        {
            unit.RepathCooldown -= dt;
            bool engaged = unit.Target != null && unit.Target.IsAlive;
            if (engaged)
            {
                float range = EffectiveRange(unit) + unit.Target.Radius;
                if (FlatDistanceSquared(unit.Position, unit.Target.Position) <= range * range) return;
                if (unit.RepathCooldown <= 0)
                {
                    Vector3 toward = unit.Position - unit.Target.Position;
                    toward.y = 0;
                    Vector3 goal = unit.Target.Position + toward.normalized * Mathf.Max(unit.Target.Radius + unit.Radius + 1, range * .78f);
                    SetDestination(unit, goal, false);
                    unit.RepathCooldown = 1.1f + UnityEngine.Random.value * .6f;
                }
            }
            else if (unit.HasMoveOrder && unit.PathVersion != structureVersion && unit.RepathCooldown <= 0)
                SetDestination(unit, unit.Destination, false);
            if (unit.PathIndex >= unit.Path.Count)
            {
                if (unit.HasMoveOrder && !engaged && unit.CurrentOrder != "Route blocked")
                {
                    unit.HasMoveOrder = unit.AttackMoving = false;
                    unit.GuardPosition = unit.Position;
                    if (unit.Construction == null) unit.CurrentOrder = "Guarding";
                }
                return;
            }
            Vector3 goalPosition = unit.Path[unit.PathIndex];
            Vector3 delta = goalPosition - unit.Position;
            delta.y = 0;
            float distance = delta.magnitude;
            if (distance < .7f) { unit.PathIndex++; return; }
            Vector3 direction = delta / distance;
            Vector3 separation = Vector3.zero;
            foreach (RtsUnit other in Units)
            {
                if (other == null || other == unit || !other.IsAlive || other.IsStructure || other.IsAircraft != unit.IsAircraft) continue;
                float separationRadius = unit.Radius + other.Radius + .3f;
                Vector3 away = unit.Position - other.Position;
                away.y = 0;
                float sqr = away.sqrMagnitude;
                if (sqr > separationRadius * separationRadius) continue;
                if (sqr < .001f) away = new Vector3((unit.GetInstanceID() & 1) == 0 ? 1 : -1, 0, .4f);
                else away /= Mathf.Sqrt(sqr);
                separation += away * (1f - Mathf.Sqrt(sqr) / separationRadius);
            }
            // Limit avoidance influence so two columns can continue through bridge chokepoints.
            Vector3 velocity = (direction + Vector3.ClampMagnitude(separation, 1.2f) * .6f).normalized;
            float step = Mathf.Min(distance, Mathf.Max(2.5f, unit.Definition.speed) * 1.25f * dt);
            Vector3 next = unit.Position + velocity * step;
            if (!unit.IsAircraft && !CanOccupy(next, unit.Radius, null))
            {
                next = unit.Position + direction * step;
                if (!CanOccupy(next, unit.Radius, null))
                {
                    unit.StuckTime += dt;
                    if (unit.StuckTime > .5f && unit.RepathCooldown <= 0)
                        SetDestination(unit, engaged ? goalPosition : unit.Destination, false);
                    return;
                }
            }
            unit.StuckTime = 0;
            next = DemoBattlefield.ClampToMap(next);
            next.y = DemoBattlefield.GroundHeight(next) + unit.Altitude;
            unit.transform.position = next;
            unit.transform.rotation = Quaternion.RotateTowards(unit.transform.rotation, Quaternion.LookRotation(direction), dt * 200);
        }

        static bool CanFire(RtsUnit unit) => unit != null && unit.Key != "Worker" && unit.Key != "Support" && unit.Definition.range > 0;

        static bool CanTarget(RtsUnit attacker, RtsUnit target)
        {
            if (!CanFire(attacker) || target == null || !target.IsAlive || target.Team == attacker.Team) return false;
            if (attacker.Simulation != null && !attacker.Simulation.IsVisible(target, attacker.Team)) return false;
            if (attacker.Key == "AirDefense") return target.IsAircraft;
            if (!target.IsAircraft) return true;
            switch (attacker.Key)
            {
                case "Rocket": case "Rifle": case "APC": case "AntiAir": case "Drone": case "Fighter":
                case "Commando": return true;
                default: return false;
            }
        }

        static float EffectiveRange(RtsUnit unit) => Mathf.Max(8, unit.Definition.range);

        void TickCombat(RtsUnit unit, float dt)
        {
            unit.WeaponCooldown -= dt;
            unit.AcquireCooldown -= dt;
            if (unit.Key == "Support") { TickRepairs(unit); return; }
            if (unit.IsStructure && (unit.Key == "Turret" || unit.Key == "AirDefense" || unit.Key == "Superweapon") && !HasPower(unit.Team))
            { unit.CurrentOrder = "Offline: insufficient power"; return; }
            if (!CanFire(unit)) return;
            if (unit.IsFixedWing && (unit.Altitude < 4 || unit.AircraftAmmo <= 0 || unit.ReturningToAirfield || unit.RearmRemaining > 0)) return;
            bool permitsCombat = unit.IsStructure || !unit.HasMoveOrder || unit.AttackMoving || unit.ExplicitAttack;
            if (!permitsCombat) { unit.Target = null; return; }
            // Unity destroyed objects compare equal to null. Still clear the old target reference
            // and restore the original attack-move route when destruction occurred between frames.
            if (!ReferenceEquals(unit.Target, null) && (!CanTarget(unit, unit.Target) ||
                (!unit.ExplicitAttack && FlatDistanceSquared(unit.Position, unit.Target.Position) >
                 Mathf.Pow(EffectiveRange(unit) + 22f, 2))))
            {
                unit.Target = null;
                if (unit.ExplicitAttack) { unit.ExplicitAttack = false; unit.GuardPosition = unit.Position; }
                if (unit.HasMoveOrder)
                {
                    SetDestination(unit, unit.Destination, false);
                    unit.CurrentOrder = unit.AttackMoving ? "Attack moving" : "Moving";
                }
                else { unit.Path.Clear(); unit.CurrentOrder = unit.IsStructure ? "Operational" : "Guarding"; }
            }
            if (unit.Target == null && unit.AcquireCooldown <= 0)
            {
                unit.AcquireCooldown = .35f + UnityEngine.Random.value * .25f;
                float bestScore = float.PositiveInfinity;
                float range = EffectiveRange(unit) + (unit.IsStructure ? 0 : 9);
                foreach (RtsUnit candidate in Units)
                {
                    if (candidate == null || !CanTarget(unit, candidate)) continue;
                    float sqr = FlatDistanceSquared(unit.Position, candidate.Position);
                    if (sqr > Mathf.Pow(range + candidate.Radius, 2)) continue;
                    if (!unit.HasMoveOrder && !unit.IsStructure &&
                        FlatDistanceSquared(unit.GuardPosition, candidate.Position) > Mathf.Pow(range + 25, 2)) continue;
                    float score = sqr * (candidate.IsStructure ? 1.35f : 1f);
                    if ((unit.Key == "AntiAir" || unit.Key == "Fighter") && candidate.IsAircraft) score *= .3f;
                    if (score < bestScore) { bestScore = score; unit.Target = candidate; }
                }
                if (unit.Target != null)
                {
                    unit.RepathCooldown = 0;
                    unit.CurrentOrder = "Engaging " + unit.Target.Definition.displayName;
                }
            }
            if (unit.Target == null) return;
            float weaponRange = EffectiveRange(unit) + unit.Target.Radius;
            if (FlatDistanceSquared(unit.Position, unit.Target.Position) > weaponRange * weaponRange) return;
            if (unit.WeaponCooldown > 0) return;
            RtsUnit target = unit.Target;
            float damage = WeaponDamage(unit, target) * (1f + unit.VeteranRank * .15f) * (HasUpgrade("Weapons", unit.Team) ? 1.2f : 1f);
            unit.WeaponCooldown = FireInterval(unit);
            float splash = unit.Key == "Superweapon" ? 9 : unit.Key == "Artillery" || unit.Key == "Bomber" ? 5 : 0;
            if (VisualEffects) VisualEffects.Fire(unit, target, damage, splash);
            else ReceiveDamage(target, damage, unit);
            if (unit.IsFixedWing && --unit.AircraftAmmo <= 0) { unit.SortieTarget = target; ReturnAircraft(unit); }
        }

        void TickRepairs(RtsUnit support)
        {
            if (support.WeaponCooldown > 0) return;
            support.WeaponCooldown = .8f;
            RtsUnit damaged = null;
            float lowest = 1;
            foreach (RtsUnit unit in Units)
                if (unit != null && unit != support && unit.IsAlive && unit.Team == support.Team &&
                    unit.BuildProgress >= 1 && unit.Definition.category != "Infantry" &&
                    FlatDistanceSquared(unit.Position, support.Position) <= support.Definition.range * support.Definition.range &&
                    unit.Health / unit.MaxHealth < lowest)
                { lowest = unit.Health / unit.MaxHealth; damaged = unit; }
            if (damaged == null) return;
            damaged.Health = Mathf.Min(damaged.MaxHealth, damaged.Health + 55);
            SpawnShot(support.Position + Vector3.up * 2, damaged.Position + Vector3.up, support.Team, .35f, .28f);
        }

        static float FireInterval(RtsUnit unit)
        {
            switch (unit.Key)
            {
                case "Rifle": return .8f;
                case "Rocket": return 2.2f;
                case "Tank": return 2f;
                case "Heavy": return 2.5f;
                case "Artillery": return 3.8f;
                case "Bomber": return 3.8f;
                case "AntiAir": case "AirDefense": return 1.5f;
                case "Turret": return 2.1f;
                case "Superweapon": return 20f;
                default: return 1.1f;
            }
        }

        static float WeaponDamage(RtsUnit attacker, RtsUnit target)
        {
            float damage;
            switch (attacker.Key)
            {
                case "Rifle": damage = 23; break;
                case "Rocket": damage = 100; break;
                case "Engineer": damage = 12; break;
                case "Commando": damage = 82; break;
                case "Scout": damage = 30; break;
                case "APC": damage = 45; break;
                case "Tank": damage = 155; break;
                case "Heavy": damage = 255; break;
                case "Artillery": damage = 220; break;
                case "AntiAir": damage = 125; break;
                case "Drone": damage = 30; break;
                case "Helicopter": damage = 100; break;
                case "Fighter": damage = 100; break;
                case "Bomber": damage = 260; break;
                case "Turret": damage = 135; break;
                case "AirDefense": damage = 220; break;
                case "Superweapon": damage = 1350; break;
                default: damage = 20; break;
            }
            bool infantry = target.Definition.category == "Infantry";
            bool armored = target.Definition.category == "Vehicle" && target.Key != "Worker" && target.Key != "Scout";
            if ((attacker.Key == "Rifle" || attacker.Key == "Scout" || attacker.Key == "APC" || attacker.Key == "Drone") && armored) damage *= .4f;
            if (attacker.Key == "Rocket" && infantry) damage *= .35f;
            if (attacker.Key == "AntiAir" && !target.IsAircraft) damage *= .23f;
            if (attacker.Key == "Fighter" && !target.IsAircraft) damage *= .55f;
            if (attacker.Key == "Rifle" && target.IsAircraft) damage *= .4f;
            if (attacker.Key == "Commando" && target.IsStructure) damage *= 2.5f;
            if (attacker.Key == "Bomber" && target.IsStructure) damage *= 1.6f;
            return damage;
        }

        void TickOpponent()
        {
            if (EnemyHQ == null || PlayerHQ == null) return;
            RtsUnit builder = null;
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == 1 && unit.IsWorker && unit.Construction == null)
                { builder = unit; break; }
            if (builder == null && !HasUnitKey(1, "Worker") && EnemyHQ.Production.Count == 0)
                QueueUnit(EnemyHQ, Find(1, "Worker"));
            if (builder != null)
            {
                bool buildingPower = Units.Exists(u => u && u.IsAlive && u.Team == 1 && u.Key == "Power" && u.BuildProgress < 1);
                string desired = (!HasBuilding(1, "Power", true) || PowerGenerated(1) - PowerConsumed(1) < 8 && !buildingPower) ? "Power" :
                    !HasBuilding(1, "Refinery", true) ? "Refinery" :
                    !HasBuilding(1, "Barracks", true) ? "Barracks" :
                    !HasBuilding(1, "Factory", true) ? "Factory" :
                    Elapsed > 45 && !HasBuilding(1, "Tech", true) ? "Tech" :
                    Elapsed > 95 && !HasBuilding(1, "Airfield", true) ? "Airfield" :
                    Elapsed > 35 && !HasBuilding(1, "Turret", true) ? "Turret" :
                    Elapsed > 130 && !HasBuilding(1, "AirDefense", true) ? "AirDefense" : "";
                if (desired.Length > 0) TryOpponentBuilding(builder, desired);
            }
            string[] infantry = wave < 2 ? new[] { "Rifle", "Rifle", "Rocket" } : new[] { "Rifle", "Rocket", "Rocket", "Commando" };
            string[] vehicles = wave < 1 ? new[] { "Scout", "APC", "Tank" } :
                new[] { "Tank", "Tank", "AntiAir", "Artillery", "Support", "Heavy" };
            int targetArmy = Mathf.Min(55, 10 + wave * 4 + difficulty * 3);
            foreach (RtsUnit producer in Units)
            {
                if (producer == null || !producer.IsAlive || producer.Team != 1 || !producer.IsStructure ||
                    producer.BuildProgress < 1 || producer.Production.Count > 0 || ReservedUnitCount(1) >= targetArmy) continue;
                string key = producer.Key == "Barracks" ? infantry[UnityEngine.Random.Range(0, infantry.Length)] :
                    producer.Key == "Factory" ? vehicles[UnityEngine.Random.Range(0, vehicles.Length)] :
                    producer.Key == "Airfield" ? (wave < 3 ? "Helicopter" : UnityEngine.Random.value < .5f ? "Fighter" : "Bomber") : "";
                if (key.Length == 0) continue;
                ArmyDefinition candidate = Find(1, key);
                if (candidate != null && GetRequirement(candidate, 1).Length > 0)
                    candidate = Find(1, producer.Key == "Barracks" ? "Rocket" : producer.Key == "Factory" ? "APC" : "Drone");
                if (candidate != null) QueueUnit(producer, candidate);
            }
            if (Elapsed < nextAttack) return;
            commandScratch.Clear();
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == 1 && !unit.IsStructure && !unit.IsWorker &&
                    !unit.HasMoveOrder && !unit.ExplicitAttack) commandScratch.Add(unit);
            if (commandScratch.Count < 4) { nextAttack = Elapsed + 12; return; }
            wave++;
            MoveUnits(commandScratch, PlayerHQ.Position, true);
            nextAttack = Elapsed + (difficulty == 0 ? 85 : difficulty == 1 ? 65 : 48);
            SetNotice("Enemy assault detected. Defend the river crossings!");
        }

        bool HasUnitKey(int team, string key)
        {
            foreach (RtsUnit unit in Units)
                if (unit != null && unit.IsAlive && unit.Team == team && unit.Key == key) return true;
            return false;
        }

        void TryOpponentBuilding(RtsUnit builder, string key)
        {
            ArmyDefinition definition = Find(1, key);
            if (definition == null || enemyCredits < definition.cost || GetRequirement(definition, 1).Length > 0) return;
            // A deterministic spacious set of expansion pads keeps the opponent's routes open.
            Vector3[] positions = {
                new Vector3(118,0,67), new Vector3(138,0,37), new Vector3(96,0,70),
                new Vector3(78,0,49), new Vector3(123,0,9), new Vector3(91,0,8),
                new Vector3(67,0,67), new Vector3(140,0,86), new Vector3(113,0,90),
                new Vector3(65,0,30), new Vector3(118,0,-20), new Vector3(92,0,-22)
            };
            foreach (Vector3 position in positions)
                if (CanBuild(builder, definition, position, out _))
                { PlaceBuilding(builder, definition, position); return; }
        }

        sealed class ShotEffect
        {
            internal GameObject Root;
            internal LineRenderer Line;
            internal Transform Impact;
            internal float Remaining, Duration, Size;
        }

        void EnsureMaterials()
        {
            if (friendlyMaterial != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            friendlyMaterial = MakeEffectMaterial(shader, new Color(.1f, 1f, .86f));
            enemyMaterial = MakeEffectMaterial(shader, new Color(1f, .34f, .1f));
            impactMaterial = MakeEffectMaterial(shader, new Color(1f, .8f, .28f));
        }

        static Material MakeEffectMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { name = "Skirmish effect" };
            SetEffectColor(material, color);
            return material;
        }

        static void SetEffectColor(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        }

        void SpawnShot(Vector3 from, Vector3 to, int team, float size, float duration)
        {
            if (effectsRoot == null) return;
            ShotEffect effect = null;
            foreach (ShotEffect candidate in effects)
                if (candidate.Remaining <= 0) { effect = candidate; break; }
            if (effect == null)
            {
                if (effects.Count >= 96) return;
                effect = new ShotEffect();
                effect.Root = new GameObject("Tracer and impact");
                effect.Root.transform.SetParent(effectsRoot, false);
                effect.Line = effect.Root.AddComponent<LineRenderer>();
                effect.Line.positionCount = 2;
                effect.Line.useWorldSpace = true;
                effect.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                impact.name = "Impact flash";
                impact.transform.SetParent(effect.Root.transform, false);
                Collider collider = impact.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                impact.GetComponent<Renderer>().sharedMaterial = impactMaterial;
                impact.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                effect.Impact = impact.transform;
                effects.Add(effect);
            }
            effect.Root.SetActive(true);
            effect.Duration = effect.Remaining = duration;
            effect.Size = size;
            effect.Line.sharedMaterial = team == 0 ? friendlyMaterial : enemyMaterial;
            effect.Line.widthMultiplier = size > 3 ? .48f : .095f;
            effect.Line.SetPosition(0, from);
            effect.Line.SetPosition(1, to);
            effect.Impact.position = to;
            effect.Impact.localScale = Vector3.one * size;
        }

        void TickEffects(float dt)
        {
            foreach (ShotEffect effect in effects)
            {
                if (effect.Remaining <= 0) continue;
                effect.Remaining -= dt;
                if (effect.Remaining <= 0) { effect.Root.SetActive(false); continue; }
                effect.Impact.localScale = Vector3.one * effect.Size * Mathf.Clamp01(effect.Remaining / effect.Duration);
            }
        }

        static void ReleaseObject(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
    }
}
