using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonFrontier
{
    /// <summary>
    /// Deterministic, collider-free scenery for Willow Crossing. Navigation and height sampling
    /// use the same dimensions as the visible river and bridges; no terrain assets are required.
    /// </summary>
    public sealed class DemoBattlefield : MonoBehaviour
    {
        public static readonly float[] BridgeZ = { -58f, 6f, 64f };
        public const float MapHalfWidth = 150f;
        public const float MapHalfLength = 105f;
        public const float RiverHalfWidth = 11f;
        public const float BridgeHalfLength = 23f;
        public const float BridgeClearHalfWidth = 7.65f;

        const float BankWidth = 5f;
        const float WaterLevel = -1.6f;
        public const float SceneryHalfWidth = 280f;
        public const float SceneryHalfLength = 270f;
        readonly List<Object> generatedAssets = new List<Object>();
        Material waterMaterial;
        Material grass, sand, concrete, concreteLight, asphalt, paint;
        Material bark, leaves, leavesLight, leavesDark, stone, reeds, foam;
        Material steel, canvas, rust, gravel, darkRubber;
        Texture2D terrainNormal;
        bool built;

        public static float RiverCenter(float z)
        {
            return Mathf.Sin(z * .024f) * 6f + Mathf.Sin(z * .051f + .5f) * 3.5f;
        }

        public static bool IsWalkable(Vector3 p, float radius = 0f)
        {
            radius = Mathf.Max(0f, radius);
            if (Mathf.Abs(p.x) + radius > MapHalfWidth || Mathf.Abs(p.z) + radius > MapHalfLength)
                return false;

            // The parapets occupy the two edges of each bridge, including the solid abutments.
            // Keep units inside the road clearance, even when their centres are over dry land.
            for (int i = 0; i < BridgeZ.Length; i++)
            {
                float along = Mathf.Abs(p.x - RiverCenter(BridgeZ[i]));
                float across = Mathf.Abs(p.z - BridgeZ[i]);
                if (along < BridgeHalfLength + radius &&
                    across > BridgeClearHalfWidth - radius && across < 9.15f + radius)
                    return false;
                if (along <= BridgeHalfLength && across <= BridgeClearHalfWidth - radius)
                    return true;
            }

            // Sample the front and rear of the footprint so the meander cannot clip a wide unit.
            return Mathf.Abs(p.x - RiverCenter(p.z)) >= RiverHalfWidth + radius &&
                   Mathf.Abs(p.x - RiverCenter(p.z - radius)) >= RiverHalfWidth + radius &&
                   Mathf.Abs(p.x - RiverCenter(p.z + radius)) >= RiverHalfWidth + radius;
        }

        public static float GroundHeight(Vector3 p)
        {
            for (int i = 0; i < BridgeZ.Length; i++)
            {
                float along = Mathf.Abs(p.x - RiverCenter(BridgeZ[i]));
                if (Mathf.Abs(p.z - BridgeZ[i]) < 9f && along <= BridgeHalfLength)
                    return .2f;
                if (Mathf.Abs(p.z - BridgeZ[i]) < 7.3f && along < 35f)
                    return Mathf.Lerp(.2f, MeadowHeight(p.x, p.z),
                        Mathf.Clamp01((along - BridgeHalfLength) / 12f));
            }
            return SurfaceHeight(p.x, p.z);
        }

        public static Vector3 ClampToMap(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, -MapHalfWidth + .5f, MapHalfWidth - .5f);
            p.z = Mathf.Clamp(p.z, -MapHalfLength + .5f, MapHalfLength - .5f);
            p.y = GroundHeight(p);
            return p;
        }

        static float SurfaceHeight(float x, float z)
        {
            float distance = Mathf.Abs(x - RiverCenter(z));
            if (distance < RiverHalfWidth) return WaterLevel - .55f;
            float bank = Mathf.Clamp01((distance - RiverHalfWidth) / BankWidth);
            return Mathf.Lerp(WaterLevel - .08f, MeadowHeight(x, z), Mathf.SmoothStep(0f, 1f, bank));
        }

        static float MeadowHeight(float x, float z)
        {
            float nearestBase = Mathf.Min(Vector2.Distance(new Vector2(x, z), new Vector2(-106f, 0f)),
                Vector2.Distance(new Vector2(x, z), new Vector2(106f, 0f)));
            float clearing = Mathf.SmoothStep(0f, 1f, (nearestBase - 45f) / 18f);
            float meadow = (Mathf.PerlinNoise(x * .022f + 15f, z * .025f + 39f) - .5f) * .16f;
            float edge = Mathf.Max(Mathf.Abs(x) - 153f, Mathf.Abs(z) - 110f);
            float hills = Mathf.SmoothStep(0f, 1f, edge / 65f) *
                          (6f + 19f * Mathf.PerlinNoise(x * .016f + 61f, z * .016f + 21f));
            // Low terrain beside the river lets the water disappear naturally into the horizon.
            hills *= Mathf.SmoothStep(0f, 1f, (Mathf.Abs(x - RiverCenter(z)) - 17f) / 55f);
            return meadow * clearing + hills;
        }

        public void Build()
        {
            ClearGenerated();
            CreateMaterials();
            CreateLandscape();
            CreateRiver();
            CreateBridges();
            CreateVegetation();
            CreateMilitaryDressing();
            CreateLighting();
            built = true;
        }

        void Update()
        {
            if (!built || !waterMaterial) return;
            waterMaterial.SetTextureOffset("_BaseMap", new Vector2(Mathf.Sin(Time.time * .035f) * .006f,
                -Time.time * .007f));
        }

        void CreateMaterials()
        {
            grass = MakeMaterial("Willow / surveyed terrain, tracks and base hardstands", Color.white, .08f);
            grass.SetTexture("_BaseMap", MakeTerrainAtlas());
            grass.SetTexture("_BumpMap", terrainNormal);
            grass.SetFloat("_BumpScale", .42f);
            grass.EnableKeyword("_NORMALMAP");
            sand = MakeMaterial("Willow / riverbank sand", Color.white, .08f);
            sand.SetTexture("_BaseMap", MakeGroundTexture(true));
            concrete = MakeMaterial("Willow / weathered poured concrete", new Color(.52f, .5f, .44f), .12f);
            concreteLight = MakeMaterial("Willow / limestone coping", new Color(.64f, .61f, .52f), .12f);
            asphalt = MakeMaterial("Willow / worn aggregate asphalt", new Color(.26f, .26f, .235f), .17f);
            paint = MakeMaterial("Willow / faded ivory lane paint", new Color(.7f, .66f, .44f), .08f);
            bark = MakeMaterial("Willow / fissured tree bark", new Color(.235f, .185f, .12f), .04f);
            leaves = MakeMaterial("Willow / olive woodland", new Color(.235f, .285f, .115f), .03f);
            leavesLight = MakeMaterial("Willow / dusty sunlit foliage", new Color(.34f, .36f, .16f), .04f);
            leavesDark = MakeMaterial("Willow / deep woodland foliage", new Color(.16f, .225f, .105f), .025f);
            stone = MakeMaterial("Willow / weathered sedimentary rock", new Color(.5f, .46f, .36f), .07f);
            reeds = MakeMaterial("Willow / dry grass seed heads", new Color(.49f, .43f, .235f), .03f);
            foam = MakeMaterial("Willow / subdued river glints", new Color(.29f, .38f, .32f), .6f);
            steel = MakeMaterial("Willow / painted military steel", new Color(.235f, .26f, .205f), .3f);
            steel.SetFloat("_Metallic", .38f);
            canvas = MakeMaterial("Willow / sandbag hessian", new Color(.51f, .45f, .3f), .03f);
            rust = MakeMaterial("Willow / oxidised steel", new Color(.35f, .205f, .115f), .17f);
            gravel = MakeMaterial("Willow / riverbed pebbles", new Color(.4f, .385f, .305f), .1f);
            darkRubber = MakeMaterial("Willow / tar and rubber", new Color(.11f, .12f, .105f), .1f);
            Texture2D grain = MakeSurfaceGrain();
            foreach (Material material in new[] { concrete, concreteLight, asphalt, bark, stone, steel, canvas, rust, gravel })
                material.SetTexture("_BaseMap", grain);
            Texture2D foliage = MakeFoliageTexture();
            leaves.SetTexture("_BaseMap", foliage);
            leavesLight.SetTexture("_BaseMap", foliage);
            leavesDark.SetTexture("_BaseMap", foliage);
            waterMaterial = MakeMaterial("Willow / silty flowing river", Color.white, .79f);
            Shader riverShader = Resources.Load<Shader>("BattlefieldWater");
            if (!riverShader) riverShader = Shader.Find("NeonFrontier/Battlefield Water");
            if (riverShader) waterMaterial.shader = riverShader;
            waterMaterial.SetTexture("_BaseMap", MakeWaterTexture());
            waterMaterial.SetFloat("_Metallic", .24f);
        }

        Material MakeMaterial(string label, Color color, float smoothness)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader) { name = label, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetFloat("_Smoothness", smoothness);
            generatedAssets.Add(material);
            return material;
        }

        Texture2D MakeTerrainAtlas()
        {
            // Original world-space artwork. The atlas contains fine grain, large vegetation
            // patches, traffic wear and individually jointed concrete rather than a stretched tile.
            const int size = 2048;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = "Willow / 2048 surveyed ground atlas", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
            var pixels = new Color[size * size];
            var random = new System.Random(74922);
            for (int row = 0; row < size; row++)
            {
                float z = Mathf.Lerp(-SceneryHalfLength, SceneryHalfLength, row / (float)(size - 1));
                float river = RiverCenter(z);
                for (int col = 0; col < size; col++)
                {
                    float x = Mathf.Lerp(-SceneryHalfWidth, SceneryHalfWidth, col / (float)(size - 1));
                    float macro = Mathf.PerlinNoise(x * .021f + 34f, z * .021f + 68f);
                    float patch = Mathf.PerlinNoise(x * .095f + 8f, z * .095f + 53f);
                    float grain = Mathf.PerlinNoise(x * .83f + 79f, z * .83f + 13f);
                    float speckle = (float)random.NextDouble();
                    float dry = Mathf.Clamp01((macro - .34f) * 2.6f + (patch - .5f) * .9f);
                    Color ground = Color.Lerp(new Color(.285f, .315f, .18f),
                        new Color(.49f, .435f, .295f), dry);
                    ground *= .82f + grain * .29f + speckle * .14f;
                    float riverDistance = Mathf.Abs(x - river);
                    float bankSoil = (1f - SmoothRange(16f, 23f + patch * 5f, riverDistance)) * .77f;
                    ground = Color.Lerp(ground, new Color(.47f, .425f, .29f) * (.83f + grain * .32f), bankSoil);

                    if (Mathf.Abs(x) < 152f && Mathf.Abs(z) < 107f && riverDistance > 15f)
                    {
                        float roadDistance = RoadDistance(x, z);
                        float wear = 1f - SmoothRange(3.25f, 7.2f + patch * 1.7f, roadDistance);
                        Color dirt = new Color(.465f, .395f, .265f) * (.88f + grain * .24f + speckle * .12f);
                        float rut = 1f - SmoothRange(.18f, .46f, Mathf.Abs(roadDistance - 2.05f));
                        dirt *= 1f - rut * (.085f + patch * .12f);
                        ground = Color.Lerp(ground, dirt, wear * .9f);

                        float facing = x < 0f ? 1f : -1f;
                        float localX = x * facing, localZ = z * facing;
                        float baseDust = (1f - SmoothRange(.65f, 1f,
                            Mathf.Max(Mathf.Abs((localX + 116f) / 32f), Mathf.Abs((localZ + 36f) / 45f)))) * .25f;
                        ground = Color.Lerp(ground, dirt, baseDust);
                        ground = ApronPixel(ground, localX + 116f, localZ + 39f, 10.4f, 10.2f, grain, patch);
                        ground = ApronPixel(ground, localX + 135f, localZ + 64f, 7.3f, 7.8f, grain, patch);
                        ground = ApronPixel(ground, localX + 134f, localZ + 14f, 9.2f, 9.4f, grain, patch);
                        ground = ApronPixel(ground, localX + 105f, localZ + 5f, 8.2f, 8.8f, grain, patch);
                        ground = ApronPixel(ground, localX + 94f, localZ + 43f, 11.2f, 10.6f, grain, patch);

                        // Small abandoned shell scars add history without changing traversability.
                        float crater = Mathf.Min(Vector2.Distance(new Vector2(x, z), new Vector2(-42f, 34f)),
                            Vector2.Distance(new Vector2(x, z), new Vector2(47f, -31f)));
                        if (crater < 4.5f)
                        {
                            float depression = 1f - SmoothRange(.8f, 3.8f, crater + (patch - .5f) * .7f);
                            ground = Color.Lerp(ground, new Color(.245f, .225f, .175f), depression * .6f);
                            ground *= 1f + Mathf.Exp(-Mathf.Pow((crater - 3.35f) * 2f, 2f)) * .12f;
                        }
                    }
                    pixels[row * size + col] = ground;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            generatedAssets.Add(texture);

            terrainNormal = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Willow / soil aggregate relief", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
            var normals = new Color32[pixels.Length];
            for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
            {
                float dx = (pixels[row * size + Mathf.Max(0, col - 1)].grayscale -
                    pixels[row * size + Mathf.Min(size - 1, col + 1)].grayscale) * 2.6f;
                float dz = (pixels[Mathf.Max(0, row - 1) * size + col].grayscale -
                    pixels[Mathf.Min(size - 1, row + 1) * size + col].grayscale) * 2.6f;
                Vector3 n = new Vector3(dx, dz, 1f).normalized;
                // Alpha stays one so URP's RG-or-AG unpacking preserves the red channel.
                normals[row * size + col] = new Color(n.x * .5f + .5f, n.y * .5f + .5f,
                    n.z * .5f + .5f, 1f);
            }
            terrainNormal.SetPixels32(normals);
            terrainNormal.Apply(true, true);
            generatedAssets.Add(terrainNormal);
            return texture;
        }

        static float RoadDistance(float x, float z)
        {
            float ax = Mathf.Abs(x);
            float localZ = x < 0f ? z : -z;
            float d = 500f;
            if (z > -93f && z < 94f)
                d = Mathf.Abs(ax - (69f + Mathf.Sin(z * .045f) * 2.2f));
            if (ax > 62f && ax < 146f) d = Mathf.Min(d, Mathf.Abs(localZ + 26f));
            if (localZ > -77f && localZ < 13f) d = Mathf.Min(d, Mathf.Abs(ax - 119f));
            if (ax < 76f)
                for (int bridge = 0; bridge < BridgeZ.Length; bridge++)
                    d = Mathf.Min(d, Mathf.Abs(z - BridgeZ[bridge]));
            return d;
        }

        static Color ApronPixel(Color ground, float x, float z, float halfX, float halfZ, float grain, float patch)
        {
            float edge = Mathf.Max(Mathf.Abs(x) - halfX, Mathf.Abs(z) - halfZ);
            if (edge > 2.5f) return ground;
            float apron = 1f - SmoothRange(-.2f, .45f, edge + (grain - .5f) * .5f);
            float dust = 1f - SmoothRange(.1f, 2.5f, edge);
            ground = Color.Lerp(ground, new Color(.46f, .415f, .31f), dust * .24f);
            float seamX = Mathf.Abs(Mathf.Repeat(x + 22f, 4.4f) - 2.2f);
            float seamZ = Mathf.Abs(Mathf.Repeat(z + 22f, 4.4f) - 2.2f);
            float joint = 1f - SmoothRange(.045f, .14f, Mathf.Min(seamX, seamZ));
            float slab = Mathf.PerlinNoise(Mathf.Floor((x + 22f) / 4.4f) * 2.7f + 14f,
                Mathf.Floor((z + 22f) / 4.4f) * 2.7f + 63f);
            Color concretePixel = new Color(.45f, .435f, .375f) *
                (.81f + slab * .23f + grain * .12f - joint * .2f);
            float stain = SmoothRange(.53f, .76f, patch) * .14f;
            return Color.Lerp(ground, concretePixel * (1f - stain), apron);
        }

        static float SmoothRange(float lower, float upper, float value)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lower, upper, value));
        }

        Texture2D MakeSurfaceGrain()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = "Willow / stone and steel weathering", wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
            var pixels = new Color32[size * size];
            var random = new System.Random(422);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float patch = TileNoise(x / (float)size, y / (float)size, 7f);
                float grain = (float)random.NextDouble();
                float streak = TileNoise(x / (float)size, y / (float)size, 32f);
                float value = .7f + patch * .2f + grain * .15f + streak * .07f;
                if (grain < .025f) value *= .62f;
                pixels[y * size + x] = new Color(value, value * .988f, value * .96f);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            generatedAssets.Add(texture);
            return texture;
        }

        Texture2D MakeFoliageTexture()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = "Willow / irregular leaf clusters", wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float broad = TileNoise(x / (float)size, y / (float)size, 6f);
                float leaf = TileNoise(x / (float)size, y / (float)size, 26f);
                float shade = .52f + broad * .4f + leaf * .31f;
                pixels[y * size + x] = new Color(shade, shade, shade * .91f);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            generatedAssets.Add(texture);
            return texture;
        }

        Texture2D MakeGroundTexture(bool shoreline)
        {
            const int size = 384;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = shoreline ? "Riverbank grain" : "Meadow grass and clover",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            var pixels = new Color[size * size];
            var random = new System.Random(shoreline ? 71 : 38);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float broad = TileNoise(x / (float)size, y / (float)size, 4f);
                float medium = TileNoise(x / (float)size, y / (float)size, 19f);
                float speckle = (float)random.NextDouble();
                Color dark = shoreline ? new Color(.36f, .325f, .235f) : new Color(.28f, .315f, .18f);
                Color light = shoreline ? new Color(.58f, .535f, .37f) : new Color(.47f, .455f, .27f);
                pixels[y * size + x] = Color.Lerp(dark, light, broad * .58f + medium * .29f + speckle * .13f);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            generatedAssets.Add(texture);
            return texture;
        }

        static float TileNoise(float u, float v, float scale)
        {
            // Crossfade four samples for a seamless texture without visible tile boundaries.
            float a = Mathf.PerlinNoise(u * scale + 7.2f, v * scale + 12.8f);
            float b = Mathf.PerlinNoise((u - 1f) * scale + 7.2f, v * scale + 12.8f);
            float c = Mathf.PerlinNoise(u * scale + 7.2f, (v - 1f) * scale + 12.8f);
            float d = Mathf.PerlinNoise((u - 1f) * scale + 7.2f, (v - 1f) * scale + 12.8f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        Texture2D MakeWaterTexture()
        {
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, true)
            {
                name = "Longitudinal river currents", wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear, anisoLevel = 4
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size;
                float v = y / (float)size;
                float wave = Mathf.Sin((u * 15f + Mathf.Sin(v * Mathf.PI * 6f) * .16f) * Mathf.PI * 2f);
                float ripple = Mathf.Sin((v * 28f + Mathf.Sin(u * Mathf.PI * 8f) * .6f) * Mathf.PI * 2f);
                float noise = TileNoise(u, v, 9f);
                pixels[y * size + x] = Color.Lerp(new Color(.12f, .235f, .215f), new Color(.23f, .345f, .29f),
                    Mathf.Clamp01(.44f + wave * .065f + ripple * .045f + (noise - .5f) * .62f));
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            generatedAssets.Add(texture);
            return texture;
        }

        void CreateLandscape()
        {
            for (int side = -1; side <= 1; side += 2)
            {
                const int rows = 180;
                const int columns = 76;
                var meadow = new MeshData();
                var bank = new MeshData();
                for (int row = 0; row <= rows; row++)
                {
                    float z = Mathf.Lerp(-SceneryHalfLength, SceneryHalfLength, row / (float)rows);
                    float river = RiverCenter(z);
                    for (int col = 0; col <= columns; col++)
                    {
                        float x = Mathf.Lerp(river + side * (RiverHalfWidth + BankWidth),
                            side * SceneryHalfWidth, col / (float)columns);
                        meadow.Vertex(new Vector3(x, MeadowHeight(x, z), z),
                            new Vector2((x + SceneryHalfWidth) / (SceneryHalfWidth * 2f),
                                (z + SceneryHalfLength) / (SceneryHalfLength * 2f)));
                    }
                    for (int col = 0; col <= 5; col++)
                    {
                        float distance = RiverHalfWidth + BankWidth * col / 5f;
                        float x = river + side * distance;
                        bank.Vertex(new Vector3(x, SurfaceHeight(x, z), z), new Vector2(x / 12f, z / 12f));
                    }
                }
                meadow.Grid(columns, rows, side > 0);
                bank.Grid(5, rows, side > 0);
                RenderMesh(side < 0 ? "West meadow and rolling hills" : "East meadow and rolling hills", meadow, grass);
                RenderMesh(side < 0 ? "West sandy riverbank" : "East sandy riverbank", bank, sand);
            }
        }

        void CreateRiver()
        {
            const int rows = 270;
            const int columns = 8;
            var river = new MeshData();
            for (int row = 0; row <= rows; row++)
            {
                float z = Mathf.Lerp(-SceneryHalfLength, SceneryHalfLength, row / (float)rows);
                for (int col = 0; col <= columns; col++)
                {
                    float t = col / (float)columns;
                    float x = RiverCenter(z) + Mathf.Lerp(-RiverHalfWidth, RiverHalfWidth, t);
                    river.Vertex(new Vector3(x, WaterLevel, z), new Vector2(t * 1.5f, z / 28f));
                }
            }
            river.Grid(columns, rows, true);
            RenderMesh("Willow River / flowing silty water", river, waterMaterial, false);

            var currents = new MeshData();
            var random = new System.Random(413);
            for (int i = 0; i < 235; i++)
            {
                float z = Range(random, -SceneryHalfLength, SceneryHalfLength);
                float x = RiverCenter(z) + Range(random, -9.7f, 9.7f);
                float length = Range(random, .25f, 1.8f);
                float width = Range(random, .014f, .04f);
                currents.Quad(new Vector3(x - width, WaterLevel + .018f, z - length),
                    new Vector3(x - width * .2f, WaterLevel + .018f, z + length),
                    new Vector3(x + width * .2f, WaterLevel + .018f, z + length),
                    new Vector3(x + width, WaterLevel + .018f, z - length));
            }
            RenderMesh("Small river current glints", currents, foam, false);

            var stones = new MeshData();
            var wetEdge = new MeshData();
            for (int side = -1; side <= 1; side += 2)
            {
                for (int step = 0; step < 360; step++)
                {
                    float z0 = -SceneryHalfLength + step * 1.5f;
                    float z1 = z0 + 1.5f;
                    float outer0 = RiverHalfWidth + .9f + Mathf.PerlinNoise(z0 * .31f + 18f, 2f) * .5f;
                    float outer1 = RiverHalfWidth + .9f + Mathf.PerlinNoise(z1 * .31f + 18f, 2f) * .5f;
                    Vector3 a = BankPoint(z0, side * RiverHalfWidth);
                    Vector3 b = BankPoint(z1, side * RiverHalfWidth);
                    Vector3 c = BankPoint(z1, side * outer1);
                    Vector3 d = BankPoint(z0, side * outer0);
                    if (side > 0) wetEdge.Quad(a, b, c, d); else wetEdge.Quad(d, c, b, a);
                }
                for (int i = 0; i < 280; i++)
                {
                    float z = Range(random, -165f, 165f);
                    float x = RiverCenter(z) + side * Range(random, 12f, 15.7f);
                    if (NearBridge(x, z, 25f, 10f)) continue;
                    float size = Range(random, .12f, .65f);
                    Vector3 position = new Vector3(x, SurfaceHeight(x, z) + size * .12f, z);
                    stones.Ellipsoid(position, new Vector3(size * 1.1f, size * .47f, size * .72f),
                        Range(random, 0f, 360f), 7, 4);
                }
            }
            RenderMesh("Silt and wet stones along the waterline", wetEdge, gravel, false);
            RenderMesh("Riverbank individual worn pebbles", stones, stone);
        }

        static Vector3 BankPoint(float z, float distance)
        {
            float x = RiverCenter(z) + distance;
            return new Vector3(x, Mathf.Max(WaterLevel + .01f, SurfaceHeight(x, z) + .015f), z);
        }

        void CreateBridges()
        {
            var foundations = new MeshData();
            var trim = new MeshData();
            var roads = new MeshData();
            var markings = new MeshData();
            var iron = new MeshData();
            var stains = new MeshData();
            for (int i = 0; i < BridgeZ.Length; i++)
            {
                float z = BridgeZ[i];
                float x = RiverCenter(z);
                foundations.Box(new Vector3(x, -.38f, z), new Vector3(46f, 1.16f, 18f));
                roads.Box(new Vector3(x, .211f, z), new Vector3(45.85f, .02f, 14.5f));
                for (int side = -1; side <= 1; side += 2)
                {
                    // Thick end abutments and tapered approach roads anchor the span to each bank.
                    foundations.Box(new Vector3(x + side * 20f, -.85f, z), new Vector3(5.5f, 2.1f, 19.3f));
                    foundations.Box(new Vector3(x + side * 7.2f, -1.9f, z), new Vector3(1.7f, 3.2f, 15.4f));
                    foundations.Box(new Vector3(x + side * 7.2f, -3.1f, z), new Vector3(3f, .7f, 17f));
                    AddApproach(roads, x, z, side);
                    trim.Box(new Vector3(x, .32f, z + side * 7.65f), new Vector3(46f, .25f, .6f));
                    foundations.Box(new Vector3(x, .62f, z + side * 8.3f), new Vector3(46f, .84f, .65f));
                    trim.Box(new Vector3(x, 1.08f, z + side * 8.3f), new Vector3(46.4f, .18f, .9f));
                    for (int post = -5; post <= 5; post++)
                    {
                        trim.Box(new Vector3(x + post * 4.25f, .74f, z + side * 8.3f), new Vector3(.65f, 1.13f, .9f));
                        iron.Box(new Vector3(x + post * 4.25f, 1.195f, z + side * 8.3f), new Vector3(.18f, .06f, .2f));
                    }
                    markings.Box(new Vector3(x, .231f, z + side * 6.75f), new Vector3(44.5f, .008f, .11f));
                    for (int end = -1; end <= 1; end += 2)
                    {
                        Vector3 marker = new Vector3(x + end * 20.4f, 1.44f, z + side * 8.3f);
                        iron.Box(marker, new Vector3(.18f, .65f, .2f));
                        markings.Box(marker + Vector3.up * .27f, new Vector3(.28f, .17f, .28f));
                        // Warning stripes sit on the solid parapets, outside the navigation clearance.
                        for (int stripe = 0; stripe < 5; stripe++)
                            iron.Box(new Vector3(x + end * (17.7f + stripe * .8f), 1.182f, z + side * 8.3f),
                                new Vector3(.38f, .028f, .89f));
                    }
                    for (int patch = -3; patch <= 3; patch++)
                        stains.Box(new Vector3(x + patch * 5.25f + .5f, .231f, z + side * 3.1f),
                            new Vector3(2.4f, .006f, .1f));
                }
                for (int dash = -5; dash <= 5; dash++)
                    markings.Box(new Vector3(x + dash * 4.1f, .234f, z), new Vector3(2.05f, .008f, .16f));
                // Visible expansion joints are shallow dark cuts across the road.
                for (int joint = -1; joint <= 1; joint++)
                    foundations.Box(new Vector3(x + joint * 15f, .236f, z), new Vector3(.045f, .008f, 14.5f));
            }
            RenderMesh("Three concrete bridge spans / piers and parapets", foundations, concrete);
            RenderMesh("Bridge coping stones and raised curbs", trim, concreteLight);
            RenderMesh("Bridge carriageways and field approaches", roads, asphalt);
            RenderMesh("Faded bridge road markings", markings, paint, false);
            RenderMesh("Bridge tie bolts and hazard markers", iron, darkRubber);
            RenderMesh("Traffic wear on bridge decks", stains, darkRubber, false);
        }

        static void AddApproach(MeshData mesh, float x, float z, int side)
        {
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float a = BridgeHalfLength + i * 2f;
                float b = a + 2f;
                Vector3 p0 = new Vector3(x + side * a, 0f, z - 7.25f);
                Vector3 p1 = new Vector3(x + side * a, 0f, z + 7.25f);
                Vector3 p2 = new Vector3(x + side * b, 0f, z + 7.25f);
                Vector3 p3 = new Vector3(x + side * b, 0f, z - 7.25f);
                p0.y = GroundHeight(p0) + .014f;
                p1.y = GroundHeight(p1) + .014f;
                p2.y = GroundHeight(p2) + .014f;
                p3.y = GroundHeight(p3) + .014f;
                if (side > 0) mesh.Quad(p0, p1, p2, p3);
                else mesh.Quad(p3, p2, p1, p0);
            }
        }

        void CreateVegetation()
        {
            var trunks = new MeshData();
            var crowns = new[] { new MeshData(), new MeshData(), new MeshData() };
            var rocks = new MeshData();
            var grasses = new MeshData();
            var random = new System.Random(92641);

            for (int i = 0; i < 310; i++)
            {
                float x = Range(random, -225f, 225f);
                float z = Range(random, -200f, 220f);
                bool perimeter = Mathf.Abs(x) > MapHalfWidth + 5f || Mathf.Abs(z) > MapHalfLength + 5f;
                if (!perimeter || Mathf.Abs(x - RiverCenter(z)) < 22f || NearBase(x, z, 57f)) continue;
                float height = Range(random, 6.5f, 13f);
                Vector3 root = new Vector3(x, MeadowHeight(x, z), z);
                trunks.Cone(root, height * .7f, height * .041f, height * .017f, 9);
                int palette = i % crowns.Length;
                for (int branch = 0; branch < 7; branch++)
                {
                    float angle = branch * 2.39996f + i * .51f;
                    float spread = height * Range(random, .13f, .3f);
                    Vector3 crown = root + new Vector3(Mathf.Cos(angle) * spread,
                        height * Range(random, .57f, .85f), Mathf.Sin(angle) * spread);
                    trunks.Beam(root + Vector3.up * height * .39f, crown, height * .014f, 6);
                    crowns[(palette + branch) % 3].Ellipsoid(crown,
                        new Vector3(height * .205f, height * Range(random, .18f, .245f), height * .2f),
                        i * 37f + branch * 17f, 10, 6);
                }
                // A separate central leader gives each tree an asymmetric, broken silhouette.
                crowns[palette].Ellipsoid(root + Vector3.up * height * .88f,
                    new Vector3(height * .16f, height * .16f, height * .18f), i * 29f, 10, 6);
            }

            for (int i = 0; i < 180; i++)
            {
                float x = Range(random, -168f, 168f);
                float z = Range(random, -122f, 133f);
                if ((Mathf.Abs(x) < 153f && Mathf.Abs(z) < 108f) || NearBase(x, z, 51f) ||
                    Mathf.Abs(x - RiverCenter(z)) < 18f) continue;
                Vector3 root = new Vector3(x, MeadowHeight(x, z), z);
                float size = Range(random, .6f, 3.8f);
                rocks.Ellipsoid(root + Vector3.up * size * .15f,
                    new Vector3(size, size * .56f, size * .72f), i * 39f, 7, 4);
                rocks.Ellipsoid(root + new Vector3(size * .7f, size * .04f, size * .25f),
                    new Vector3(size * .54f, size * .3f, size * .42f), i * 23f, 6, 4);
            }

            // Small clusters keep open ground readable; all blades share one mesh and material.
            for (int i = 0; i < 2100; i++)
            {
                float z = Range(random, -125f, 130f);
                float x;
                if (i < 900) x = RiverCenter(z) + (i % 2 == 0 ? -1f : 1f) * Range(random, 14.8f, 19f);
                else x = Range(random, -163f, 163f);
                if (NearBase(x, z, 48f) || NearBridge(x, z, 38f, 12f) ||
                    Mathf.Abs(x - RiverCenter(z)) < 14.5f || RoadDistance(x, z) < 8f) continue;
                for (int blade = 0; blade < 5; blade++)
                {
                    Vector3 root = new Vector3(x + Range(random, -.36f, .36f), 0f, z + Range(random, -.36f, .36f));
                    root.y = SurfaceHeight(root.x, root.z) + .015f;
                    float height = Range(random, .17f, .57f);
                    Vector3 edge = new Vector3(Range(random, .06f, .14f), 0f, Range(random, -.12f, .12f));
                    Vector3 tip = root + new Vector3(.1f, height, .08f);
                    grasses.Triangle(root - edge, tip, root + edge);
                    grasses.Triangle(root + edge, tip, root - edge);
                }
            }
            RenderMesh("Perimeter trees / trunks", trunks, bark);
            RenderMesh("Perimeter trees / summer crowns", crowns[0], leaves);
            RenderMesh("Perimeter trees / sunlit crowns", crowns[1], leavesLight);
            RenderMesh("Perimeter trees / deep crowns", crowns[2], leavesDark);
            RenderMesh("Scattered perimeter fieldstone", rocks, stone);
            RenderMesh("Riverbank reeds and meadow grass tufts", grasses, reeds, false);
        }

        static bool NearBase(float x, float z, float radius)
        {
            return new Vector2(x + 106f, z).sqrMagnitude < radius * radius ||
                   new Vector2(x - 106f, z).sqrMagnitude < radius * radius;
        }

        void CreateMilitaryDressing()
        {
            // These larger props live beyond the playable rectangle. They give the bases
            // context while every point accepted by the navigation API stays unobstructed.
            var metal = new MeshData();
            var corroded = new MeshData();
            var timber = new MeshData();
            var foundations = new MeshData();
            var sandbags = new MeshData();
            var black = new MeshData();
            var yellow = new MeshData();
            for (int side = -1; side <= 1; side += 2)
            {
                // Broken lengths of perimeter fence preserve views from the command camera.
                for (int section = 0; section < 13; section++)
                {
                    float z = (-89f + section * 7f) * -side;
                    float nextZ = z + 6.8f * -side;
                    if (section == 4 || section == 9) continue;
                    Vector3 a = SceneryPoint(side * 154f, z);
                    Vector3 b = SceneryPoint(side * 154f, nextZ);
                    metal.Beam(a, a + Vector3.up * 2.05f, .075f, 6);
                    metal.Beam(b, b + Vector3.up * 2.05f, .075f, 6);
                    for (int rail = 0; rail < 3; rail++)
                    {
                        float height = .5f + rail * .58f;
                        metal.Beam(a + Vector3.up * height, b + Vector3.up * height, .025f, 4);
                    }
                    for (int wire = 0; wire < 7; wire++)
                    {
                        Vector3 low = Vector3.Lerp(a, b, wire / 7f) + Vector3.up * .3f;
                        Vector3 high = Vector3.Lerp(a, b, (wire + 1f) / 7f) + Vector3.up * 1.85f;
                        metal.Beam(low, high, .012f, 3);
                    }
                }

                Vector3 tower = SceneryPoint(side * 162f, side * 83f);
                foundations.Box(tower + Vector3.up * .18f, new Vector3(5.4f, .36f, 5.4f));
                for (int cornerX = -1; cornerX <= 1; cornerX += 2)
                for (int cornerZ = -1; cornerZ <= 1; cornerZ += 2)
                {
                    Vector3 foot = tower + new Vector3(cornerX * 1.8f, .25f, cornerZ * 1.8f);
                    metal.Beam(foot, foot + Vector3.up * 7.1f, .13f, 8);
                    metal.Beam(foot, tower + new Vector3(-cornerX * 1.8f, 4.9f, cornerZ * 1.8f), .055f, 5);
                }
                timber.Box(tower + Vector3.up * 5.2f, new Vector3(4.4f, .22f, 4.4f));
                metal.Box(tower + Vector3.up * 7.4f, new Vector3(5.2f, .2f, 5.2f));
                for (int edge = -1; edge <= 1; edge += 2)
                {
                    metal.Box(tower + new Vector3(0f, 5.85f, edge * 2.03f), new Vector3(4.3f, 1f, .11f));
                    metal.Box(tower + new Vector3(edge * 2.03f, 5.85f, 0f), new Vector3(.11f, 1f, 4.3f));
                }
                for (int rung = 0; rung < 13; rung++)
                    metal.Box(tower + new Vector3(2.35f, .45f + rung * .39f, 0f), new Vector3(.14f, .09f, 1.05f));
                metal.Beam(tower + new Vector3(2.35f, .2f, -.57f), tower + new Vector3(2.35f, 5.5f, -.57f), .055f, 5);
                metal.Beam(tower + new Vector3(2.35f, .2f, .57f), tower + new Vector3(2.35f, 5.5f, .57f), .055f, 5);

                for (int container = 0; container < 2; container++)
                {
                    Vector3 position = SceneryPoint(side * (168f + container * 9f), side * 48f);
                    foundations.Box(position + Vector3.up * .1f, new Vector3(7.7f, .2f, 14.2f));
                    MeshData shell = container == 0 ? metal : corroded;
                    shell.Box(position + Vector3.up * 1.55f, new Vector3(5.8f, 3f, 12.2f));
                    metal.Box(position + Vector3.up * 3.1f, new Vector3(6f, .16f, 12.4f));
                    for (int rib = 0; rib < 15; rib++)
                    {
                        float along = -5.65f + rib * .8f;
                        shell.Box(position + new Vector3(-2.95f, 1.57f, along), new Vector3(.11f, 2.83f, .09f));
                        shell.Box(position + new Vector3(2.95f, 1.57f, along), new Vector3(.11f, 2.83f, .09f));
                        shell.Box(position + new Vector3(0f, 3.22f, along), new Vector3(5.7f, .07f, .095f));
                    }
                    for (int door = -1; door <= 1; door += 2)
                    {
                        metal.Box(position + new Vector3(door * 1.43f, 1.57f, -6.16f), new Vector3(2.73f, 2.81f, .08f));
                        yellow.Box(position + new Vector3(door * 2.5f, 2.65f, -6.23f), new Vector3(.18f, .38f, .025f));
                        metal.Beam(position + new Vector3(door * .45f, .35f, -6.28f),
                            position + new Vector3(door * .45f, 2.8f, -6.28f), .065f, 6);
                    }
                }

                Vector3 supply = SceneryPoint(side * 159f, side * 27f);
                for (int crate = 0; crate < 6; crate++)
                {
                    Vector3 p = supply + new Vector3(crate % 3 * side * 1.7f, .68f, crate / 3 * 1.8f);
                    timber.Box(p, new Vector3(1.4f, 1.35f, 1.4f));
                    for (int strap = -1; strap <= 1; strap += 2)
                    {
                        metal.Box(p + new Vector3(strap * .47f, .7f, 0f), new Vector3(.075f, .045f, 1.42f));
                        metal.Box(p + new Vector3(strap * .47f, 0f, -.71f), new Vector3(.075f, 1.35f, .025f));
                    }
                }
                for (int drum = 0; drum < 5; drum++)
                {
                    Vector3 p = supply + new Vector3(side * 7.1f + drum % 2 * 1.15f, 0f, drum / 2 * 1.15f);
                    metal.Beam(p, p + Vector3.up * 1.45f, .48f, 12);
                    black.Beam(p + Vector3.up * .35f, p + Vector3.up * .42f, .495f, 12);
                    black.Beam(p + Vector3.up * 1.08f, p + Vector3.up * 1.15f, .495f, 12);
                    corroded.Box(p + new Vector3(.16f, 1.465f, .12f), new Vector3(.13f, .03f, .13f));
                }
                for (int row = 0; row < 2; row++)
                for (int bag = 0; bag < 11; bag++)
                {
                    Vector3 p = SceneryPoint(side * (155.2f + row * .13f), (9f + bag * 1.18f) * side);
                    sandbags.Ellipsoid(p + new Vector3(0f, .22f + row * .4f, row * .4f),
                        new Vector3(.52f, .25f, .68f), bag * 3.5f, 8, 4);
                }
            }
            RenderMesh("Perimeter logistics / corrugated steel, drums and watchposts", metal, steel);
            RenderMesh("Perimeter logistics / weathered cargo container", corroded, rust);
            RenderMesh("Perimeter logistics / timber supply crates", timber, bark);
            RenderMesh("Perimeter logistics / poured foundations", foundations, concrete);
            RenderMesh("Perimeter logistics / stacked hessian sandbags", sandbags, canvas);
            RenderMesh("Perimeter logistics / barrel reinforcing hoops", black, darkRubber);
            RenderMesh("Perimeter logistics / faded cargo safety labels", yellow, paint);
        }

        static Vector3 SceneryPoint(float x, float z)
        {
            return new Vector3(x, MeadowHeight(x, z), z);
        }

        static bool NearBridge(float x, float z, float along, float across)
        {
            for (int i = 0; i < BridgeZ.Length; i++)
                if (Mathf.Abs(z - BridgeZ[i]) < across && Mathf.Abs(x - RiverCenter(BridgeZ[i])) < along)
                    return true;
            return false;
        }

        void CreateLighting()
        {
            var sunObject = new GameObject("Warm afternoon sun");
            sunObject.transform.SetParent(transform, false);
            sunObject.transform.rotation = Quaternion.Euler(43f, -36f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .925f, .79f);
            sun.intensity = 1.48f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .8f;
            sun.shadowBias = .55f;
            sun.shadowNormalBias = .32f;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.42f, .475f, .535f);
            RenderSettings.ambientEquatorColor = new Color(.36f, .355f, .29f);
            RenderSettings.ambientGroundColor = new Color(.22f, .205f, .16f);
            RenderSettings.ambientIntensity = .88f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.57f, .59f, .56f);
            RenderSettings.fogStartDistance = 245f;
            RenderSettings.fogEndDistance = 640f;
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader)
            {
                var sky = new Material(skyShader) { name = "Willow / clear summer sky" };
                sky.SetColor("_SkyTint", new Color(.5f, .56f, .64f));
                sky.SetColor("_GroundColor", new Color(.43f, .405f, .33f));
                sky.SetFloat("_AtmosphereThickness", .9f);
                sky.SetFloat("_SunSize", .025f);
                sky.SetFloat("_Exposure", .95f);
                generatedAssets.Add(sky);
                RenderSettings.skybox = sky;
            }

            var volumeObject = new GameObject("Willow / restrained cinematic battlefield grade");
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 12f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Willow / warm military film response";
            generatedAssets.Add(profile);
            volume.sharedProfile = profile;
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.12f);
            color.contrast.Override(9f);
            color.saturation.Override(-6f);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.3f);
            bloom.intensity.Override(.13f);
            bloom.scatter.Override(.55f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.105f);
            vignette.smoothness.Override(.55f);
            foreach (VolumeComponent component in profile.components) generatedAssets.Add(component);
        }

        void RenderMesh(string label, MeshData data, Material material, bool castShadows = true)
        {
            if (data.vertices.Count == 0) return;
            var mesh = new Mesh { name = label, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(data.vertices);
            mesh.SetUVs(0, data.uvs);
            mesh.SetTriangles(data.triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            generatedAssets.Add(mesh);
            var piece = new GameObject(label);
            piece.transform.SetParent(transform, false);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = piece.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            // There are deliberately no scenery colliders: tactical input picks the ground plane.
        }

        static float Range(System.Random random, float min, float max)
        {
            return Mathf.Lerp(min, max, (float)random.NextDouble());
        }

        void ClearGenerated()
        {
            built = false;
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Release(child);
            }
            for (int i = 0; i < generatedAssets.Count; i++) Release(generatedAssets[i]);
            generatedAssets.Clear();
        }

        void OnDestroy()
        {
            for (int i = 0; i < generatedAssets.Count; i++) Release(generatedAssets[i]);
            generatedAssets.Clear();
        }

        static void Release(Object item)
        {
            if (!item) return;
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }

        sealed class MeshData
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();

            public void Vertex(Vector3 p, Vector2 uv)
            {
                vertices.Add(p);
                uvs.Add(uv);
            }

            public void Grid(int columns, int rows, bool positiveX)
            {
                for (int z = 0; z < rows; z++)
                for (int x = 0; x < columns; x++)
                {
                    int a = z * (columns + 1) + x;
                    int b = a + columns + 1;
                    if (positiveX)
                    {
                        triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                        triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                    }
                    else
                    {
                        triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                        triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                    }
                }
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                Vertex(a, Vector2.zero); Vertex(b, Vector2.up); Vertex(c, Vector2.right);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count;
                Vertex(a, Vector2.zero); Vertex(b, Vector2.up);
                Vertex(c, Vector2.one); Vertex(d, Vector2.right);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }

            public void Box(Vector3 center, Vector3 size)
            {
                Vector3 lo = center - size * .5f;
                Vector3 hi = center + size * .5f;
                Vector3 a = new Vector3(lo.x, lo.y, lo.z), b = new Vector3(lo.x, lo.y, hi.z);
                Vector3 c = new Vector3(hi.x, lo.y, hi.z), d = new Vector3(hi.x, lo.y, lo.z);
                Vector3 e = new Vector3(lo.x, hi.y, lo.z), f = new Vector3(lo.x, hi.y, hi.z);
                Vector3 g = new Vector3(hi.x, hi.y, hi.z), h = new Vector3(hi.x, hi.y, lo.z);
                Quad(e, f, g, h); Quad(d, c, b, a);
                Quad(a, b, f, e); Quad(c, d, h, g);
                Quad(b, c, g, f); Quad(d, a, e, h);
            }

            public void Cone(Vector3 root, float height, float lowerRadius, float upperRadius, int sides)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    float b = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 d0 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 d1 = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                    Quad(root + d0 * lowerRadius, root + Vector3.up * height + d0 * upperRadius,
                        root + Vector3.up * height + d1 * upperRadius, root + d1 * lowerRadius);
                }
            }

            public void Beam(Vector3 from, Vector3 to, float radius, int sides)
            {
                Vector3 direction = (to - from).normalized;
                Vector3 across = Vector3.Cross(direction,
                    Mathf.Abs(direction.y) > .94f ? Vector3.forward : Vector3.up).normalized;
                Vector3 forward = Vector3.Cross(across, direction).normalized;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    float b = (i + 1) * Mathf.PI * 2f / sides;
                    Vector3 r0 = (across * Mathf.Cos(a) + forward * Mathf.Sin(a)) * radius;
                    Vector3 r1 = (across * Mathf.Cos(b) + forward * Mathf.Sin(b)) * radius;
                    Quad(from + r0, to + r0, to + r1, from + r1);
                    Triangle(to, to + r1, to + r0);
                    Triangle(from, from + r0, from + r1);
                }
            }

            public void Ellipsoid(Vector3 center, Vector3 size, float rotation, int slices, int rings)
            {
                Quaternion spin = Quaternion.Euler(0f, rotation, 0f);
                for (int ring = 0; ring < rings; ring++)
                for (int slice = 0; slice < slices; slice++)
                {
                    Vector3 a = SpherePoint(ring, slice, rings, slices);
                    Vector3 b = SpherePoint(ring + 1, slice, rings, slices);
                    Vector3 c = SpherePoint(ring + 1, slice + 1, rings, slices);
                    Vector3 d = SpherePoint(ring, slice + 1, rings, slices);
                    a = center + spin * Vector3.Scale(a, size);
                    b = center + spin * Vector3.Scale(b, size);
                    c = center + spin * Vector3.Scale(c, size);
                    d = center + spin * Vector3.Scale(d, size);
                    Quad(a, d, c, b);
                }
            }

            static Vector3 SpherePoint(int ring, int slice, int rings, int slices)
            {
                float latitude = Mathf.PI * ring / rings;
                float longitude = Mathf.PI * 2f * slice / slices;
                float roughness = 1f + Mathf.Sin((slice % slices) * 19.13f + ring * 31.7f) * .055f;
                return new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude) * roughness,
                    Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude) * roughness);
            }
        }
    }
}
