using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonFrontier
{
    /// <summary>Team sight, persistent exploration and matching world/minimap fog.</summary>
    public sealed class BattlefieldVisibility : MonoBehaviour
    {
        const int Width = 160, Height = 112;
        readonly bool[][] visible = { new bool[Width * Height], new bool[Width * Height] };
        readonly bool[] explored = new bool[Width * Height];
        readonly Color32[] pixels = new Color32[Width * Height];
        readonly byte[] rawAlpha = new byte[Width * Height];
        readonly Dictionary<RtsUnit, Renderer[]> renderers = new Dictionary<RtsUnit, Renderer[]>();
        readonly List<RtsUnit> expired = new List<RtsUnit>();
        Texture2D texture;
        Material material;
        Mesh mesh;
        float clock;
        public Texture2D Texture => texture;

        public void Initialize()
        {
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Tactical explored terrain", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            material = new Material(Resources.Load<Shader>("BattlefieldParticle")) { name = "Fog of war", mainTexture = texture, renderQueue = 3010 };
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            const int cols = 186, rows = 180;
            var vertices = new Vector3[(cols + 1) * (rows + 1)]; var uv = new Vector2[vertices.Length]; var colors = new Color[vertices.Length];
            var triangles = new int[cols * rows * 6]; int t = 0;
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= cols; x++)
            {
                int i = y * (cols + 1) + x; float u = x / (float)cols, v = y / (float)rows;
                var p = new Vector3((u - .5f) * DemoBattlefield.SceneryHalfWidth * 2, 0, (v - .5f) * DemoBattlefield.SceneryHalfLength * 2);
                p.y = Mathf.Max(-1.45f, DemoBattlefield.GroundHeight(p) + .2f);
                // Continue the border's visibility across the decorative landscape so the
                // playable rectangle does not cut a bright, hard seam through the scenery.
                vertices[i] = p; uv[i] = new Vector2(p.x / 300 + .5f, p.z / 210 + .5f); colors[i] = Color.white;
                if (x == cols || y == rows) continue;
                triangles[t++] = i; triangles[t++] = i + cols + 1; triangles[t++] = i + 1;
                triangles[t++] = i + 1; triangles[t++] = i + cols + 1; triangles[t++] = i + cols + 2;
            }
            mesh = new Mesh { name = "Terrain following visibility veil", vertices = vertices, uv = uv, colors = colors, triangles = triangles }; mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        public bool IsVisible(Vector3 point, int team)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((point.x / 300 + .5f) * Width), 0, Width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((point.z / 210 + .5f) * Height), 0, Height - 1);
            return visible[Mathf.Clamp(team, 0, 1)][y * Width + x];
        }

        public void Tick(IReadOnlyList<RtsUnit> units, float dt, bool force = false)
        {
            clock -= dt; if (clock > 0 && !force) return; clock = .2f;
            System.Array.Clear(visible[0], 0, visible[0].Length); System.Array.Clear(visible[1], 0, visible[1].Length);
            foreach (var unit in units)
            {
                if (!unit || !unit.IsAlive) continue;
                float radius = unit.IsStructure ? 25 : unit.Key == "Scout" || unit.IsAircraft ? 43 : Mathf.Max(29, Mathf.Min(unit.Definition.range + 7, 57));
                if (unit.BuildProgress < 1) radius = 12;
                float cx = (unit.Position.x / 300 + .5f) * Width, cy = (unit.Position.z / 210 + .5f) * Height;
                float rx = radius / 300 * Width, ry = radius / 210 * Height;
                for (int y = Mathf.Max(0, (int)(cy - ry)); y <= Mathf.Min(Height - 1, (int)(cy + ry)); y++)
                    for (int x = Mathf.Max(0, (int)(cx - rx)); x <= Mathf.Min(Width - 1, (int)(cx + rx)); x++)
                        if (Mathf.Pow((x - cx) / rx, 2) + Mathf.Pow((y - cy) / ry, 2) <= 1) visible[unit.Team][y * Width + x] = true;
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                if (visible[0][i]) explored[i] = true;
                rawAlpha[i] = visible[0][i] ? (byte)0 : explored[i] ? (byte)125 : (byte)238;
            }
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int alpha = 0, weight = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++)
                {
                    int w = (3 - Mathf.Abs(dx)) * (3 - Mathf.Abs(dy));
                    alpha += rawAlpha[Mathf.Clamp(y + dy, 0, Height - 1) * Width + Mathf.Clamp(x + dx, 0, Width - 1)] * w; weight += w;
                }
                pixels[y * Width + x] = new Color32(7, 10, 9, (byte)(alpha / weight));
            }
            texture.SetPixels32(pixels); texture.Apply(false);
            foreach (var unit in units)
            {
                if (!unit || !unit.IsAlive || unit.Team == 0) continue;
                if (!renderers.TryGetValue(unit, out var meshes)) { meshes = unit.GetComponentsInChildren<Renderer>(); renderers.Add(unit, meshes); }
                bool seen = IsVisible(unit.Position, 0);
                foreach (var renderer in meshes) if (renderer && !(renderer is LineRenderer)) renderer.enabled = seen;
                if (!seen) unit.Selected = false;
            }
            expired.Clear(); foreach (var unit in renderers.Keys) if (!unit || !unit.IsAlive) expired.Add(unit);
            foreach (var unit in expired) renderers.Remove(unit);
        }
        void OnDestroy() { if (texture) Destroy(texture); if (material) Destroy(material); if (mesh) Destroy(mesh); }
    }
}
