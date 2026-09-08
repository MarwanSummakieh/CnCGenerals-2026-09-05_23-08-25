using System.Collections.Generic;
using UnityEngine;

namespace NeonFrontier
{
    [CreateAssetMenu(fileName = "ArmyDefinition", menuName = "Neon Frontier/Army Definition")]
    public sealed class ArmyDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public string faction;
        public string category;
        public string role;
        [TextArea(2, 4)] public string description;
        [Range(1, 3)] public int tier = 1;
        public int cost;
        public float health;
        public float range;
        public float speed;
        public string[] strengths;
        public string[] weaknesses;
        public GameObject prefab;
    }

    /// <summary>Concept data for the army atlas. Values are design proposals, not implemented combat rules.</summary>
    public static class ArmyRosterCatalog
    {
        public static List<ArmyDefinition> CreateDefinitions()
        {
            var result = new List<ArmyDefinition>(52);
            Add(result, "Vanguard", "Worker", "MULE Fabricator", "Vehicle", "Construction / field logistics", 1, 600, 450, 0, 7,
                "An autonomous four-wheel construction rig. Deploys prefabricated modules and restores allied machinery between engagements.", "Builds infrastructure;Repairs vehicles", "No weapon;Raiding scouts");
            Add(result, "Vanguard", "Rifle", "Neon Ranger", "Infantry", "General-purpose line infantry", 1, 200, 160, 18, 4.5f,
                "Professional infantry in sealed ivory and navy armor. Smart optics and disciplined fire reward cover and combined-arms support.", "Enemy infantry;Garrison fighting", "Area weapons;Heavy armor");
            Add(result, "Vanguard", "Rocket", "Arc Lancer", "Infantry", "Portable anti-armor / anti-air", 1, 350, 130, 26, 3.8f,
                "A shoulder-mounted guided launcher gives small squads a precise response to armor and low-flying aircraft.", "Armored vehicles;Helicopters", "Suppression;Close infantry");
            Add(result, "Vanguard", "Engineer", "Ghostwire Specialist", "Infantry", "Capture / electronic support", 1, 450, 110, 8, 4.4f,
                "A combat technician with a portable uplink. Captures neutral infrastructure and supports detection of hostile systems.", "Neutral objectives;Stealth detection", "Direct combat;Fast raiders");
            Add(result, "Vanguard", "Commando", "Specter Operative", "Infantry", "Elite sabotage / reconnaissance", 3, 1600, 340, 24, 5.2f,
                "An elite infiltration specialist using active camouflage and precision demolition. A proposed one-per-army limit preserves counterplay.", "Isolated infrastructure;Soft targets", "Detection networks;Area damage");
            Add(result, "Vanguard", "Scout", "Wisp Recon Buggy", "Vehicle", "Fast reconnaissance / harassment", 1, 500, 300, 17, 12,
                "A lightweight patrol buggy with a sensor mast and compact autocannon. Spots firing lanes for the slower weapons behind it.", "Map information;Exposed workers", "Tanks;Anti-vehicle fire");
            Add(result, "Vanguard", "APC", "Aegis IFV", "Vehicle", "Infantry transport / screening", 1, 800, 850, 21, 8.5f,
                "A protected troop carrier with a remote weapons station. A mobile anchor for Rangers and specialist infantry.", "Infantry delivery;Light vehicles", "Heavy tanks;Rocket squads");
            Add(result, "Vanguard", "Tank", "Paladin Railtank", "Vehicle", "Precision main battle tank", 2, 1200, 1500, 29, 6.5f,
                "An angular railgun tank with spaced armor and a low turret. Wins deliberate armor engagements with accurate first shots.", "Medium armor;Defensive duels", "Flanking swarms;Artillery");
            Add(result, "Vanguard", "Heavy", "Atlas Siege Tank", "Vehicle", "Heavy breakthrough / fire support", 3, 2500, 3000, 32, 3.8f,
                "A broad tracked assault platform with paired rail weapons. Its mass and slow repositioning demand an escort.", "Fortifications;Heavy armor", "Air strikes;Mobile artillery");
            Add(result, "Vanguard", "Artillery", "Longbow Coil Battery", "Vehicle", "Long-range precision artillery", 2, 1400, 600, 62, 5.2f,
                "A deployable electromagnetic gun fires precise long-range rounds. Reconnaissance unlocks its reach; close pressure shuts it down.", "Static defenses;Concentrated armor", "Fast flanks;Aircraft");
            Add(result, "Vanguard", "AntiAir", "Halo Interceptor", "Vehicle", "Mobile air denial", 2, 1000, 800, 35, 7,
                "A rotating missile and tracking-radar module protects valuable armor columns against attack aircraft.", "Aircraft;Airborne drones", "Tanks;Ground artillery");
            Add(result, "Vanguard", "Support", "Nexus Field Rig", "Vehicle", "Repair / electronic warfare", 2, 950, 700, 16, 6.5f,
                "A mobile service and communications vehicle. Sustains expensive Vanguard hardware but contributes little direct damage.", "Vehicle sustain;Sensor support", "Focus fire;Independent combat");
            Add(result, "Vanguard", "Drone", "Firefly Attack Drone", "Aircraft", "Cheap aerial reconnaissance", 1, 450, 180, 16, 10,
                "A compact ducted-fan drone carries a light precision weapon. Scouts contested ground and punishes unsupported workers.", "Scouting;Exposed infantry", "Dedicated anti-air;Heavy armor");
            Add(result, "Vanguard", "Helicopter", "Valkyrie Gunship", "Aircraft", "Hovering anti-armor support", 2, 1600, 850, 28, 11,
                "A four-blade main rotor and tail rotor carry this precision gunship and its guided munitions. Hovering attacks exploit gaps in enemy formations.", "Ground armor;Flanking attacks", "Air defenses;Interceptors");
            Add(result, "Vanguard", "Fighter", "Razorwing Interceptor", "Aircraft", "Air superiority / precision strike", 2, 1800, 750, 32, 24,
                "A sharp-profile combat jet built around air control. Limited strike capacity rewards target priority and rearming discipline.", "Enemy aircraft;Priority targets", "Layered air defenses;Long attrition");
            Add(result, "Vanguard", "Bomber", "Eclipse Stealth Bomber", "Aircraft", "Strategic precision bombing", 3, 2800, 1000, 30, 17,
                "A broad-wing twin-engine strike bomber with canted tail fins attacks strategic targets. Low-observable sorties are constrained by detection and rearm windows.", "High-value structures;Packed defenses", "Detection plus interceptors;Rearm downtime");

            Add(result, "Dynasty", "Worker", "Ox Foundry Crawler", "Vehicle", "Construction / salvage logistics", 1, 500, 650, 0, 5.5f,
                "A rugged tracked fabrication crawler with articulated tools. Establishes heavy industry and keeps the production network expanding.", "Durable construction;Field repairs", "No weapon;Air harassment");
            Add(result, "Dynasty", "Rifle", "Redline Legionary", "Infantry", "Massed line infantry", 1, 150, 180, 17, 4.2f,
                "Armored infantry carry rugged pulse rifles. Affordable squads hold territory and screen the Dynasty's industrial war machines.", "Cost-efficient map control;Massed infantry", "Area damage;Precision suppression");
            Add(result, "Dynasty", "Rocket", "Thunder Spear", "Infantry", "Heavy rocket infantry", 1, 300, 150, 24, 3.5f,
                "A reinforced powered frame supports a heavy rocket launcher. Cheap concentrated volleys threaten armor and slow aircraft.", "Armored pushes;Helicopters", "Artillery;Fast anti-infantry");
            Add(result, "Dynasty", "Engineer", "Circuit Adept", "Infantry", "Capture / infrastructure support", 1, 400, 140, 8, 4,
                "A field network engineer carrying a relay pack. Captures strategic facilities and reinforces the army's industrial backbone.", "Neutral objectives;Infrastructure support", "Frontline fighting;Raiders");
            Add(result, "Dynasty", "Commando", "Jade Phantom", "Infantry", "Elite disruption / demolition", 3, 1500, 400, 21, 4.8f,
                "A cybernetically enhanced saboteur with an industrial demolition rig. Designed for one decisive breach, with a one-per-army limit.", "Production sabotage;Isolated heavy targets", "Detection;Concentrated fire");
            Add(result, "Dynasty", "Scout", "Jackal Recon Buggy", "Vehicle", "Rapid scouting / worker raids", 1, 400, 260, 16, 13,
                "A stripped-down four-wheel recon buggy with a forward gun. Fast and disposable, it pressures distant supply routes.", "Worker harassment;Fast scouting", "Armored vehicles;Prepared defenses");
            Add(result, "Dynasty", "APC", "Bastion Troop Carrier", "Vehicle", "Armored transport / infantry support", 1, 700, 1000, 19, 7,
                "A squat six-wheel troop carrier with armored side compartments. Brings Legionaries into the fight behind a suppressive twin-gun turret.", "Infantry screening;Protected delivery", "Main battle tanks;Guided rockets");
            Add(result, "Dynasty", "Tank", "Longma Battle Tank", "Vehicle", "Mass-production battle tank", 2, 1000, 1800, 26, 5.8f,
                "A wide tracked tank with twin conventional cannons and exposed cooling systems. Strong armor enables sustained frontal pressure.", "Frontal engagements;Cost-efficient armor", "Precision railguns;Mobile flanks");
            Add(result, "Dynasty", "Heavy", "Qilin Twin-Cannon", "Vehicle", "Super-heavy assault tank", 3, 2400, 3600, 30, 3.2f,
                "A towering twin-barrel siege tank wrapped in layered armor. Dominates ground approaches but cannot cover the sky alone.", "Ground breakthrough;Static fortifications", "Attack aircraft;Long-range artillery");
            Add(result, "Dynasty", "Artillery", "Firestorm Rocket Array", "Vehicle", "Area-denial rocket artillery", 2, 1300, 750, 58, 4.8f,
                "An industrial missile rack saturates a broad target area. Devastating against fixed formations, inefficient against dispersed movers.", "Infantry formations;Entrenched defenses", "Fast raids;Dispersed targets");
            Add(result, "Dynasty", "AntiAir", "Skyguard Missile Carrier", "Vehicle", "Mobile anti-air missile screen", 2, 850, 1050, 29, 6,
                "A radar-directed eight-cell missile platform accompanies the armored front. Overlapping batteries shield heavy vehicles from aerial attacks.", "Aircraft;Airborne drones", "Heavy tanks;Ground artillery");
            Add(result, "Dynasty", "Support", "Forge Repair Carrier", "Vehicle", "Heavy repair / army sustain", 2, 800, 900, 14, 5.5f,
                "A six-wheel support vehicle carries spare armor, manipulator cranes, and field tools. Keeps a slow armored push supplied and repaired.", "Heavy vehicle sustain;Prolonged pushes", "Air raids;Focus fire");
            Add(result, "Dynasty", "Drone", "Cinder Swarm Drone", "Aircraft", "Disposable aerial harassment", 1, 350, 150, 14, 10.5f,
                "A cheap compact strike drone with bright industrial emitters. Numbers and overlapping approaches create pressure against thin defenses.", "Cheap scouting;Unsupported workers", "Rotary anti-air;Armor");
            Add(result, "Dynasty", "Helicopter", "Redkite Assault VTOL", "Aircraft", "Heavy ground attack / suppression", 2, 1450, 1100, 25, 9,
                "An armored attack helicopter with a four-blade rotor, tail rotor, and twin chin guns trades agility for endurance. Best above a supported ground offensive.", "Infantry positions;Ground armor", "Interceptors;Layered anti-air");
            Add(result, "Dynasty", "Fighter", "J-88 Stormblade", "Aircraft", "Fast interception / strike sorties", 2, 1600, 850, 29, 23,
                "A swept-back twin-engine combat aircraft built for violent short sorties. A robust airframe supports the Dynasty's tempo on the ground.", "Air interception;Strike opportunities", "Prepared air defenses;Rearm downtime");
            Add(result, "Dynasty", "Bomber", "Heavenfall Arsenal Plane", "Aircraft", "Heavy area bombardment", 3, 2600, 1400, 28, 14,
                "A broad heavy bomber with multiple engines and a deep payload bay. Saturation attacks punish dense bases and static battle lines.", "Dense structures;Stationary armies", "Fighter interception;Scattered targets");

            Add(result, "Vanguard", "Command", "Vanguard Command Nexus", "Structure", "Headquarters / construction access", 1, 2500, 5000, 0, 0,
                "A secure central command complex with a holographic communications spire. Produces MULEs and anchors the base network.", "Construction access;Base coordination", "Strategic priority target;No direct weapon");
            Add(result, "Vanguard", "Power", "Helios Fusion Plant", "Structure", "Power generation", 1, 800, 1700, 0, 0,
                "A compact fusion core behind armored heat exchangers. The proposed power network supports technology and defensive systems.", "Efficient base power;Technology support", "Raid vulnerability;Power dependency");
            Add(result, "Vanguard", "Refinery", "Quantum Supply Hub", "Structure", "Resource collection / logistics", 1, 1200, 2300, 0, 0,
                "A cargo depot with automated receiving cranes and resource processors. Creates a valuable point of interaction along supply routes.", "Army income;Supply logistics", "Worker raids;Exposed expansion sites");
            Add(result, "Vanguard", "Barracks", "Ranger Deployment Center", "Structure", "Infantry recruitment", 1, 700, 2000, 0, 0,
                "A modular training and deployment building. Fields Rangers, Arc Lancers, and Ghostwire Specialists; Specter requires tier-three research.", "Infantry production;Objective control", "Siege weapons;Production disruption");
            Add(result, "Vanguard", "Factory", "Aegis Motorworks", "Structure", "Vehicle production", 1, 1500, 3200, 0, 0,
                "An armored fabrication hall with an open assembly bay. Produces ground vehicles as technology unlocks become available.", "Ground force production;Vehicle variety", "Air strikes;Supply dependence");
            Add(result, "Vanguard", "Airfield", "Vector Flight Deck", "Structure", "Aircraft production / rearming", 2, 1800, 2800, 0, 0,
                "A circular luminous VTOL landing pad, control tower, and maintenance apron. Builds and rearms aircraft; advanced bombers require tier-three research.", "Air projection;Aircraft servicing", "Bombardment;Ground raids");
            Add(result, "Vanguard", "Tech", "Prism Research Institute", "Structure", "Tier-two / tier-three research", 2, 2000, 2200, 0, 0,
                "A secure laboratory with optical antennae and a central research core. Unlocks advanced armor, electronic systems, and late-game options.", "Technology access;Advanced upgrades", "High-value raid target;Investment delay");
            Add(result, "Vanguard", "Turret", "Sentry Rail Emplacement", "Structure", "Ground defense", 1, 850, 1600, 32, 0,
                "An elevated precision gun protects predictable ground approaches. Overlapping infantry and air defenses cover its specialized firing role.", "Armored approaches;Chokepoints", "Artillery;Aircraft");
            Add(result, "Vanguard", "AirDefense", "Aureole SAM Grid", "Structure", "Static air defense / detection", 2, 1000, 1400, 40, 0,
                "A fixed missile battery and sensor array secures the air above critical infrastructure. Proposed detection counters stealth aircraft.", "Aircraft;Stealth detection", "Ground assaults;Artillery");
            Add(result, "Vanguard", "Superweapon", "Aurora Orbital Uplink", "Structure", "Strategic precision superweapon", 3, 5000, 3200, 120, 0,
                "A huge segmented uplink directs a proposed orbital lance after a visible charging timer. Its role is to break late-game stalemates.", "Strategic target removal;Siege pressure", "Long visible charge;Power loss / raids");

            Add(result, "Dynasty", "Command", "Dynasty Directorate", "Structure", "Headquarters / construction access", 1, 2500, 6000, 0, 0,
                "A fortified administrative citadel crowned with communications masts. Produces Ox Crawlers and coordinates the industrial base.", "Durable headquarters;Construction access", "Strategic priority target;No direct weapon");
            Add(result, "Dynasty", "Power", "Dragonheart Reactor", "Structure", "Power generation", 1, 700, 2100, 0, 0,
                "A tall armored reactor with conspicuous cooling towers. Proposed overclocking adds power at the cost of increased vulnerability.", "Industrial power;Expansion capacity", "Raids;Overclock risk");
            Add(result, "Dynasty", "Refinery", "Iron Tributary Depot", "Structure", "Resource collection / salvage", 1, 1100, 2800, 0, 0,
                "A hard-wearing resource intake complex with gantry cranes and cargo hoppers. Sustains continuous unit production from defended supply lines.", "Army income;Industrial logistics", "Supply-line raids;Expansion pressure");
            Add(result, "Dynasty", "Barracks", "Legion Muster Hall", "Structure", "Infantry recruitment", 1, 600, 2400, 0, 0,
                "A fortified infantry assembly hall. Recruits Legionaries, Thunder Spears, and Circuit Adepts; Jade Phantom requires advanced technology.", "Affordable infantry access;Territory control", "Area bombardment;Production disruption");
            Add(result, "Dynasty", "Factory", "Ironclad Assembly Works", "Structure", "Vehicle production", 1, 1400, 3800, 0, 0,
                "A deep industrial hall with twin production lanes and heavy lifting equipment. Supports the Dynasty's armor-led offensive doctrine.", "Armored production;Sustained pressure", "Supply dependence;Siege weapons");
            Add(result, "Dynasty", "Airfield", "Stormblade Aerodrome", "Structure", "Aircraft production / rearming", 2, 1700, 3300, 0, 0,
                "A hardened circular VTOL landing pad with maintenance facilities and industrial radar. Replenishes strike sorties and unlocks the faction's air wing.", "Air support;Aircraft servicing", "Ground raids;Bombardment");
            Add(result, "Dynasty", "Tech", "Jade Signal Academy", "Structure", "Tier-two / tier-three research", 2, 1800, 2700, 0, 0,
                "A fortified computing and weapons laboratory. Researches heavy platforms, production improvements, and the strategic weapons program.", "Heavy technology;Production upgrades", "High-value raid target;Investment delay");
            Add(result, "Dynasty", "Turret", "Ironwall Twin-Rail Bastion", "Structure", "Armored ground defense", 1, 750, 2200, 23, 0,
                "Twin rail weapons above a heavy armored bunker guard entrances and industrial approaches. Limited reach requires support against ranged armor.", "Ground approaches;Close chokepoints", "Long-range tanks;Aircraft");
            Add(result, "Dynasty", "AirDefense", "Skywall Missile Tower", "Structure", "Static anti-air missile defense", 2, 900, 1900, 35, 0,
                "An elevated eight-tube missile emplacement with heavy radar. Overlapping guided volleys protect the industrial core from aerial attacks.", "Aircraft;Low-flying attackers", "Ground armor;Precision artillery");
            Add(result, "Dynasty", "Superweapon", "Heavenforge Particle Spire", "Structure", "Strategic area energy superweapon", 3, 5000, 4000, 120, 0,
                "A fortified energy emitter prepares a proposed large-area particle discharge behind a public countdown. Its exposed charged spire invites decisive counter-raids.", "Area denial;Dense base damage", "Long visible charge;Raids / power loss");
            return result;
        }

        private static void Add(List<ArmyDefinition> list, string faction, string key, string title,
            string category, string role, int tier, int cost, float health, float range, float speed,
            string description, string strengths, string weaknesses)
        {
            var definition = ScriptableObject.CreateInstance<ArmyDefinition>();
            definition.name = faction + "_" + key;
            definition.id = definition.name;
            definition.displayName = title;
            definition.faction = faction;
            definition.category = category;
            definition.role = role;
            definition.tier = tier;
            definition.cost = cost;
            definition.health = health;
            definition.range = range;
            definition.speed = speed;
            definition.description = description;
            definition.strengths = strengths.Split(';');
            definition.weaknesses = weaknesses.Split(';');
            list.Add(definition);
        }
    }
}
