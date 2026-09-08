using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeonFrontier.Editor
{
    /// <summary>Reproducible authoring tool. Generated content stays inside Assets/Armies.</summary>
    public static class ArmyShowcaseBuilder
    {
        public const string Root = "Assets/Armies";
        public const string ScenePath = Root + "/Scenes/NeonFrontier_ArmyShowcase.unity";
        static readonly Color Cyan = new Color(.08f, .83f, 1f);
        static readonly Color Amber = new Color(1f, .36f, .065f);
        static Material ground, platform, inset, metal, cyan, amber, white, muted, road;
        static Font font;

        [MenuItem("Neon Frontier/Skirmish/Refresh Military Models")]
        public static void RefreshMilitaryAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string guid in AssetDatabase.FindAssets("t:ArmyDefinition", new[] { Root + "/Data" }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ArmyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                bool structure = definition.category == "Structure";
                string modelPath = $"{Root}/Models/{(structure ? "Structures" : "Units")}/{definition.id}.fbx";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (!source) throw new InvalidOperationException("Missing model " + modelPath);
                var root = new GameObject(definition.displayName);
                try
                {
                    var visual = UnityEngine.Object.Instantiate(source, root.transform); visual.name = "Model";
                    RemapMaterials(visual, definition.faction);
                    Bounds bounds = WorldBounds(visual);
                    visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    bounds = WorldBounds(visual);
                    var collider = root.AddComponent<BoxCollider>(); collider.center = bounds.center;
                    collider.size = new Vector3(Mathf.Max(bounds.size.x, 1.5f), Mathf.Max(bounds.size.y, 2), Mathf.Max(bounds.size.z, 1.5f));
                    var entity = root.AddComponent<ArmyEntity>(); entity.definition = definition; entity.isStructure = structure;
                    definition.prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/Prefabs/{definition.faction}/{definition.id}.prefab");
                    EditorUtility.SetDirty(definition);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("MILITARY ART: refreshed all model prefabs and materials, preserving roster definitions and working scenes.");
        }

        [MenuItem("Neon Frontier/Build Army Showcase")]
        public static void Build()
        {
            // A menu invocation must not silently discard an artist's unsaved scene.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string folder in new[] { "Materials", "Prefabs/Vanguard", "Prefabs/Dynasty", "Data/Vanguard", "Data/Dynasty", "Scenes", "Settings" })
                Directory.CreateDirectory(Root + "/" + folder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateMaterials();
            ConfigureRendering();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Create transient ScriptableObjects after NewScene, which unloads unattached objects.
            var defs = ArmyRosterCatalog.CreateDefinitions();
            foreach (var def in defs)
            {
                string kind = def.category == "Structure" ? "Structures" : "Units";
                if (AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Models/{kind}/{def.id}.fbx") == null)
                    throw new InvalidOperationException("Missing Blender model: " + def.id + ". Run the Blender generators first.");
            }
            var environment = new GameObject("00 | NIGHTFALL // proving ground").transform;
            MakeEnvironment(environment);
            var savedDefinitions = new List<ArmyDefinition>();
            foreach (string faction in new[] { "Vanguard", "Dynasty" })
            {
                float center = faction == "Vanguard" ? -52 : 52;
                var army = new GameObject(faction == "Vanguard" ? "01 | PACIFIC VANGUARD" : "02 | CRIMSON DYNASTY").transform;
                var factionDefs = defs.Where(d => d.faction == faction).ToList();
                int structure = 0, unit = 0;
                foreach (var transient in factionDefs)
                {
                    string dataPath = $"{Root}/Data/{faction}/{transient.id}.asset";
                    var def = AssetDatabase.LoadAssetAtPath<ArmyDefinition>(dataPath);
                    if (def == null) { def = transient; AssetDatabase.CreateAsset(def, dataPath); }
                    else { EditorUtility.CopySerialized(transient, def); UnityEngine.Object.DestroyImmediate(transient); }
                    def.name = def.id;
                    bool building = def.category == "Structure";
                    string modelPath = $"{Root}/Models/{(building ? "Structures" : "Units")}/{def.id}.fbx";
                    var prefabRoot = new GameObject(def.displayName);
                    var visual = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), prefabRoot.transform);
                    visual.name = "Model";
                    visual.transform.localPosition = Vector3.zero;
                    RemapMaterials(visual, faction);
                    Bounds bounds = WorldBounds(visual);
                    // FBX axis conversion and exporter origins are normalized once in the prefab.
                    visual.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    bounds = WorldBounds(visual);
                    var collider = prefabRoot.AddComponent<BoxCollider>();
                    collider.center = bounds.center;
                    collider.size = new Vector3(Mathf.Max(bounds.size.x, 1.5f), Mathf.Max(bounds.size.y, 2), Mathf.Max(bounds.size.z, 1.5f));
                    var entity = prefabRoot.AddComponent<ArmyEntity>();
                    entity.definition = def;
                    entity.isStructure = building;
                    string prefabPath = $"{Root}/Prefabs/{faction}/{def.id}.prefab";
                    def.prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    UnityEngine.Object.DestroyImmediate(prefabRoot);
                    EditorUtility.SetDirty(def);
                    savedDefinitions.Add(def);
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(def.prefab);
                    instance.transform.SetParent(army);
                    Vector3 pos;
                    int ordinal;
                    if (building)
                    {
                        ordinal = structure++;
                        pos = new Vector3(center + (ordinal % 5 - 2) * 17, .24f, 32 - ordinal / 5 * 22);
                    }
                    else
                    {
                        ordinal = unit++;
                        pos = new Vector3(center + (ordinal % 4 - 1.5f) * 20, .24f, -13 - ordinal / 4 * 13);
                    }
                    instance.transform.position = pos;
                    // A quarter turn makes equipment and turret silhouettes readable from the overview.
                    instance.transform.rotation = Quaternion.Euler(0, building ? 0 : -22, 0);
                    MakeDisplayPad(army, pos, def, ordinal, faction == "Vanguard" ? cyan : amber);
                }
            }
            Camera camera = MakeLightingAndCamera();
            var controller = new GameObject("03 | Tactical roster & camera").AddComponent<ArmyShowcaseController>();
            controller.sceneCamera = camera;
            controller.vanguardFocus = new Vector3(-52, 0, -5);
            controller.dynastyFocus = new Vector3(52, 0, -5);
            controller.roster = savedDefinitions.ToArray();
            controller.gameObject.AddComponent<ArmyShowcaseCapture>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var existing = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            existing.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = existing.ToArray();
            AssetDatabase.SaveAssets();
            Validate();
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt(new Vector3(0, 0, -5), Quaternion.Euler(58, 0, 0), 145);
            Debug.Log("NEON FRONTIER: saved complete showcase with 52 models, prefabs and roster definitions.");
        }

        static void CreateMaterials()
        {
            ground = Mat("Environment_Deck", new Color(.043f, .067f, .09f), .48f, .35f);
            platform = Mat("Environment_Pad", new Color(.10f, .14f, .18f), .58f, .42f);
            inset = Mat("Environment_Inset", new Color(.025f, .04f, .055f), .35f, .28f);
            metal = Mat("Environment_Metal", new Color(.21f, .28f, .32f), .7f, .4f);
            road = Mat("Environment_Road", new Color(.025f, .035f, .043f), .3f, .4f);
            cyan = Mat("Environment_Cyan", Cyan, .35f, .4f, 2.2f);
            amber = Mat("Environment_Amber", Amber, .35f, .4f, 2.2f);
            white = Mat("Environment_Lettering", new Color(.7f, .86f, .92f), 0, .3f, .3f);
            muted = Mat("Environment_Muted", new Color(.19f, .3f, .38f), 0, .3f, .3f);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static Material Mat(string name, Color color, float metallic, float smoothness, float emission = 0)
        {
            string path = $"{Root}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = name;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            if (emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else { material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(material);
            return material;
        }

        static void RemapMaterials(GameObject model, string faction)
        {
            bool usa = faction == "Vanguard";
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                {
                    string key = source ? source.name.Replace(" (Instance)", "") : faction + "_Armor";
                    string lower = key.ToLowerInvariant();
                    Color color;
                    float glow = 0, metallic = .16f, smoothness = .22f;
                    if (lower.Contains("tankarmor")) { color = new Color(.115f,.125f,.13f); metallic = .5f; smoothness = .32f; }
                    else if (lower.Contains("tanksecondary")) { color = new Color(.038f,.046f,.052f); metallic = .55f; smoothness = .35f; }
                    else if (lower.Contains("tankglow")) { color = usa ? new Color(.1f,.85f,1f) : new Color(1f,.24f,.055f); glow = 1.7f; }
                    else if (lower.Contains("tankmetal")) { color = new Color(.20f,.22f,.23f); metallic = .8f; smoothness = .4f; }
                    else if (lower.Contains("tankrubber")) { color = new Color(.012f,.015f,.018f); metallic = .05f; smoothness = .16f; }
                    else if (lower.Contains("tankglass")) { color = new Color(.015f,.05f,.06f); metallic = .5f; smoothness = .85f; }
                    else if (lower.Contains("aircraftarmor")) { color = usa ? new Color(.78f,.83f,.87f) : new Color(.82f,.81f,.77f); metallic = .48f; smoothness = .65f; }
                    else if (lower.Contains("aircraftsecondary")) { color = new Color(.32f,.39f,.43f); metallic = .62f; smoothness = .56f; }
                    else if (lower.Contains("aircraftglow")) { color = new Color(1f,.33f,.055f); glow = 3f; }
                    else if (lower.Contains("aircraftsensor")) { color = new Color(.07f,.7f,1f); glow = 2f; }
                    else if (lower.Contains("aircraftglass")) { color = new Color(.006f,.025f,.04f); metallic = .7f; smoothness = .9f; }
                    else if (lower.Contains("aircraftmetal")) { color = new Color(.025f,.038f,.045f); metallic = .85f; smoothness = .6f; }
                    else if (lower.Contains("structurearmor")) { color = usa ? new Color(.16f,.20f,.22f) : new Color(.21f,.18f,.17f); metallic = .4f; smoothness = .26f; }
                    else if (lower.Contains("structuresecondary")) { color = new Color(.29f,.32f,.33f); metallic = .5f; smoothness = .3f; }
                    else if (lower.Contains("structureglow")) { color = new Color(.13f,.68f,.85f); glow = 2.2f; }
                    else if (lower.Contains("structureglass")) { color = new Color(.055f,.16f,.21f); metallic = .55f; smoothness = .76f; glow = .25f; }
                    else if (lower.Contains("structuremetal")) { color = new Color(.095f,.11f,.12f); metallic = .72f; smoothness = .3f; }
                    else if (lower.Contains("skin")) { color = new Color(.49f,.34f,.23f); metallic = 0; smoothness = .12f; }
                    else if (lower.Contains("glow") || lower.Contains("emiss") || lower.Contains("light")) { color = usa ? new Color(.13f,.36f,.57f) : new Color(.68f,.15f,.08f); }
                    else if (lower.Contains("glass") || lower.Contains("visor") || lower.Contains("window")) { color = new Color(.075f,.15f,.17f); smoothness = .78f; metallic = .5f; }
                    else if (lower.Contains("secondary")) color = usa ? new Color(.27f,.31f,.24f) : new Color(.36f,.20f,.13f);
                    else if (lower.Contains("dark")) color = new Color(.095f,.10f,.085f);
                    else if (lower.Contains("rubber") || lower.Contains("track")) { color = new Color(.07f,.064f,.05f); metallic = .1f; smoothness = .12f; }
                    else if (lower.Contains("metal") || lower.Contains("steel") || lower.Contains("joint")) { color = new Color(.27f,.27f,.23f); metallic = .6f; }
                    else if (lower.Contains("gold") || lower.Contains("accent")) color = usa ? new Color(.34f,.38f,.3f) : new Color(.56f,.39f,.12f);
                    else color = usa ? new Color(.62f,.56f,.40f) : new Color(.32f,.38f,.22f);
                    return Mat(key, color, metallic, smoothness, glow);
                }).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        static void ConfigureRendering()
        {
            string path = Root + "/Settings/ShowcasePipeline.asset";
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (!pipeline)
            {
                pipeline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"));
                AssetDatabase.CreateAsset(pipeline, path);
            }
            pipeline.name = "ShowcasePipeline";
            pipeline.shadowDistance = 260;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1;
            QualitySettings.renderPipeline = pipeline;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
        }

        static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = true;
            return go;
        }

        static void Line(Transform parent, string name, Vector3 a, Vector3 b, float width, Material material)
        {
            var box = Box(parent, name, (a + b) / 2, new Vector3(width, .035f, Vector3.Distance(a, b)), material);
            box.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
        }

        static void Text(Transform parent, string value, Vector3 position, float scale, Material tint, float maxWidth = 86, float maxHeight = 1)
        {
            var go = new GameObject(value);
            go.transform.SetParent(parent);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = value;
            mesh.font = font;
            mesh.fontSize = 80;
            mesh.characterSize = scale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = tint.GetColor("_BaseColor");
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            Bounds textBounds = go.GetComponent<MeshRenderer>().bounds;
            float fit = Mathf.Min(1, maxWidth / Mathf.Max(.01f, textBounds.size.x), maxHeight / Mathf.Max(.01f, textBounds.size.z));
            go.transform.localScale = Vector3.one * fit;
        }

        static void MakeEnvironment(Transform parent)
        {
            Box(parent, "Foundation", new Vector3(0,-1.4f,-6), new Vector3(218,2,133), inset);
            Box(parent, "Outer service apron", new Vector3(0,-.3f,-6), new Vector3(212,.3f,127), road);
            foreach (float x in new[] {-52f,52f})
            {
                Material accent = x < 0 ? cyan : amber;
                Box(parent, "Armored faction deck", new Vector3(x,-.08f,-6), new Vector3(96,.3f,115), ground);
                for (int i = -4; i <= 4; i++)
                    Box(parent, "Deck expansion joint", new Vector3(x+i*10,.077f,-6),new Vector3(.045f,.015f,113),inset);
                for (int i = -5; i <= 5; i++)
                    Box(parent,"Deck expansion joint",new Vector3(x,.078f,-6+i*10),new Vector3(94,.015f,.045f),inset);
                foreach (float side in new[] {-1f,1f})
                {
                    Box(parent, "Perimeter armor", new Vector3(x + side * 47.7f,.3f,-6),new Vector3(.5f,.6f,115),metal);
                    Box(parent, "Perimeter light", new Vector3(x + side * 47.35f,.14f,-6),new Vector3(.09f,.08f,113),accent);
                    Box(parent, "Perimeter armor", new Vector3(x,.3f,-6 + side * 57.7f),new Vector3(96,.6f,.5f),metal);
                    Box(parent, "Perimeter light", new Vector3(x,.14f,-6 + side * 57.3f),new Vector3(95,.08f,.09f),accent);
                }
                Box(parent,"Assembly corridor", new Vector3(x,.084f,-2),new Vector3(93,.012f,5),road);
                for (int i=-8;i<=8;i++) Box(parent,"Lane dash",new Vector3(x+i*5.3f,.1f,-2),new Vector3(2,.018f,.06f),muted);
                Text(parent, x < 0 ? "PACIFIC / VANGUARD" : "CRIMSON / DYNASTY", new Vector3(x,.15f,46),1.0f,accent,84,3.5f);
                Text(parent, x < 0 ? "01     PRECISION  /  NETWORK  /  AIR POWER" : "02     ARMOR  /  INDUSTRY  /  AREA DENIAL",new Vector3(x,.15f,42.7f),.35f,white,80,.65f);
                Text(parent,"MOBILE DIVISION     //     16 PLATFORMS",new Vector3(x,.15f,-6.2f),.37f,muted);
                Text(parent,"NIGHTFALL    /    FORCE DEVELOPMENT COMMAND",new Vector3(x,.15f,-61),.33f,muted);
            }
            Box(parent,"Central utility trench",new Vector3(0,-.2f,-6),new Vector3(5,.15f,115),inset);
            for(int z=-59;z<52;z+=3)
            {
                Box(parent,"Utility conduit",new Vector3(-.8f,-.08f,z),new Vector3(.1f,.1f,2),cyan);
                Box(parent,"Utility conduit",new Vector3(.8f,-.08f,z),new Vector3(.1f,.1f,2),amber);
            }
            foreach(float z in new[]{-38f,-2f,31f})
                Box(parent,"Service bridge",new Vector3(0,.15f,z),new Vector3(8,.3f,3.6f),metal);
            for (int x=-102;x<105;x+=6)
            {
                Box(parent,"Apron strip",new Vector3(x,-.13f,-66),new Vector3(2,.02f,.10f),muted);
                Box(parent,"Apron strip",new Vector3(x,-.13f,55),new Vector3(2,.02f,.10f),muted);
            }
            // Low skyline gives the test range a cyberpunk industrial context without hiding assets.
            var random = new System.Random(27);
            for(int i=0;i<28;i++)
            {
                float x=-123+i*9.2f, height=8+(float)random.NextDouble()*22;
                float z=75+(i%3)*8;
                Box(parent,"Distant logistics tower",new Vector3(x,height/2-2,z),new Vector3(6.5f,height,6),inset);
                Box(parent,"Tower crown",new Vector3(x,height-1.8f,z),new Vector3(5,.3f,4.5f),metal);
                for(int level=3;level<height-2;level+=3)
                    Box(parent,"Tower facade light",new Vector3(x,level,z-3.03f),new Vector3(4,.065f,.035f),i%3==0?amber:cyan);
            }
        }

        static void MakeDisplayPad(Transform parent, Vector3 pos, ArmyDefinition def, int ordinal, Material accent)
        {
            bool building = def.category == "Structure";
            var group = new GameObject("Bay " + def.id).transform;
            group.SetParent(parent);
            float width=building?15.4f:17.5f, depth=building?17f:11.2f;
            Box(group,"Recessed model bay",new Vector3(pos.x,.13f,pos.z),new Vector3(width,.18f,depth),inset);
            Box(group,"Equipment plinth",new Vector3(pos.x,.21f,pos.z+.6f),new Vector3(building?14.3f:10,.14f,building?13.7f:7.6f),platform);
            foreach(float sign in new[]{-1f,1f})
            {
                float x=pos.x+sign*(width/2-.3f);
                Line(group,"Illuminated bay corner",new Vector3(x,.25f,pos.z-depth/2+.3f),new Vector3(x,.25f,pos.z-depth/2+1.7f),.075f,accent);
                Line(group,"Illuminated bay corner",new Vector3(x,.25f,pos.z+depth/2-.3f),new Vector3(x,.25f,pos.z+depth/2-1.7f),.075f,accent);
            }
            string shortName=def.displayName.ToUpperInvariant();
            Text(group,shortName,new Vector3(pos.x,.255f,pos.z-depth/2+.85f),building?.34f:.37f,white,width-1.8f,.65f);
            Text(group,$"{(building?"B":"U")}{ordinal+1:00}   /   T{def.tier}   /   {def.category.ToUpperInvariant()}",new Vector3(pos.x,.255f,pos.z-depth/2+.25f),.19f,accent,width-2.2f,.26f);
        }

        static Camera MakeLightingAndCamera()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.36f,.47f,.6f);
            RenderSettings.ambientEquatorColor = new Color(.17f,.24f,.32f);
            RenderSettings.ambientGroundColor = new Color(.08f,.12f,.17f);
            RenderSettings.fog = false;
            var sun = new GameObject("Cool moon / key light").AddComponent<Light>();
            sun.type=LightType.Directional; sun.color=new Color(.76f,.86f,1); sun.intensity=2.0f;
            sun.transform.rotation=Quaternion.Euler(48,-32,0); sun.shadows=LightShadows.Soft; sun.shadowStrength=.85f;
            RenderSettings.sun=sun;
            var fill = new GameObject("Warm industrial / rim light").AddComponent<Light>();
            fill.type=LightType.Directional; fill.color=new Color(1,.65f,.43f); fill.intensity=.65f;
            fill.transform.rotation=Quaternion.Euler(35,148,0);
            var camera=new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag="MainCamera";
            camera.orthographic=true; camera.orthographicSize=82;
            camera.transform.rotation=Quaternion.Euler(50,0,0);
            camera.transform.position=new Vector3(-24,0,-5)-camera.transform.forward*205;
            camera.nearClipPlane=.3f; camera.farClipPlane=650;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.013f,.021f,.034f);
            camera.allowHDR=true;
            camera.gameObject.AddComponent<AudioListener>();
            var cameraData=camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing=true;
            cameraData.antialiasing=AntialiasingMode.FastApproximateAntialiasing;
            string profilePath=Root+"/Settings/ShowcaseVolume.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(!profile) { profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,profilePath); }
            foreach(var component in profile.components.ToArray()) { profile.components.Remove(component); UnityEngine.Object.DestroyImmediate(component,true); }
            var bloom=profile.Add<Bloom>(true); bloom.intensity.Override(.24f); bloom.threshold.Override(1.05f); bloom.scatter.Override(.55f);
            var tonemap=profile.Add<Tonemapping>(true); tonemap.mode.Override(TonemappingMode.ACES);
            var adjustments=profile.Add<ColorAdjustments>(true); adjustments.postExposure.Override(.5f); adjustments.contrast.Override(10); adjustments.saturation.Override(5);
            foreach(var component in profile.components) AssetDatabase.AddObjectToAsset(component,profile);
            EditorUtility.SetDirty(profile);
            var volume=new GameObject("Nightfall / color & bloom").AddComponent<Volume>();
            volume.isGlobal=true; volume.sharedProfile=profile;
            return camera;
        }

        static Bounds WorldBounds(GameObject go)
        {
            Renderer[] renderers=go.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0) throw new InvalidOperationException("No mesh renderers: "+go.name);
            var bounds=renderers[0].bounds;
            foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        [MenuItem("Neon Frontier/Validate Army Assets")]
        public static void Validate()
        {
            var definitions=AssetDatabase.FindAssets("t:ArmyDefinition",new[]{Root+"/Data"})
                .Select(guid=>AssetDatabase.LoadAssetAtPath<ArmyDefinition>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            var errors=new List<string>();
            if(definitions.Length!=52) errors.Add("Expected 52 definitions; found "+definitions.Length);
            if(definitions.Select(d=>d.id).Distinct().Count()!=definitions.Length) errors.Add("Duplicate roster IDs");
            foreach(string faction in new[]{"Vanguard","Dynasty"})
            {
                if(definitions.Count(d=>d.faction==faction && d.category=="Structure")!=10) errors.Add(faction+" needs 10 structures");
                if(definitions.Count(d=>d.faction==faction && d.category!="Structure")!=16) errors.Add(faction+" needs 16 units");
            }
            var report=new System.Text.StringBuilder("NEON FRONTIER ASSET VALIDATION\n");
            foreach(var def in definitions.OrderBy(d=>d.id))
            {
                if(!def.prefab) { errors.Add("Missing prefab: "+def.id); continue; }
                var entity=def.prefab.GetComponent<ArmyEntity>();
                if(!entity || entity.definition!=def || !def.prefab.GetComponent<Collider>()) errors.Add("Invalid selectable prefab: "+def.id);
                var renderers=def.prefab.GetComponentsInChildren<Renderer>();
                if(renderers.Length==0) errors.Add("No renderers: "+def.id);
                if(renderers.Any(r=>r.sharedMaterials.Any(m=>!m || !m.shader || m.shader.name.Contains("InternalError")))) errors.Add("Broken material: "+def.id);
                int triangles=def.prefab.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh?m.sharedMesh.triangles.Length/3:0);
                if(triangles==0) errors.Add("Empty mesh: "+def.id);
                report.AppendLine($"{def.id}: {triangles:N0} triangles, {renderers.Length} renderers, prefab + data OK");
            }
            if(EditorSceneManager.GetActiveScene().path==ScenePath)
            {
                int entities=UnityEngine.Object.FindObjectsByType<ArmyEntity>(FindObjectsSortMode.None).Length;
                if(entities!=52) errors.Add("Scene expected 52 selectable entries; found "+entities);
            }
            report.AppendLine(errors.Count==0?"PASS: 52/52 complete.":string.Join("\n",errors));
            Directory.CreateDirectory("Documentation");
            File.WriteAllText("Documentation/AssetValidation.txt",report.ToString());
            if(errors.Count>0) throw new InvalidOperationException(string.Join("\n",errors));
            Debug.Log("NEON FRONTIER VALIDATION PASS: 52 assets with meshes, URP materials, colliders and definitions.");
        }

        [MenuItem("Neon Frontier/Build Windows Showcase")]
        public static void BuildWindows()
        {
            if(!File.Exists(ScenePath)) Build();
            EditorSceneManager.OpenScene(ScenePath);
            Validate();
            Directory.CreateDirectory("Builds/NeonFrontier");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{ScenePath}, locationPathName="Builds/NeonFrontier/NeonFrontier.exe",
                target=BuildTarget.StandaloneWindows64, options=BuildOptions.None
            });
            if(result.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Showcase build failed: "+result.summary.result);
            Debug.Log("NEON FRONTIER WINDOWS BUILD PASS");
        }

        public static void BuildAll() { Build(); BuildWindows(); }
    }
}
