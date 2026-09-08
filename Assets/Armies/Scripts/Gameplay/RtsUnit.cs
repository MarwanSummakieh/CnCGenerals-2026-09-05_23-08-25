using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeonFrontier
{
    /// <summary>Runtime state for a model in the playable skirmish, independent of atlas concept data.</summary>
    public sealed class RtsUnit : MonoBehaviour
    {
        public ArmyDefinition Definition { get; internal set; }
        public int Team { get; internal set; }
        public float Health { get; internal set; }
        public float MaxHealth { get; internal set; }
        public float Experience { get; internal set; }
        public int VeteranRank => Experience >= 1800 ? 3 : Experience >= 800 ? 2 : Experience >= 300 ? 1 : 0;
        public bool IsStructure => Definition != null && Definition.category == "Structure";
        public bool IsAircraft => Definition != null && Definition.category == "Aircraft";
        public bool IsFixedWing => Key == "Fighter" || Key == "Bomber";
        public RtsUnit HomeAirfield { get; internal set; }
        public int AirfieldSlot { get; internal set; } = -1;
        public int AircraftAmmo { get; internal set; } = 4;
        public float RearmRemaining { get; internal set; }
        public bool ReturningToAirfield { get; internal set; }
        internal RtsUnit SortieTarget;
        internal float EgressRemaining, EgressDuration;
        internal bool HaltAfterEgress;
        internal Vector3 EgressFrom, EgressTo, EgressDestination;
        public bool IsWorker => Key == "Worker";
        public bool IsAlive => Health > 0 && gameObject.activeSelf;
        public bool Selected { get; set; }
        public Vector3 Position => transform.position;
        public string Key { get; internal set; }
        public float Radius { get; internal set; }
        public float ScreenHeight { get; internal set; }
        public string CurrentOrder { get; internal set; } = "Guarding";
        public readonly List<ProductionOrder> Production = new List<ProductionOrder>(5);
        public float BuildProgress { get; internal set; } = 1f;
        public Vector3 RallyPoint { get; set; }

        internal SkirmishSimulation Simulation;
        internal readonly List<Vector3> Path = new List<Vector3>(64);
        internal int PathIndex;
        internal Vector3 Destination;
        internal Vector3 GuardPosition;
        internal RtsUnit Target;
        internal RtsUnit Builder;
        internal RtsUnit Construction;
        internal bool AttackMoving;
        internal bool ExplicitAttack;
        internal bool HasMoveOrder;
        internal float WeaponCooldown;
        internal float AcquireCooldown;
        internal float RepathCooldown;
        internal float BuildDuration;
        internal float Altitude;
        internal LineRenderer SelectionRing;
        internal int PathVersion;
        internal float StuckTime;
        internal bool AcceptsIdleConstruction = true;

        /// <summary>Apply combat damage through the simulation so destruction and victory are accounted for.</summary>
        public void ApplyDamage(float amount, RtsUnit attacker = null)
        {
            if (Simulation != null) Simulation.ReceiveDamage(this, amount, attacker);
        }

        internal void RefreshSelection()
        {
            if (SelectionRing == null) return;
            SelectionRing.enabled = Selected && IsAlive;
            if (Selected)
                SelectionRing.transform.position = new Vector3(Position.x,
                    DemoBattlefield.GroundHeight(Position) + .16f, Position.z);
        }
    }

    [Serializable]
    public sealed class ProductionOrder
    {
        public ArmyDefinition Definition { get; internal set; }
        public float Duration { get; internal set; }
        public float Remaining { get; internal set; }
        public float Progress => Duration > 0 ? Mathf.Clamp01(1f - Remaining / Duration) : 1f;

        internal ProductionOrder(ArmyDefinition definition, float duration)
        {
            Definition = definition;
            Duration = duration;
            Remaining = duration;
        }
    }
}
