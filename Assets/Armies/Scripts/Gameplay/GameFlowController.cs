using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace NeonFrontier
{
    /// <summary>Menu, deployment, RTS input, tactical HUD and match lifecycle.</summary>
    public sealed partial class GameFlowController : MonoBehaviour
    {
        public enum ScreenState { MainMenu, Setup, Loading, Playing, Paused, Victory, Defeat, Settings, Manual }
        public ArmyDefinition[] roster;
        public Camera sceneCamera;
        // Keep the procedural UI/command shader in standalone builds as well as the editor.
        public Shader commandShader;
        public ScreenState State { get; private set; } = ScreenState.MainMenu;
        public SkirmishSimulation Simulation { get; private set; }
        public readonly List<RtsUnit> Selection = new List<RtsUnit>();
        public string ChosenFaction { get; private set; } = "Vanguard";
        public int Difficulty { get; private set; } = 1;
        public Vector3 CameraFocus => focus;
        ScreenState returnScreen;
        DemoBattlefield battlefield;
        Vector3 focus = new Vector3(0, 0, 5), wantedFocus;
        float zoom = 112, wantedZoom = 112, yaw = -24, wantedYaw = -24;
        float scale, width, height, sensitivity = 1, volume = .65f;
        bool edgePan, dragging, attackMove, buildPanel;
        Vector2 dragStart, dragEnd;
        ArmyDefinition placing;
        GameObject placementPreview;
        Material previewMaterial;
        Vector3 placementPosition;
        string placementReason, localNotice;
        float noticeUntil;
        Texture2D mapTexture, steelTexture;
        TacticalPortraitRenderer portraits;
        GUIStyle title, heading, body, small, tiny, button, number, cardLabel, centerLabel;
        string hoverTitle, hoverBody, hoverFooter;
        Color hoverColor;
        readonly Color ink = new Color(.055f, .062f, .052f, .985f);
        readonly Color panel = new Color(.115f, .13f, .105f, .985f);
        readonly Color white = new Color(.95f, .92f, .82f);
        readonly Color muted = new Color(.80f, .82f, .74f);
        readonly Color lime = new Color(.82f, .69f, .36f);
        readonly Color cyan = new Color(.36f, .68f, .84f);
        readonly Color red = new Color(.88f, .32f, .23f);
        readonly Dictionary<int, List<RtsUnit>> groups = new Dictionary<int, List<RtsUnit>>();
        List<ArmyDefinition> production = new List<ArmyDefinition>();
        float refreshOptions;
        AudioSource audioSource;
        AudioClip clickSound, commandSound;
        public Color Accent => ChosenFaction == "Vanguard" ? cyan : red;
        [NonSerialized] public RenderTexture VerificationFrameTarget;
        public int VerificationRepaintSerial { get; private set; }
        Color EnemyAccent => ChosenFaction == "Vanguard" ? red : cyan;
        List<RtsUnit> FriendlySelection => Selection.FindAll(unit => unit && unit.IsAlive && unit.Team == 0);

        void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            if (!sceneCamera) sceneCamera = Camera.main;
            if (!sceneCamera)
            {
                sceneCamera = new GameObject("Tactical camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
                sceneCamera.tag = "MainCamera";
            }
            sceneCamera.orthographic = true;
            sceneCamera.nearClipPlane = .3f;
            sceneCamera.farClipPlane = 1200;
            sceneCamera.backgroundColor = new Color(.61f, .77f, .83f);
            var cameraData = sceneCamera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.renderShadows = true;
            battlefield = new GameObject("VERDANT REACH | river valley").AddComponent<DemoBattlefield>();
            battlefield.Build();
            Simulation = new GameObject("Skirmish simulation").AddComponent<SkirmishSimulation>();
            Simulation.MatchEnded += EndMatch;
            wantedFocus = focus;
            sensitivity = PlayerPrefs.GetFloat("NF.CameraSpeed", 1);
            volume = PlayerPrefs.GetFloat("NF.Volume", .65f);
            edgePan = PlayerPrefs.GetInt("NF.EdgePan", 0) == 1;
            AudioListener.volume = volume;
            audioSource = gameObject.AddComponent<AudioSource>();
            clickSound = Tone(620, .045f);
            commandSound = Tone(410, .075f);
            BuildMinimap();
            BuildInterfaceTexture();
            portraits = gameObject.AddComponent<TacticalPortraitRenderer>();
            portraits.Initialize(roster, sceneCamera);
            ApplyCamera(true);
            gameObject.AddComponent<SkirmishVerification>();
        }

        AudioClip Tone(float hz, float seconds)
        {
            int length = Mathf.RoundToInt(22050 * seconds);
            var samples = new float[length];
            for (int i = 0; i < length; i++) samples[i] = Mathf.Sin(i * hz * 2 * Mathf.PI / 22050) * .10f * (1f - i / (float)length);
            var clip = AudioClip.Create("Tactical feedback", length, 1, 22050, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void Layout() { scale = Mathf.Max(.4f, Mathf.Min(Screen.width / 1600f, Screen.height / 900f)); width = Screen.width / scale; height = Screen.height / scale; }
        Vector2 GuiMouse(Vector2 screen) => new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
        bool OverHud(Vector2 screen) { Vector2 p = GuiMouse(screen); return p.y < 58 || p.y > height - 266 || (p.x < 305 && p.y > height - 304) || (p.x > width - 350 && p.y < 118); }
        Rect MinimapRect => new Rect(20, height - 219, 258, 180);
        public void FocusCamera(Vector3 position, float size = 48) { wantedFocus = DemoBattlefield.ClampToMap(position); wantedZoom = Mathf.Clamp(size, 24, 140); }

        void Update()
        {
            Layout();
            Selection.RemoveAll(u => !u || !u.IsAlive || !Simulation.IsVisible(u));
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (placing) CancelPlacement();
                else if (attackMove) attackMove = false;
                else if (State == ScreenState.Playing) PauseMatch();
                else if (State == ScreenState.Paused) ResumeMatch();
                else if (State == ScreenState.Manual || State == ScreenState.Settings) State = returnScreen;
                else if (State == ScreenState.Setup) State = ScreenState.MainMenu;
            }
            if (State == ScreenState.Playing)
            {
                HandleCameraInput();
                HandleOrders();
                if (Time.unscaledTime > refreshOptions) { RefreshProduction(); refreshOptions = Time.unscaledTime + .5f; }
            }
            else if (State == ScreenState.MainMenu || State == ScreenState.Setup)
            {
                wantedFocus = new Vector3(15, 0, 7);
                wantedZoom = State == ScreenState.MainMenu ? 116 : 130;
                wantedYaw = -24 + Mathf.Sin(Time.unscaledTime * .025f) * 5;
            }
            ApplyCamera(false);
        }

        void HandleCameraInput()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            Vector3 right = Vector3.ProjectOnPlane(sceneCamera.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(sceneCamera.transform.forward, Vector3.up).normalized;
            Vector3 move = Vector3.zero;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) move += forward;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) move -= forward;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) move += right;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) move -= right;
                wantedYaw += ((k.eKey.isPressed ? 1 : 0) - (k.qKey.isPressed ? 1 : 0)) * 60 * Time.unscaledDeltaTime;
                if (k.spaceKey.wasPressedThisFrame && Simulation.PlayerHQ) FocusCamera(Simulation.PlayerHQ.Position);
                if (k.fKey.wasPressedThisFrame && Selection.Count > 0) FocusCamera(Selection[0].Position, 36);
            }
            if (m != null)
            {
                Vector2 pos = m.position.ReadValue();
                if (edgePan && Application.isFocused)
                {
                    if (pos.x < 8) move -= right;
                    if (pos.x > Screen.width - 8) move += right;
                    if (pos.y > Screen.height - 8) move += forward;
                    if (pos.y < 8) move -= forward;
                }
                if (!OverHud(pos)) wantedZoom = Mathf.Clamp(wantedZoom * Mathf.Exp(-m.scroll.ReadValue().y * .001f), 24, 135);
                if (m.middleButton.isPressed && !OverHud(pos))
                    wantedFocus += GroundPoint(pos - m.delta.ReadValue()) - GroundPoint(pos);
            }
            wantedFocus += move.normalized * wantedZoom * sensitivity * Time.unscaledDeltaTime;
            wantedFocus = DemoBattlefield.ClampToMap(wantedFocus);
        }

        void ApplyCamera(bool immediate)
        {
            float blend = immediate ? 1 : 1 - Mathf.Exp(-9 * Time.unscaledDeltaTime);
            focus = Vector3.Lerp(focus, wantedFocus, blend);
            zoom = Mathf.Lerp(zoom, wantedZoom, blend);
            yaw = Mathf.LerpAngle(yaw, wantedYaw, blend);
            sceneCamera.transform.rotation = Quaternion.Euler(48, yaw, 0);
            // Orthographic framing is unchanged by distance; stay inside the pipeline's shadow range.
            sceneCamera.transform.position = focus - sceneCamera.transform.forward * 185;
            sceneCamera.orthographicSize = zoom;
        }

        public Vector3 GroundPoint(Vector2 screen)
        {
            var ray = sceneCamera.ScreenPointToRay(screen);
            return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance) ? DemoBattlefield.ClampToMap(ray.GetPoint(distance)) : focus;
        }
        RtsUnit Pick(Vector2 screen)
        {
            var hits = Physics.RaycastAll(sceneCamera.ScreenPointToRay(screen), 1000);
            RtsUnit best = null; float nearest = float.MaxValue;
            foreach (var hit in hits)
            {
                var u = hit.collider.GetComponentInParent<RtsUnit>();
                if (u && u.IsAlive && Simulation.IsVisible(u) && hit.distance < nearest) { best = u; nearest = hit.distance; }
            }
            return best;
        }

        void HandleOrders()
        {
            var m = Mouse.current; var k = Keyboard.current;
            bool shift = k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
            if (k != null)
            {
                if (k.tabKey.wasPressedThisFrame)
                {
                    ClearSelection();
                    foreach (var u in Simulation.Units) if (u && u.IsAlive && u.Team == 0 && !u.IsStructure && !u.IsWorker) AddSelection(u);
                    RefreshProduction();
                }
                if (k.xKey.wasPressedThisFrame) { attackMove = true; Notify("ATTACK MOVE  /  Choose a destination"); }
                if (k.hKey.wasPressedThisFrame) Simulation.StopUnits(FriendlySelection);
                if (k.bKey.wasPressedThisFrame) { buildPanel = !buildPanel; RefreshProduction(); }
                Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
                for (int i = 0; i < keys.Length; i++) if (k[keys[i]].wasPressedThisFrame)
                {
                    if (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed)
                    { groups[i] = new List<RtsUnit>(Selection); Notify("GROUP " + (i + 1) + "  /  " + Selection.Count + " assigned"); }
                    else if (groups.TryGetValue(i, out var group))
                    { ClearSelection(); foreach (var u in group) if (u && u.IsAlive && u.Team == 0) AddSelection(u); RefreshProduction(); }
                }
            }
            if (m == null) return;
            Vector2 p = m.position.ReadValue();
            if (placing)
            {
                placementPosition = GroundPoint(p);
                RtsUnit worker = SelectedWorker();
                bool valid = Simulation.CanBuild(worker, placing, placementPosition, out placementReason);
                if (placementPreview)
                {
                    placementPreview.transform.position = placementPosition + Vector3.up * (DemoBattlefield.GroundHeight(placementPosition) + .15f);
                    previewMaterial.color = valid ? new Color(.35f, 1, .55f, .42f) : new Color(1, .2f, .1f, .42f);
                }
                if (m.rightButton.wasPressedThisFrame) CancelPlacement();
                else if (m.leftButton.wasPressedThisFrame && !OverHud(p))
                {
                    if (Simulation.PlaceBuilding(worker, placing, placementPosition)) { CancelPlacement(); audioSource.PlayOneShot(commandSound); }
                    else Notify(placementReason);
                }
                return;
            }
            if (m.leftButton.wasPressedThisFrame && !OverHud(p))
            {
                if (attackMove) { Simulation.MoveUnits(FriendlySelection, GroundPoint(p), true); attackMove = false; audioSource.PlayOneShot(commandSound); }
                else { dragging = true; dragStart = dragEnd = p; }
            }
            if (dragging) dragEnd = p;
            if (dragging && m.leftButton.wasReleasedThisFrame)
            {
                dragging = false;
                if (!shift) ClearSelection();
                if (Vector2.Distance(dragStart, dragEnd) < 7)
                {
                    var picked = Pick(p);
                    if (picked && (Selection.Count == 0 || picked.Team == 0))
                    {
                        if (shift && Selection.Contains(picked)) { picked.Selected = false; Selection.Remove(picked); }
                        else AddSelection(picked);
                    }
                }
                else
                {
                    Rect box = ScreenBox(dragStart, dragEnd);
                    foreach (var u in Simulation.Units)
                        if (u && u.Team == 0 && u.IsAlive && !u.IsStructure)
                        {
                            Vector3 at = sceneCamera.WorldToScreenPoint(u.Position);
                            if (at.z > 0 && box.Contains(at)) AddSelection(u);
                        }
                }
                buildPanel = SelectedWorker() != null;
                RefreshProduction();
            }
            if (m.rightButton.wasPressedThisFrame && !OverHud(p))
            {
                attackMove = false;
                var target = Pick(p);
                if (target && target.Team == 1) Simulation.AttackUnits(FriendlySelection, target);
                else
                {
                    var point = GroundPoint(p);
                    Simulation.MoveUnits(FriendlySelection, point);
                    foreach (var u in Selection) if (u.Team == 0 && u.IsStructure) u.RallyPoint = point;
                }
                audioSource.PlayOneShot(commandSound);
            }
        }

        static Rect ScreenBox(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        public void ClearSelection() { foreach (var u in Selection) if (u) u.Selected = false; Selection.Clear(); }
        void AddSelection(RtsUnit u) { if (Selection.Count > 0 && Selection[0].Team != u.Team) ClearSelection(); if (!Selection.Contains(u)) { Selection.Add(u); u.Selected = true; } }
        public void SelectUnit(RtsUnit u) { ClearSelection(); if (u) AddSelection(u); buildPanel = u && u.IsWorker; RefreshProduction(); }
        RtsUnit SelectedWorker() { foreach (var u in Selection) if (u && u.Team == 0 && u.IsWorker) return u; return null; }
        void RefreshProduction()
        {
            production.Clear();
            if (Selection.Count == 0 || Selection[0].Team != 0) return;
            production = buildPanel && SelectedWorker() ? Simulation.GetBuildOptions() : Simulation.GetAvailableProduction(Selection[0]);
        }
        void Notify(string message) { localNotice = message; noticeUntil = Time.unscaledTime + 4; }
        public void OpenSetup() { State = ScreenState.Setup; }
        public void Configure(string faction, int difficulty) { ChosenFaction = faction == "Dynasty" ? "Dynasty" : "Vanguard"; Difficulty = Mathf.Clamp(difficulty, 0, 2); }
        public void Deploy() { if (State != ScreenState.Loading) StartCoroutine(DeployRoutine()); }
        IEnumerator DeployRoutine()
        {
            State = ScreenState.Loading;
            Time.timeScale = 1;
            CancelPlacement(); ClearSelection(); groups.Clear(); attackMove = false; dragging = false;
            yield return null;
            Simulation.Initialize(roster, ChosenFaction, Difficulty);
            Simulation.Running = false;
            yield return new WaitForSecondsRealtime(.7f);
            State = ScreenState.Playing;
            Simulation.Running = true;
            wantedYaw = -22;
            FocusCamera(Simulation.PlayerHQ ? Simulation.PlayerHQ.Position + new Vector3(12, 0, -6) : new Vector3(-106, 0, 0), 51);
            Notify("COMMAND ESTABLISHED  /  Build your army. Destroy the enemy headquarters.");
        }
        public void PauseMatch() { if (State != ScreenState.Playing) return; State = ScreenState.Paused; Simulation.Running = false; Time.timeScale = 0; dragging = false; }
        public void ResumeMatch() { if (State != ScreenState.Paused) return; State = ScreenState.Playing; Simulation.Running = true; Time.timeScale = 1; }
        public void ReturnToMenu()
        {
            CancelPlacement(); ClearSelection(); groups.Clear(); Simulation.Clear(); Simulation.Running = false;
            Time.timeScale = 1; State = ScreenState.MainMenu; attackMove = false; buildPanel = false; wantedYaw = -24;
        }
        void EndMatch(bool victory)
        {
            State = victory ? ScreenState.Victory : ScreenState.Defeat;
            Simulation.Running = false; Time.timeScale = 0; CancelPlacement(); dragging = false;
        }
        public void OpenSettings() { returnScreen = State; State = ScreenState.Settings; }
        public void OpenManual() { returnScreen = State; State = ScreenState.Manual; }
        public void CloseSubpage() { State = returnScreen; }
        void BeginPlacement(ArmyDefinition def)
        {
            CancelPlacement(); placing = def; attackMove = false;
            placementPreview = def.prefab ? Instantiate(def.prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            placementPreview.name = "Construction blueprint / " + def.displayName;
            placementPreview.transform.position = Vector3.zero;
            placementPreview.transform.rotation = Quaternion.identity;
            foreach (var collider in placementPreview.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var behaviour in placementPreview.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            float footprint = SkirmishSimulation.Footprint(def);
            var entity = placementPreview.GetComponent<ArmyEntity>();
            var bounds = entity ? entity.DisplayBounds : new Bounds(Vector3.zero, Vector3.one);
            placementPreview.transform.localScale *= footprint / Mathf.Max(bounds.size.x, bounds.size.z, .1f);
            previewMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            previewMaterial.SetFloat("_Surface", 1);
            previewMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            previewMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            previewMaterial.SetFloat("_ZWrite", 0);
            previewMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            previewMaterial.renderQueue = 3000;
            previewMaterial.color = new Color(.35f, 1, .55f, .42f);
            foreach (var renderer in placementPreview.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = previewMaterial;
                renderer.sharedMaterials = materials;
            }
        }
        void CancelPlacement() { placing = null; if (placementPreview) Destroy(placementPreview); if (previewMaterial) Destroy(previewMaterial); placementPreview = null; }

        void Styles()
        {
            if (title != null) return;
            title = Style(72, white, FontStyle.Bold); title.normal.textColor = white;
            heading = Style(29, white, FontStyle.Bold);
            body = Style(17, white); body.wordWrap = true;
            small = Style(13, muted); small.wordWrap = true;
            tiny = Style(11, muted); tiny.wordWrap = true;
            number = Style(27, lime, FontStyle.Bold);
            button = Style(15, white, FontStyle.Bold); button.alignment = TextAnchor.MiddleLeft; button.padding = new RectOffset(15, 10, 0, 0);
            cardLabel = Style(11, white, FontStyle.Bold); cardLabel.alignment = TextAnchor.MiddleCenter;
            cardLabel.padding = new RectOffset(0, 0, 0, 0); cardLabel.wordWrap = true;
            centerLabel = Style(13, white, FontStyle.Bold); centerLabel.alignment = TextAnchor.MiddleCenter;
        }
        GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal) => new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight, normal = { textColor = color }, clipping = TextClipping.Clip };
        void Fill(Rect r, Color c) { var previous = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = previous; }
        void Label(Rect r, string text, GUIStyle style = null) { GUI.Label(r, text, style ?? body); }
        void Rule(float x, float y, float w, Color? c = null) => Fill(new Rect(x, y, w, 1), c ?? new Color(.27f, .39f, .36f, .65f));
        bool Button(Rect r, string text, bool primary = false, bool enabled = true)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            MetalPanel(r, primary ? (hover ? new Color(.82f, .71f, .43f) : new Color(.64f, .55f, .31f)) : hover && enabled ? new Color(.24f, .27f, .20f) : panel, false);
            var old = GUI.enabled; GUI.enabled = enabled;
            button.normal.textColor = primary ? ink : enabled ? white : muted;
            button.hover.textColor = button.active.textColor = button.focused.textColor = button.normal.textColor;
            bool pressed = GUI.Button(r, text, button);
            GUI.enabled = old;
            if (pressed && audioSource) audioSource.PlayOneShot(clickSound);
            return pressed;
        }

        void BuildInterfaceTexture()
        {
            steelTexture = new Texture2D(128, 128, TextureFormat.RGB24, false) { name = "Brushed olive command console", wrapMode = TextureWrapMode.Repeat };
            var noise = new System.Random(3417);
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                float value = .76f + (float)noise.NextDouble() * .16f + (y % 4 == 0 ? .035f : 0);
                pixels[y * 128 + x] = new Color(value, value, value);
            }
            steelTexture.SetPixels(pixels); steelTexture.Apply();
        }

        void MetalPanel(Rect r, Color color, bool screws = true)
        {
            Fill(r, color);
            if (steelTexture)
            {
                Color previous = GUI.color; GUI.color = new Color(color.r, color.g, color.b, 1);
                GUI.DrawTextureWithTexCoords(r, steelTexture, new Rect(0, 0, r.width / 128, r.height / 128));
                GUI.color = previous;
            }
            Border(r, new Color(.015f, .019f, .012f), 2);
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, 1), new Color(.47f, .49f, .39f, .75f));
            Fill(new Rect(r.x + 2, r.yMax - 3, r.width - 4, 1), Color.black);
            if (!screws) return;
            Screw(r.x + 8, r.y + 8); Screw(r.xMax - 12, r.y + 8);
            Screw(r.x + 8, r.yMax - 12); Screw(r.xMax - 12, r.yMax - 12);
        }

        void Screw(float x, float y)
        {
            Fill(new Rect(x, y, 5, 5), new Color(.33f, .35f, .28f));
            Fill(new Rect(x, y + 2, 5, 1), Color.black);
        }

        void Tooltip(Rect rect, string name, string description, string footer, Color? color = null)
        {
            if (!rect.Contains(Event.current.mousePosition)) return;
            hoverTitle = name; hoverBody = description; hoverFooter = footer; hoverColor = color ?? lime;
        }

        void DrawTooltip()
        {
            if (string.IsNullOrEmpty(hoverTitle)) return;
            float x = Mathf.Clamp(Event.current.mousePosition.x - 195, 12, width - 402);
            var r = new Rect(x, height - 460, 390, 181);
            MetalPanel(r, ink);
            Fill(new Rect(r.x + 2, r.y + 2, r.width - 4, 3), hoverColor);
            Label(new Rect(x + 17, r.y + 15, 356, 28), hoverTitle, body);
            Label(new Rect(x + 17, r.y + 49, 356, 84), hoverBody, small);
            Rule(x + 17, r.y + 137, 356);
            var previous = tiny.normal.textColor; tiny.normal.textColor = hoverColor;
            Label(new Rect(x + 17, r.y + 149, 356, 23), hoverFooter, tiny);
            tiny.normal.textColor = previous;
        }

        void Portrait(Rect r, ArmyDefinition definition, bool disabled = false)
        {
            Fill(r, new Color(.10f, .12f, .09f));
            Texture texture = portraits ? portraits.Get(definition) : null;
            if (texture)
            {
                Color previous = GUI.color; GUI.color = disabled ? new Color(.43f, .43f, .40f) : Color.white;
                GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit);
                GUI.color = previous;
            }
            else Label(new Rect(r.x, r.y + r.height / 2 - 12, r.width, 25), definition ? definition.category.ToUpperInvariant() : "COMMAND", centerLabel);
            Fill(new Rect(r.x, r.yMax - 2, r.width, 2), definition && definition.faction == "Dynasty" ? red : cyan);
        }

        internal void DrawVerificationInterface() => OnGUI();

        void OnGUI()
        {
            if (!Simulation || !sceneCamera) return;
            bool capture = VerificationFrameTarget && Event.current.type == EventType.Repaint;
            RenderTexture previousTarget = RenderTexture.active;
            var previous = GUI.matrix;
            try
            {
                if (capture) RenderTexture.active = VerificationFrameTarget;
                Layout(); Styles();
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                if (State == ScreenState.Playing) DrawHud();
                else if (State == ScreenState.MainMenu) DrawMainMenu();
                else if (State == ScreenState.Setup) DrawSetup();
                else if (State == ScreenState.Loading) DrawLoading();
                else if (State == ScreenState.Settings) DrawSettings();
                else if (State == ScreenState.Manual) DrawManual();
                else DrawMatchOverlay();
                if (capture) VerificationRepaintSerial++;
            }
            finally
            {
                GUI.matrix = previous;
                if (capture)
                {
                    RenderTexture.active = previousTarget;
                    VerificationFrameTarget = null;
                }
            }
        }

        void DrawMainMenu()
        {
            Fill(new Rect(0, 0, width, height), new Color(.025f, .028f, .018f, .23f));
            MetalPanel(new Rect(33, 30, 524, height - 60), ink);
            Label(new Rect(72, 65, 425, 23), "JOINT OPERATIONS COMMAND  /  STRATEGIC WARFARE", tiny);
            Rule(75, 110, 440, lime);
            Label(new Rect(66, 127, 480, 94), "NEON", title);
            Label(new Rect(66, 202, 480, 95), "FRONTIER", title);
            Label(new Rect(76, 302, 420, 31), "C O M M A N D   T H E   B A T T L E F I E L D", small);
            Label(new Rect(76, 354, 413, 50), "Build your base. Assemble your army.\nTake control of the river corridor.", body);
            float y = Mathf.Max(439, height * .52f);
            if (Button(new Rect(75, y, 438, 61), "SKIRMISH                                      >", true)) OpenSetup();
            if (Button(new Rect(75, y + 76, 438, 49), "FIELD MANUAL")) OpenManual();
            if (Button(new Rect(75, y + 137, 438, 49), "OPTIONS")) OpenSettings();
            if (Button(new Rect(75, y + 198, 438, 49), "EXIT TO DESKTOP")) Quit();
            Rule(75, height - 109, 438);
            Label(new Rect(75, height - 88, 439, 42), "2 FACTIONS  /  52 UNITS & STRUCTURES\nSINGLE PLAYER SKIRMISH", tiny);
            float right = Mathf.Max(596, width - 731);
            MetalPanel(new Rect(right, height - 277, 689, 235), panel);
            Label(new Rect(right + 22, height - 259, 622, 30), "COMBINED ARMS. DECISIVE FORCE.", body);
            string[] featured = { "Vanguard_Tank", "Dynasty_Heavy", "Vanguard_Helicopter" };
            for (int i = 0; i < featured.Length; i++)
            {
                ArmyDefinition definition = null;
                if (roster != null) foreach (var item in roster) if (item && item.id == featured[i]) { definition = item; break; }
                var r = new Rect(right + 21 + i * 218, height - 216, 210, 132);
                Portrait(r, definition);
                Border(r, new Color(.35f, .37f, .28f), 1);
                Label(new Rect(r.x, height - 77, r.width, 22), definition ? definition.displayName : "", cardLabel);
            }
            MetalPanel(new Rect(width - 359, 38, 315, 84), ink);
            Label(new Rect(width - 338, 51, 272, 24), "THEATER OF OPERATIONS", tiny);
            Label(new Rect(width - 338, 80, 272, 26), "VERDANT REACH", body);
        }

        void DrawSetup()
        {
            Fill(new Rect(0, 0, width, height), new Color(.02f, .04f, .05f, .72f));
            float x = (width - 1240) / 2, y = (height - 700) / 2;
            MetalPanel(new Rect(x, y, 1240, 700), ink);
            Label(new Rect(x + 38, y + 28, 700, 46), "SKIRMISH / OPERATION SETUP", heading);
            Label(new Rect(x + 38, y + 91, 630, 25), "01   CHOOSE YOUR COMMAND", small);
            FactionCard(new Rect(x + 38, y + 132, 330, 205), "Vanguard", "PACIFIC VANGUARD", "PRECISION & AIR POWER", "Durable rail armor. Fast recon.\nAdvanced air support.", cyan);
            FactionCard(new Rect(x + 384, y + 132, 330, 205), "Dynasty", "CRIMSON DYNASTY", "INDUSTRIAL FIREPOWER", "Affordable infantry. Heavy tanks.\nRelentless artillery.", red);
            Label(new Rect(x + 38, y + 372, 650, 24), "02   ENEMY COMMAND", small);
            string[] levels = { "RELAXED", "STANDARD", "VETERAN" };
            for (int i = 0; i < 3; i++) if (Button(new Rect(x + 38 + i * 230, y + 410, 215, 48), levels[i], Difficulty == i)) Difficulty = i;
            Label(new Rect(x + 38, y + 475, 670, 54), Difficulty == 0 ? "Room to learn: a longer opening and lighter enemy waves." : Difficulty == 1 ? "A measured opening followed by regular combined arms attacks." : "A shorter opening, stronger reinforcements, and sustained pressure.", body);
            Label(new Rect(x + 38, y + 551, 680, 38), "OBJECTIVE   Destroy the opposing headquarters. Keep yours standing.", small);
            if (Button(new Rect(x + 38, y + 610, 185, 53), "<  BACK")) State = ScreenState.MainMenu;
            if (Button(new Rect(x + 240, y + 610, 474, 53), "BEGIN OPERATION     >", true)) Deploy();
            MetalPanel(new Rect(x + 752, y + 106, 450, 557), panel);
            DrawMap(new Rect(x + 774, y + 129, 406, 284), false);
            Label(new Rect(x + 778, y + 436, 395, 45), "VERDANT REACH", heading);
            Label(new Rect(x + 778, y + 488, 395, 66), "Wide green fields flank a winding river. Three concrete crossings connect the front. Choose your route and control the bridges.", body);
            Rule(x + 778, y + 580, 398);
            Label(new Rect(x + 778, y + 598, 395, 46), "300 x 210 m   /   1 vs AI\nStarting base + strike group + construction rig", small);
        }
        void FactionCard(Rect r, string id, string name, string doctrine, string text, Color color)
        {
            bool selected = ChosenFaction == id;
            MetalPanel(r, selected ? new Color(.11f, .14f, .095f) : panel);
            Fill(new Rect(r.x, r.y, r.width, selected ? 4 : 1), color);
            Label(new Rect(r.x + 18, r.y + 24, r.width - 30, 32), name, body);
            Color previousSmall = small.normal.textColor, previousTiny = tiny.normal.textColor;
            if (selected) small.normal.textColor = tiny.normal.textColor = white;
            Label(new Rect(r.x + 18, r.y + 64, r.width - 30, 24), doctrine, tiny);
            Label(new Rect(r.x + 18, r.y + 103, r.width - 30, 61), text, small);
            Label(new Rect(r.x + 18, r.y + 169, r.width - 30, 25), selected ? "[ SELECTED ]" : "SELECT FACTION  >", small);
            small.normal.textColor = previousSmall; tiny.normal.textColor = previousTiny;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { ChosenFaction = id; audioSource.PlayOneShot(clickSound); }
        }

        void DrawLoading()
        {
            Fill(new Rect(0, 0, width, height), ink);
            Label(new Rect(width / 2 - 300, height / 2 - 92, 700, 50), "ESTABLISHING COMMAND", heading);
            Label(new Rect(width / 2 - 300, height / 2 - 22, 700, 35), "VERDANT REACH  /  " + ChosenFaction.ToUpperInvariant(), body);
            Fill(new Rect(width / 2 - 300, height / 2 + 40, 600, 3), muted);
            Fill(new Rect(width / 2 - 300, height / 2 + 40, 600 * Mathf.PingPong(Time.unscaledTime, 1), 3), lime);
        }

        string ShortName(ArmyDefinition def)
        {
            string[] parts = def.id.Split('_'); string key = parts[parts.Length - 1];
            switch (key) { case "Command": return "Headquarters"; case "Power": return "Power plant"; case "Refinery": return "Supply depot"; case "Barracks": return "Barracks"; case "Factory": return "Vehicle factory"; case "Airfield": return "Airfield"; case "Tech": return "Research center"; case "Turret": return "Ground defense"; case "AirDefense": return "Air defense"; case "Superweapon": return "Strategic cannon"; default: return def.displayName; }
        }
        void DrawHealthBars()
        {
            foreach (var u in Simulation.Units)
            {
                if (!u || !u.IsAlive || !Simulation.IsVisible(u) || (!u.Selected && Mathf.Approximately(u.Health, u.MaxHealth) && u.BuildProgress >= 1)) continue;
                Vector3 p = sceneCamera.WorldToScreenPoint(u.Position + Vector3.up * u.ScreenHeight);
                if (p.z < 0) continue;
                Vector2 at = GuiMouse(p);
                if (at.y < 63 || at.y > height - 270 || at.x < 0 || at.x > width) continue;
                Fill(new Rect(at.x - 25, at.y - 2, 50, 6), ink);
                Fill(new Rect(at.x - 24, at.y - 1, 48 * Mathf.Clamp01(u.Health / u.MaxHealth), 4), u.Team == 0 ? new Color(.42f, .78f, .25f) : EnemyAccent);
                if (u.BuildProgress < 1) { Fill(new Rect(at.x - 24, at.y + 7, 48 * u.BuildProgress, 3), lime); }
            }
        }
        void Border(Rect r, Color color, float thickness)
        { Fill(new Rect(r.x, r.y, r.width, thickness), color); Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color); Fill(new Rect(r.x, r.y, thickness, r.height), color); Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color); }

        void BuildMinimap()
        {
            mapTexture = new Texture2D(300, 210, TextureFormat.RGB24, false);
            var pixels = new Color[300 * 210];
            for (int z = 0; z < 210; z++) for (int x = 0; x < 300; x++)
            {
                var p = new Vector3(x - 150, 0, z - 105);
                float d = Mathf.Abs(p.x - DemoBattlefield.RiverCenter(p.z));
                Color c = Color.Lerp(new Color(.23f, .34f, .19f), new Color(.36f, .45f, .24f), Mathf.PerlinNoise(x * .05f, z * .05f));
                if (d < 14) c = new Color(.55f, .56f, .36f);
                if (d < 11) c = new Color(.12f, .39f, .47f);
                foreach (float bridge in DemoBattlefield.BridgeZ) if (Mathf.Abs(p.z - bridge) < 9 && d < 24) c = new Color(.68f, .70f, .65f);
                pixels[z * 300 + x] = c;
            }
            mapTexture.SetPixels(pixels); mapTexture.Apply();
        }
        Vector2 MapPoint(Rect r, Vector3 p) => new Vector2(r.x + (p.x + 150) / 300 * r.width, r.y + (105 - p.z) / 210 * r.height);
        void DrawMap(Rect r, bool interactive)
        {
            GUI.DrawTexture(r, mapTexture);
            if (interactive && Simulation.FogTexture) GUI.DrawTexture(r, Simulation.FogTexture);
            for (int i = 1; i < 6; i++)
            {
                Fill(new Rect(r.x + r.width * i / 6, r.y, 1, r.height), new Color(.73f, .82f, .48f, .12f));
                Fill(new Rect(r.x, r.y + r.height * i / 6, r.width, 1), new Color(.73f, .82f, .48f, .12f));
            }
            Border(r, new Color(.40f, .47f, .27f), 2);
            if (interactive)
            {
                foreach (var u in Simulation.Units) if (u && u.IsAlive && Simulation.IsVisible(u))
                {
                    Vector2 p = MapPoint(r, u.Position); float size = u.IsStructure ? 6 : 3;
                    Fill(new Rect(p.x - size / 2 - 1, p.y - size / 2 - 1, size + 2, size + 2), Color.black);
                    Fill(new Rect(p.x - size / 2, p.y - size / 2, size, size), u.Team == 0 ? Accent : EnemyAccent);
                    if (u.Selected) Border(new Rect(p.x - size / 2 - 2, p.y - size / 2 - 2, size + 4, size + 4), white, 1);
                }
                float bottom = 266 * scale;
                Vector2 a = MapPoint(r, GroundPoint(new Vector2(0, bottom)));
                Vector2 b = MapPoint(r, GroundPoint(new Vector2(Screen.width, bottom)));
                Vector2 c = MapPoint(r, GroundPoint(new Vector2(Screen.width, Screen.height - 55 * scale)));
                Vector2 d = MapPoint(r, GroundPoint(new Vector2(0, Screen.height - 55 * scale)));
                DrawMapLine(a, b); DrawMapLine(b, c); DrawMapLine(c, d); DrawMapLine(d, a);
                var e = Event.current;
                if (r.Contains(e.mousePosition) && e.type == EventType.MouseDown)
                {
                    Vector3 world = new Vector3((e.mousePosition.x - r.x) / r.width * 300 - 150, 0, 105 - (e.mousePosition.y - r.y) / r.height * 210);
                    if (e.button == 0) FocusCamera(world, wantedZoom);
                    else if (e.button == 1) { Simulation.MoveUnits(FriendlySelection, world, attackMove); attackMove = false; }
                    e.Use();
                }
            }
            else
            {
                Vector2 a = MapPoint(r, new Vector3(-106, 0, 0)), b = MapPoint(r, new Vector3(106, 0, 0));
                Fill(new Rect(a.x - 7, a.y - 7, 14, 14), Accent); Fill(new Rect(b.x - 7, b.y - 7, 14, 14), EnemyAccent);
                Rect friendlyLabel = new Rect(a.x - 43, a.y + 12, 86, 23);
                Rect enemyLabel = new Rect(b.x - 35, b.y + 12, 70, 23);
                Fill(friendlyLabel, ink); Fill(enemyLabel, ink);
                Label(friendlyLabel, "YOUR BASE", centerLabel);
                Label(enemyLabel, "ENEMY", centerLabel);
            }
        }

        void DrawMapLine(Vector2 a, Vector2 b)
        {
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y, Vector2.Distance(a, b), 1), white);
            GUI.matrix = previous;
        }

        void DrawMatchOverlay()
        {
            Fill(new Rect(0, 0, width, height), new Color(.015f, .035f, .045f, .80f));
            bool paused = State == ScreenState.Paused, won = State == ScreenState.Victory;
            float x = width / 2 - 285, y = height / 2 - 310;
            Fill(new Rect(x, y, 570, 620), ink); Fill(new Rect(x, y, 570, 4), paused ? lime : won ? cyan : red);
            Label(new Rect(x + 38, y + 35, 490, 27), "VERDANT REACH / " + (paused ? "COMMAND SUSPENDED" : "OPERATION COMPLETE"), small);
            Label(new Rect(x + 38, y + 84, 490, 53), paused ? "GAME PAUSED" : won ? "VICTORY" : "DEFEAT", heading);
            Label(new Rect(x + 38, y + 151, 490, 54), paused ? "Your forces are standing by." : won ? "Enemy headquarters neutralized.\nThe river corridor is yours." : "Your headquarters has fallen.\nRegroup and choose a new approach.", body);
            Rule(x + 38, y + 233, 494);
            Label(new Rect(x + 38, y + 254, 495, 40), "TIME  " + Clock(Simulation.Elapsed) + "     KILLS  " + Simulation.Kills + "     LOSSES  " + Simulation.Losses, small);
            if (Button(new Rect(x + 38, y + 318, 494, 57), paused ? "RESUME OPERATION" : "PLAY AGAIN", true)) { if (paused) ResumeMatch(); else Deploy(); }
            if (Button(new Rect(x + 38, y + 390, 239, 49), paused ? "SETTINGS" : "CHANGE FACTION")) { if (paused) OpenSettings(); else { ReturnToMenu(); OpenSetup(); } }
            if (Button(new Rect(x + 291, y + 390, 241, 49), paused ? "FIELD MANUAL" : "REVIEW MAP")) { if (paused) OpenManual(); else { ReturnToMenu(); OpenSetup(); } }
            if (Button(new Rect(x + 38, y + 456, 494, 49), "RETURN TO MAIN MENU")) ReturnToMenu();
            Label(new Rect(x + 38, y + 548, 495, 36), paused ? "Returning to the menu ends the current skirmish." : "Every crossing offers a new line of attack.", small);
        }
        void DrawSettings()
        {
            Fill(new Rect(0, 0, width, height), new Color(.02f, .04f, .05f, .86f));
            float x = width / 2 - 340, y = height / 2 - 290;
            Fill(new Rect(x, y, 680, 580), ink);
            Label(new Rect(x + 40, y + 35, 600, 46), "SETTINGS", heading);
            Label(new Rect(x + 40, y + 112, 500, 30), "Interface & command volume", body);
            volume = GUI.HorizontalSlider(new Rect(x + 40, y + 154, 480, 25), volume, 0, 1);
            Label(new Rect(x + 550, y + 145, 88, 32), Mathf.RoundToInt(volume * 100) + "%", body);
            Label(new Rect(x + 40, y + 202, 500, 30), "Camera movement speed", body);
            sensitivity = GUI.HorizontalSlider(new Rect(x + 40, y + 244, 480, 25), sensitivity, .4f, 2);
            Label(new Rect(x + 550, y + 235, 88, 32), sensitivity.ToString("0.0") + "x", body);
            if (Button(new Rect(x + 40, y + 300, 600, 49), "SCREEN EDGE PANNING    " + (edgePan ? "ON" : "OFF"))) edgePan = !edgePan;
            if (Button(new Rect(x + 40, y + 363, 600, 49), "DISPLAY    " + (Screen.fullScreen ? "FULLSCREEN" : "WINDOWED"))) Screen.fullScreen = !Screen.fullScreen;
            if (Button(new Rect(x + 40, y + 463, 600, 57), "SAVE & RETURN", true))
            {
                PlayerPrefs.SetFloat("NF.Volume", volume); PlayerPrefs.SetFloat("NF.CameraSpeed", sensitivity); PlayerPrefs.SetInt("NF.EdgePan", edgePan ? 1 : 0); PlayerPrefs.Save(); State = returnScreen;
            }
            AudioListener.volume = volume;
        }
        void DrawManual()
        {
            Fill(new Rect(0, 0, width, height), new Color(.02f, .04f, .05f, .87f));
            float x = width / 2 - 550, y = height / 2 - 350;
            Fill(new Rect(x, y, 1100, 700), ink);
            Label(new Rect(x + 38, y + 30, 1000, 48), "FIELD MANUAL", heading);
            Label(new Rect(x + 38, y + 96, 495, 35), "COMMAND YOUR FORCES", body);
            Label(new Rect(x + 38, y + 146, 485, 361), "Left-click / drag     Select a unit / group\nShift + click            Add or remove a unit\nRight-click              Move, attack, or set rally\nX then left-click       Attack along a route\nH                              Halt selected units\nTab                           Select all combat units\nCtrl + 1–5 / 1–5       Store / recall a group\n\nWASD / arrows        Pan the camera\nMiddle mouse drag  Pan with the mouse\nMouse wheel           Zoom\nQ / E                        Orbit\nSpace / F                 Focus HQ / selection\nEscape                     Pause or cancel placement", body);
            Label(new Rect(x + 582, y + 96, 480, 35), "WIN THE RIVER CORRIDOR", body);
            Label(new Rect(x + 582, y + 146, 470, 376), "01  ESTABLISH\nSelect the construction rig. Build supply depots for recurring credits, production buildings for reinforcements, and research to unlock advanced forces.\n\n02  REINFORCE\nSelect a barracks, vehicle factory, or airfield. Click a unit to queue it. Right-click the battlefield to set the building's rally point.\n\n03  ADVANCE\nGround forces cross the river at the concrete bridges. Aircraft cross directly. Use different crossings to flank.\n\n04  FINISH\nDestroy the enemy headquarters while defending your own. Enemy pressure increases over time.", body);
            Rule(x + 38, y + 551, 1024);
            Label(new Rect(x + 38, y + 570, 760, 83), "FIELD SYSTEMS / Scout to reveal enemy positions. Keep your power grid supplied.\nResearch armor, weapons and faster production. Cancel queued units for a full refund.\nAirfields reserve four plane slots. Planes return to their own pads to reload.\nUnits engage automatically; support rigs repair nearby allies.", small);
            if (Button(new Rect(x + 836, y + 604, 226, 53), "RETURN", true)) State = returnScreen;
        }
        string Clock(float time) => ((int)time / 60).ToString("00") + ":" + ((int)time % 60).ToString("00");
        void Quit()
        {
            if (Application.isEditor) { Notify("Exit is available in the standalone player."); return; }
            Application.Quit();
        }
        void OnDestroy()
        {
            Time.timeScale = 1;
            if (Simulation) Simulation.MatchEnded -= EndMatch;
            if (mapTexture) Destroy(mapTexture);
            if (steelTexture) Destroy(steelTexture);
            if (clickSound) Destroy(clickSound);
            if (commandSound) Destroy(commandSound);
        }
    }
}
