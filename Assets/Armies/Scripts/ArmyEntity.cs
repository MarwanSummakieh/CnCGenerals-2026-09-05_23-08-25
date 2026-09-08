using UnityEngine;

namespace NeonFrontier
{
    /// <summary>Connects a selectable display model to its editable army design.</summary>
    public sealed class ArmyEntity : MonoBehaviour
    {
        public ArmyDefinition definition;
        public bool isStructure;

        public Bounds DisplayBounds
        {
            get
            {
                var renderers = GetComponentsInChildren<Renderer>();
                var bounds = new Bounds(transform.position, Vector3.one);
                bool found = false;
                foreach (var renderer in renderers)
                {
                    if (renderer is ParticleSystemRenderer || renderer is LineRenderer || !renderer.enabled)
                        continue;
                    if (!found) { bounds = renderer.bounds; found = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                return bounds;
            }
        }
    }
}
