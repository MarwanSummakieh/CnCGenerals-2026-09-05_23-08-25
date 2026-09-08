using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace NeonFrontier.Editor
{
    /// <summary>Authors the playable entry scene without replacing the user's working scene.</summary>
    public static class SkirmishBuilder
    {
        public const string ScenePath = "Assets/Armies/Scenes/NeonFrontier.unity";
        public const string PlayerPath = "Builds/NeonFrontierDemo/NeonFrontier.exe";
        const int ExpectedRosterSize = 52;

        [MenuItem("Neon Frontier/Skirmish/Generate Gameplay Scene")]
        public static void Build()
        {
            EnsureEditorIsReady();
            if (Application.isBatchMode && File.Exists(ScenePath))
            {
                // A source checkout already contains the authored entry scene. Open it
                // in the isolated batch editor and resolve assets after the scene switch.
                var savedScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                ValidateScene(savedScene, LoadDefinitions());
                ConfigureBuildScenes();
                Debug.Log("NEON FRONTIER: saved gameplay scene validated in the batch editor.");
                return;
            }
            var definitions = LoadDefinitions();
            var loadedGameplayScene = SceneManager.GetSceneByPath(ScenePath);
            if (loadedGameplayScene.IsValid() && loadedGameplayScene.isLoaded)
            {
                // A loaded scene may contain user edits. Reuse it only when its saved bootstrap is valid.
                if (loadedGameplayScene.isDirty)
                    throw new InvalidOperationException("The gameplay scene has unsaved edits. Save or close it before regenerating; no scene edits were discarded.");
                ValidateScene(loadedGameplayScene, definitions);
                ConfigureBuildScenes();
                Debug.Log("NEON FRONTIER: gameplay scene already loaded and valid; preserved the open scene.");
                return;
            }

            Directory.CreateDirectory("Assets/Armies/Scenes");
            var previousActiveScene = SceneManager.GetActiveScene();
            // A batch editor begins with an untitled scene that cannot accept an additive
            // scene. Only that isolated process uses Single; an interactive editor retains
            // its open scenes and the restoration path below.
            var authoringScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(authoringScene);
                if (Application.isBatchMode) definitions = LoadDefinitions();
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.orthographic = true;
                camera.orthographicSize = 76;
                camera.transform.rotation = Quaternion.Euler(56, 0, 0);
                camera.transform.position = new Vector3(0, 0, 0) - camera.transform.forward * 210;
                camera.nearClipPlane = .3f;
                camera.farClipPlane = 700;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.57f, .73f, .83f);
                camera.allowHDR = true;
                camera.gameObject.AddComponent<AudioListener>();
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

                var flow = new GameObject("Neon Frontier | Game Flow").AddComponent<GameFlowController>();
                flow.sceneCamera = camera;
                flow.commandShader = Shader.Find("Universal Render Pipeline/Unlit");
                flow.roster = definitions;
                ValidateScene(authoringScene, definitions);
                EditorSceneManager.MarkSceneDirty(authoringScene);
                if (!EditorSceneManager.SaveScene(authoringScene, ScenePath))
                    throw new IOException("Unity could not save the gameplay scene: " + ScenePath);
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (authoringScene.IsValid() && authoringScene.isLoaded && SceneManager.sceneCount > 1)
                    EditorSceneManager.CloseScene(authoringScene, true);
            }

            ConfigureBuildScenes();
            Debug.Log("NEON FRONTIER: saved playable main-menu entry scene with all 52 army definitions. The user's working scenes were preserved.");
        }

        [MenuItem("Neon Frontier/Skirmish/Validate Gameplay Scene")]
        public static void Validate()
        {
            EnsureEditorIsReady();
            var definitions = LoadDefinitions();
            var previousActiveScene = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                ValidateScene(scene, definitions);
                var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
                if (enabled.Length == 0 || enabled[0].path != ScenePath)
                    throw new InvalidOperationException("The gameplay scene must be the first enabled build scene.");
                if (enabled.Length < 2 || enabled[1].path != ArmyShowcaseBuilder.ScenePath)
                    throw new InvalidOperationException("The army showcase must remain the second enabled build scene.");
                Debug.Log("NEON FRONTIER: gameplay scene validation passed (camera, controller, 52 definitions, prefab links and build order).");
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedForValidation && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("Neon Frontier/Skirmish/Build Windows Player")]
        public static void BuildAll()
        {
            ArmyShowcaseBuilder.RefreshMilitaryAssets();
            Build();
            Validate();
            PlayerSettings.companyName = "Neon Frontier";
            PlayerSettings.productName = "Neon Frontier";
            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows build failed: {report.summary.result}; {report.summary.totalErrors} error(s). Check the Unity editor log.");
            Debug.Log($"NEON FRONTIER: Windows player built at {Path.GetFullPath(PlayerPath)} ({report.summary.totalSize:N0} bytes, {report.summary.totalWarnings} warning(s)).");
        }

        static ArmyDefinition[] LoadDefinitions()
        {
            var definitions = AssetDatabase.FindAssets("t:ArmyDefinition", new[] { "Assets/Armies/Data" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ArmyDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null)
                .OrderBy(definition => definition.id, StringComparer.Ordinal)
                .ToArray();
            if (definitions.Length != ExpectedRosterSize)
                throw new InvalidOperationException($"Expected {ExpectedRosterSize} existing army definitions, found {definitions.Length}. Generate the army assets first.");
            if (definitions.Any(definition => string.IsNullOrWhiteSpace(definition.id)) ||
                definitions.Select(definition => definition.id).Distinct(StringComparer.Ordinal).Count() != ExpectedRosterSize)
                throw new InvalidOperationException("Every army definition must have a unique, nonempty ID.");
            foreach (var faction in new[] { "Vanguard", "Dynasty" })
                if (definitions.Count(definition => definition.faction == faction) != 26)
                    throw new InvalidOperationException("The roster must contain 26 definitions for " + faction + ".");
            foreach (var definition in definitions)
            {
                if (!definition.prefab)
                    throw new InvalidOperationException("Missing prefab for " + definition.id + ".");
                var entity = definition.prefab.GetComponent<ArmyEntity>();
                if (!entity || entity.definition != definition)
                    throw new InvalidOperationException("Prefab definition link is invalid for " + definition.id + ".");
            }
            return definitions;
        }

        static void ValidateScene(Scene scene, ArmyDefinition[] definitions)
        {
            var controllers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameFlowController>(true)).ToArray();
            if (controllers.Length != 1)
                throw new InvalidOperationException("Gameplay scene must contain exactly one GameFlowController.");
            var flow = controllers[0];
            if (!flow.sceneCamera || flow.sceneCamera.gameObject.scene != scene || !flow.sceneCamera.GetComponent<AudioListener>())
                throw new InvalidOperationException("Gameplay scene requires its own assigned camera and audio listener.");
            if (flow.roster == null || flow.roster.Length != definitions.Length ||
                flow.roster.Any(definition => definition == null) ||
                flow.roster.Distinct().Count() != definitions.Length ||
                definitions.Any(definition => !flow.roster.Contains(definition)))
                throw new InvalidOperationException("GameFlowController must reference all 52 existing army definitions exactly once.");
        }

        static void ConfigureBuildScenes()
        {
            if (!File.Exists(ArmyShowcaseBuilder.ScenePath))
                throw new FileNotFoundException("The existing army showcase scene is missing.", ArmyShowcaseBuilder.ScenePath);
            var otherScenes = EditorBuildSettings.scenes
                .Where(scene => scene.path != ScenePath && scene.path != ArmyShowcaseBuilder.ScenePath)
                .ToList();
            otherScenes.Insert(0, new EditorBuildSettingsScene(ArmyShowcaseBuilder.ScenePath, true));
            otherScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = otherScenes.ToArray();
        }

        static void EnsureEditorIsReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before authoring or building the gameplay scene.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Wait for Unity compilation, import or the active build to finish.");
        }
    }

    /// <summary>Allows an external build request to use an already-open editor safely.</summary>
    [InitializeOnLoad]
    internal static class SkirmishBuildBridge
    {
        const string RequestPath = "Temp/SkirmishBuild.request";
        const string StatusPath = "Temp/SkirmishBuild.status.json";
        const string RefreshSessionKey = "NeonFrontier.SkirmishBuildBridge.RefreshedRequest";
        static bool busy;
        static double nextPoll;
        static string lastStatus;

        [Serializable]
        sealed class RequestStatus
        {
            public string requestId;
            public string command;
            public string state;
            public string message;
            public string utc;
            public string scene;
            public string player;
        }

        static SkirmishBuildBridge()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (Application.isBatchMode || busy || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .5;
            if (!File.Exists(RequestPath)) return;
            string command = "", requestId = "";
            try
            {
                var request = File.ReadAllLines(RequestPath);
                command = request.Length > 0 ? request[0].Trim().ToLowerInvariant() : "";
                requestId = request.Length > 1 ? request[1].Trim() : File.GetLastWriteTimeUtc(RequestPath).Ticks.ToString();
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SetStatus(requestId, command, "waiting", "Waiting for the user to exit Play Mode. The editor has not been stopped.");
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
                {
                    SetStatus(requestId, command, "waiting", "Waiting for Unity compilation, import or the active build to finish.");
                    return;
                }
                if (command != "scene" && command != "build")
                    throw new InvalidOperationException("Unknown request. Use 'scene' to author the scene or 'build' to author and build the Windows player.");

                // Refresh once per request, then let the editor finish any resulting domain reload.
                if (SessionState.GetString(RefreshSessionKey, "") != requestId)
                {
                    SessionState.SetString(RefreshSessionKey, requestId);
                    SetStatus(requestId, command, "waiting", "Refreshing project assets before authoring.");
                    AssetDatabase.Refresh();
                    return;
                }

                busy = true;
                File.Delete(RequestPath);
                SetStatus(requestId, command, "running", command == "build" ? "Authoring the gameplay scene and building the Windows player." : "Authoring the gameplay scene.");
                if (command == "build") SkirmishBuilder.BuildAll();
                else
                {
                    SkirmishBuilder.Build();
                    SkirmishBuilder.Validate();
                }
                SetStatus(requestId, command, "completed", command == "build" ? "Gameplay scene validated and Windows player built successfully." : "Gameplay scene generated and validated successfully.");
            }
            catch (Exception exception)
            {
                if (File.Exists(RequestPath)) File.Delete(RequestPath);
                SetStatus(requestId, command, "failed", exception.ToString());
                Debug.LogException(exception);
            }
            finally
            {
                busy = false;
            }
        }

        static void SetStatus(string requestId, string command, string state, string message)
        {
            string key = requestId + "|" + state + "|" + message;
            if (key == lastStatus) return;
            lastStatus = key;
            Directory.CreateDirectory("Temp");
            File.WriteAllText(StatusPath, JsonUtility.ToJson(new RequestStatus
            {
                requestId = requestId,
                command = command,
                state = state,
                message = message,
                utc = DateTime.UtcNow.ToString("O"),
                scene = SkirmishBuilder.ScenePath,
                player = command == "build" ? Path.GetFullPath(SkirmishBuilder.PlayerPath) : ""
            }, true));
        }
    }
}
