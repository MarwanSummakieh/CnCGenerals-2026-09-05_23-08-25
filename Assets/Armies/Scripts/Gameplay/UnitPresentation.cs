using System.Collections.Generic;
using UnityEngine;

namespace NeonFrontier
{
    /// <summary>Visual-only locomotion, turret tracking, smoke, rotor motion and construction staging.</summary>
    public sealed class UnitPresentation : MonoBehaviour
    {
        RtsUnit unit;
        BattlefieldEffects effects;
        Vector3 previous, visualScale, visualPosition;
        Transform visual, turret, rotor;
        Quaternion turretRest;
        float dustClock, smokeClock, weldClock, recoil;
        readonly List<GameObject> scaffolding = new List<GameObject>();
        public void Initialize(RtsUnit state, BattlefieldEffects fx)
        {
            unit = state; effects = fx; previous = transform.position;
            visual = transform.Find("Model");
            if (visual) { visualScale = visual.localScale; visualPosition = visual.localPosition; }
            foreach (Transform child in GetComponentsInChildren<Transform>())
            {
                if (child.name.Contains("TurretPivot")) { turret = child; turretRest = child.localRotation; }
                if (child.name == "Rotor" || child.name.StartsWith("Rotor.")) rotor = child;
            }
            if (unit.BuildProgress < 1 && visual)
            {
                Material material = GetComponentInChildren<MeshRenderer>()?.sharedMaterial;
                for (int i = 0; i < 4; i++)
                {
                    var post = GameObject.CreatePrimitive(PrimitiveType.Cube); post.name = "Construction scaffold";
                    post.transform.SetParent(transform, false);
                    var collider = post.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                    post.GetComponent<MeshRenderer>().sharedMaterial = material;
                    float radius = unit.Radius * .78f;
                    var world = transform.position + new Vector3(i % 2 == 0 ? -radius : radius, unit.ScreenHeight * .35f, i < 2 ? -radius : radius);
                    post.transform.position = world;
                    post.transform.localScale = new Vector3(.2f / transform.lossyScale.x, unit.ScreenHeight * .7f / transform.lossyScale.y, .2f / transform.lossyScale.z);
                    scaffolding.Add(post);
                }
            }
        }
        public void Recoil() { recoil = .16f; }
        void Update()
        {
            if (!unit || !unit.IsAlive || !unit.Simulation.Running) return;
            float dt = Time.deltaTime;
            if (rotor) rotor.Rotate(0, 1250 * dt, 0, Space.World);
            if (turret && unit.Target && unit.Target.IsAlive)
            {
                Vector3 aim = unit.Target.Position - turret.position; aim.y = 0;
                if (aim.sqrMagnitude > .01f)
                {
                    Quaternion desired = Quaternion.LookRotation(aim) * turretRest;
                    turret.rotation = Quaternion.Slerp(turret.rotation, desired, 1 - Mathf.Exp(-6 * dt));
                }
            }
            if (visual)
            {
                if (unit.BuildProgress < 1)
                {
                    visual.localScale = new Vector3(visualScale.x, visualScale.y * Mathf.Lerp(.06f, 1, unit.BuildProgress), visualScale.z);
                    weldClock -= dt;
                    if (weldClock <= 0 && unit.Builder && effects && unit.Simulation.IsVisible(unit))
                    { weldClock = .22f; effects.Welding(Vector3.Lerp(unit.Position, unit.Builder.Position, .65f) + Vector3.up * 1.3f); }
                }
                else
                {
                    visual.localScale = visualScale;
                    if (scaffolding.Count > 0) { foreach (var post in scaffolding) Destroy(post); scaffolding.Clear(); }
                    recoil = Mathf.Max(0, recoil - dt);
                    visual.localPosition = visualPosition + Vector3.back * (Mathf.Sin(recoil / .16f * Mathf.PI) * .10f);
                }
            }
            float travel = Vector3.Distance(previous, transform.position);
            dustClock -= dt; smokeClock -= dt;
            bool visible = unit.Simulation.IsVisible(unit);
            if (visible && travel > .012f && !unit.IsStructure && !unit.IsAircraft && unit.Definition.category != "Infantry" && dustClock <= 0 && effects)
            {
                dustClock = .12f;
                effects.VehicleDust(unit.Position - transform.forward * unit.Radius * .7f, unit.Radius * .5f);
            }
            if (visible && unit.Health < unit.MaxHealth * .5f && unit.BuildProgress >= 1 && unit.Definition.category != "Infantry" && smokeClock <= 0 && effects)
            {
                smokeClock = .22f;
                effects.DamageSmoke(unit.Position + Vector3.up * unit.ScreenHeight * .55f, unit.IsStructure ? 2.5f : 1.2f, unit.Health < unit.MaxHealth * .25f);
            }
            previous = transform.position;
        }
    }
}
