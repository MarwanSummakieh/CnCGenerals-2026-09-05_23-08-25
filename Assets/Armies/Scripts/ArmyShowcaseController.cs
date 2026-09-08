using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonFrontier
{
    /// <summary>Camera, picking, and an IMGUI design browser. This atlas does not simulate combat.</summary>
    public sealed class ArmyShowcaseController : MonoBehaviour
    {
        public Camera sceneCamera;
        public Vector3 vanguardFocus = new Vector3(-52, 0, 0);
        public Vector3 dynastyFocus = new Vector3(52, 0, 0);
        public ArmyDefinition[] roster;

        private readonly Color background = new Color(.025f, .045f, .075f, .96f);
        private readonly Color textColor = new Color(.91f, .95f, .98f);
        private readonly Color muted = new Color(.48f, .59f, .67f);
        private readonly Color cyan = new Color(.22f, .84f, 1f);
        private readonly Color amber = new Color(1f, .47f, .2f);
        private readonly string[] categories = { "All", "Infantry", "Vehicle", "Aircraft", "Structure" };
        private readonly string[] categoryLabels = { "ALL", "INF", "VEH", "AIR", "BASE" };
        private readonly Dictionary<string, ArmyEntity> entities = new Dictionary<string, ArmyEntity>();
        private readonly List<ArmyDefinition> filtered = new List<ArmyDefinition>();
        private ArmyDefinition selectedDefinition;
        private ArmyEntity selectedEntity;
        private string faction = "Vanguard";
        private int categoryIndex;
        private Vector2 scroll;
        private bool showInterface = true;
        private bool dragging;
        private Vector3 initialPosition, initialFocus, currentFocus, desiredFocus;
        private Quaternion initialRotation;
        private float initialSize, desiredSize;
        private float uiScale = 1, guiWidth, guiHeight;
        private GUIStyle titleStyle, smallStyle, bodyStyle, nameStyle, rowStyle, tinyStyle, statStyle;
        private Rect sidebarRect, topbarRect, detailRect;

        private Color Accent => faction == "Dynasty" ? amber : cyan;

        private void Start()
        {
            if (!sceneCamera) sceneCamera = Camera.main;
            if (!sceneCamera) { enabled = false; return; }
            initialPosition = sceneCamera.transform.position;
            initialRotation = sceneCamera.transform.rotation;
            initialSize = desiredSize = sceneCamera.orthographicSize;
            var ground = new Plane(Vector3.up, Vector3.zero);
            var ray = sceneCamera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            initialFocus = ground.Raycast(ray, out var distance) ? ray.GetPoint(distance) : Vector3.zero;
            currentFocus = desiredFocus = initialFocus;
            foreach (var entity in FindObjectsByType<ArmyEntity>(FindObjectsSortMode.None))
                if (entity.definition && !entities.ContainsKey(entity.definition.id))
                    entities.Add(entity.definition.id, entity);
            RebuildFilter();
            UpdateLayout();
        }

        private void Update()
        {
            if (!sceneCamera) return;
            UpdateLayout();
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            if (keyboard != null)
            {
                if (keyboard.hKey.wasPressedThisFrame) showInterface = !showInterface;
                if (keyboard.homeKey.wasPressedThisFrame) ResetView();
                if (keyboard.digit1Key.wasPressedThisFrame) FocusFaction("Vanguard");
                if (keyboard.digit2Key.wasPressedThisFrame) FocusFaction("Dynasty");
                if (keyboard.escapeKey.wasPressedThisFrame) { selectedDefinition = null; selectedEntity = null; }
                if (keyboard.fKey.wasPressedThisFrame && selectedEntity) FocusEntity(selectedEntity);

                Vector3 movement = Vector3.zero;
                Vector3 right = Vector3.ProjectOnPlane(sceneCamera.transform.right, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(sceneCamera.transform.forward, Vector3.up).normalized;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement -= right;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement += right;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement += forward;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement -= forward;
                float multiplier = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed ? 2f : 1f;
                desiredFocus += movement.normalized * (desiredSize * .8f) * multiplier * dt;
                float orbit = (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0);
                if (orbit != 0) sceneCamera.transform.RotateAround(currentFocus, Vector3.up, orbit * 55f * dt);
            }

            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();
                bool overUI = IsOverInterface(position);
                float wheel = mouse.scroll.ReadValue().y;
                if (!overUI && Mathf.Abs(wheel) > .01f)
                    desiredSize = Mathf.Clamp(desiredSize * Mathf.Pow(.87f, Mathf.Clamp(wheel / 120f, -3f, 3f)), 4, 105);
                if (mouse.middleButton.wasPressedThisFrame && !overUI) dragging = true;
                if (!mouse.middleButton.isPressed) dragging = false;
                if (dragging)
                {
                    var ground = new Plane(Vector3.up, Vector3.zero);
                    var previousRay = sceneCamera.ScreenPointToRay(position - mouse.delta.ReadValue());
                    var currentRay = sceneCamera.ScreenPointToRay(position);
                    if (ground.Raycast(previousRay, out var previousDistance) && ground.Raycast(currentRay, out var currentDistance))
                        desiredFocus += previousRay.GetPoint(previousDistance) - currentRay.GetPoint(currentDistance);
                }
                if (mouse.leftButton.wasPressedThisFrame && !overUI &&
                    Physics.Raycast(sceneCamera.ScreenPointToRay(position), out var hit, 2000f))
                {
                    var entity = hit.collider.GetComponentInParent<ArmyEntity>();
                    if (entity && entity.definition) Select(entity.definition, false);
                }
            }
            desiredFocus.x = Mathf.Clamp(desiredFocus.x, -145, 145);
            desiredFocus.z = Mathf.Clamp(desiredFocus.z, -115, 115);
            desiredFocus.y = 0;
            Vector3 next = Vector3.Lerp(currentFocus, desiredFocus, 1 - Mathf.Exp(-10f * dt));
            sceneCamera.transform.position += next - currentFocus;
            currentFocus = next;
            sceneCamera.orthographicSize = Mathf.Lerp(sceneCamera.orthographicSize, desiredSize, 1 - Mathf.Exp(-10f * dt));
        }

        public void ResetView()
        {
            if (!sceneCamera) return;
            sceneCamera.transform.SetPositionAndRotation(initialPosition, initialRotation);
            sceneCamera.orthographicSize = desiredSize = initialSize;
            currentFocus = desiredFocus = initialFocus;
        }

        public void FocusFaction(string factionId)
        {
            faction = factionId == "Dynasty" ? "Dynasty" : "Vanguard";
            selectedDefinition = null;
            selectedEntity = null;
            desiredFocus = faction == "Dynasty" ? dynastyFocus : vanguardFocus;
            desiredSize = 46;
            if (sceneCamera)
                desiredFocus -= Vector3.ProjectOnPlane(sceneCamera.transform.right, Vector3.up).normalized * desiredSize * .35f;
            scroll = Vector2.zero;
            RebuildFilter();
        }

        public void SelectDefinition(string id)
        {
            if (roster == null) return;
            foreach (var definition in roster)
                if (definition && definition.id == id) { Select(definition, true); return; }
        }

        private void Select(ArmyDefinition definition, bool focus)
        {
            if (!definition) return;
            selectedDefinition = definition;
            entities.TryGetValue(definition.id, out selectedEntity);
            if (faction != definition.faction) { faction = definition.faction; RebuildFilter(); }
            if (focus && selectedEntity) FocusEntity(selectedEntity);
        }

        private void FocusEntity(ArmyEntity entity)
        {
            Bounds bounds = entity.DisplayBounds;
            desiredFocus = bounds.center;
            desiredFocus.y = 0;
            float modelSize = bounds.extents.magnitude * 1.45f;
            desiredSize = entity.isStructure ? Mathf.Clamp(modelSize, 16, 24) :
                entity.definition && entity.definition.category == "Infantry" ? Mathf.Clamp(modelSize, 4.5f, 7) :
                Mathf.Clamp(modelSize, 8, 12);
            // Place the model in the clear scene area to the right of the browser.
            Vector3 right = Vector3.ProjectOnPlane(sceneCamera.transform.right, Vector3.up).normalized;
            desiredFocus -= right * desiredSize * .22f;
            Vector3 forward = Vector3.ProjectOnPlane(sceneCamera.transform.forward, Vector3.up).normalized;
            desiredFocus -= forward * desiredSize * .22f;
        }

        private void RebuildFilter()
        {
            filtered.Clear();
            if (roster == null) return;
            foreach (var definition in roster)
                if (definition && definition.faction == faction &&
                    (categoryIndex == 0 || definition.category == categories[categoryIndex])) filtered.Add(definition);
        }

        private void UpdateLayout()
        {
            uiScale = Mathf.Max(.1f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
            guiWidth = Screen.width / uiScale;
            guiHeight = Screen.height / uiScale;
            topbarRect = new Rect(0, 0, guiWidth, 66);
            sidebarRect = new Rect(18, 84, 276, guiHeight - 106);
            detailRect = new Rect(312, guiHeight - 206, guiWidth - 332, 184);
        }

        private bool IsOverInterface(Vector2 screenPosition)
        {
            if (!showInterface) return false;
            Vector2 p = new Vector2(screenPosition.x / uiScale, (Screen.height - screenPosition.y) / uiScale);
            return topbarRect.Contains(p) || sidebarRect.Contains(p) ||
                (selectedDefinition && detailRect.Contains(p));
        }

        private void MakeStyles()
        {
            if (titleStyle != null) return;
            titleStyle = Style(23, textColor, FontStyle.Bold);
            smallStyle = Style(12, muted);
            bodyStyle = Style(14, textColor);
            bodyStyle.wordWrap = true;
            nameStyle = Style(22, textColor, FontStyle.Bold);
            rowStyle = Style(14, textColor, FontStyle.Bold);
            tinyStyle = Style(10, muted);
            statStyle = Style(20, textColor, FontStyle.Bold);
        }

        private static GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight, padding = new RectOffset(0, 0, 0, 0) };
            style.normal.textColor = color;
            return style;
        }

        private void OnGUI()
        {
            if (!sceneCamera) return;
            MakeStyles();
            UpdateLayout();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1));
            if (!showInterface)
            {
                Fill(new Rect(18, 18, 164, 31), background);
                GUI.Label(new Rect(30, 26, 150, 20), "H  /  SHOW INTERFACE", smallStyle);
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
                return;
            }
            DrawSelectionBrackets();
            DrawTopbar();
            DrawSidebar();
            GUI.Label(new Rect(314, 86, guiWidth - 334, 22),
                "WASD / ARROWS  PAN     SCROLL  ZOOM     MIDDLE DRAG  PAN     Q / E  ORBIT     F  FOCUS     H  HIDE", tinyStyle);
            if (selectedDefinition) DrawDetails();
            else
            {
                var hint = new Rect(314, guiHeight - 74, Mathf.Min(640, guiWidth - 340), 52);
                Fill(hint, background);
                Fill(new Rect(hint.x, hint.y, 3, hint.height), Accent);
                GUI.Label(new Rect(hint.x + 16, hint.y + 10, hint.width - 32, 20), "SELECT A MODEL TO INSPECT ITS DESIGN", rowStyle);
                GUI.Label(new Rect(hint.x + 16, hint.y + 31, hint.width - 32, 16), "Click in the scene or choose a roster entry.  1 / 2 faction view  ·  Home overview", tinyStyle);
            }
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawTopbar()
        {
            Fill(topbarRect, background);
            Fill(new Rect(18, 19, 5, 28), cyan);
            GUI.Label(new Rect(34, 10, 360, 31), "NEON FRONTIER", titleStyle);
            GUI.Label(new Rect(35, 42, 410, 16), "FACTION DESIGN ATLAS  /  CYBERPUNK COMBINED ARMS", tinyStyle);
            GUI.Label(new Rect(guiWidth - 523, 17, 285, 20), "02 ARMIES     32 UNITS     20 STRUCTURES", smallStyle);
            GUI.Label(new Rect(guiWidth - 523, 38, 310, 16), "VISUAL PROTOTYPE  ·  NO COMBAT SIMULATION", tinyStyle);
            if (Button(new Rect(guiWidth - 195, 18, 175, 32), "OVERVIEW  /  HOME", false, cyan)) ResetView();
            Fill(new Rect(0, 65, guiWidth, 1), new Color(.22f, .38f, .46f, .65f));
        }

        private void DrawSidebar()
        {
            Fill(sidebarRect, background);
            float x = sidebarRect.x + 14;
            GUI.Label(new Rect(x, 100, 220, 18), "FACTION DIRECTORY", tinyStyle);
            if (Button(new Rect(x, 125, 120, 34), "01  VANGUARD", faction == "Vanguard", cyan)) FocusFaction("Vanguard");
            if (Button(new Rect(x + 126, 125, 120, 34), "02  DYNASTY", faction == "Dynasty", amber)) FocusFaction("Dynasty");
            Color previous = GUI.color;
            GUI.color = Accent;
            GUI.Label(new Rect(x, 178, 246, 28), faction == "Vanguard" ? "PACIFIC VANGUARD" : "CRIMSON DYNASTY", rowStyle);
            GUI.color = previous;
            GUI.Label(new Rect(x, 204, 246, 42), faction == "Vanguard"
                ? "Precision warfare. Networked armor.\nAir dominance and specialist support."
                : "Industrial power. Relentless armor.\nMass production and area denial.", smallStyle);
            for (int i = 0; i < categories.Length; i++)
                if (Button(new Rect(x + i * 50, 257, 46, 29), categoryLabels[i], categoryIndex == i, Accent))
                { categoryIndex = i; scroll = Vector2.zero; RebuildFilter(); }
            GUI.Label(new Rect(x, 301, 246, 20), categories[categoryIndex].ToUpperInvariant() + "  /  " + filtered.Count.ToString("00") + " DESIGNS", tinyStyle);
            var viewport = new Rect(x - 2, 329, 254, sidebarRect.yMax - 374);
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, 235, filtered.Count * 49), false, true);
            for (int i = 0; i < filtered.Count; i++)
            {
                var definition = filtered[i];
                var rect = new Rect(0, i * 49, 232, 45);
                bool selected = selectedDefinition == definition;
                Fill(rect, selected ? new Color(Accent.r, Accent.g, Accent.b, .16f) : new Color(.12f, .2f, .26f, .3f));
                if (selected) Fill(new Rect(rect.x, rect.y, 3, rect.height), Accent);
                GUI.Label(new Rect(10, rect.y + 7, 213, 20), definition.displayName, bodyStyle);
                GUI.Label(new Rect(10, rect.y + 29, 213, 14), definition.category.ToUpperInvariant() + "   /   T" + definition.tier + "   /   " + definition.cost.ToString("N0") + " CR", tinyStyle);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) Select(definition, true);
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(x, sidebarRect.yMax - 30, 246, 18), "DESIGN VALUES  /  SUBJECT TO PLAYTEST", tinyStyle);
        }

        private void DrawDetails()
        {
            ArmyDefinition d = selectedDefinition;
            Fill(detailRect, background);
            Fill(new Rect(detailRect.x, detailRect.y, detailRect.width, 2), Accent);
            float x = detailRect.x + 18, y = detailRect.y + 15;
            float informationWidth = detailRect.width * .57f;
            GUI.Label(new Rect(x, y, informationWidth, 18), d.category.ToUpperInvariant() + "   /   TIER " + d.tier + "   /   " + d.role.ToUpperInvariant(), tinyStyle);
            GUI.Label(new Rect(x, y + 23, informationWidth, 29), d.displayName, nameStyle);
            GUI.Label(new Rect(x, y + 58, informationWidth - 10, 62), d.description, bodyStyle);
            GUI.Label(new Rect(x, y + 130, informationWidth, 30), "STRONG AGAINST  " + Join(d.strengths), smallStyle);
            GUI.Label(new Rect(x, y + 151, informationWidth, 25), "COUNTERED BY  " + Join(d.weaknesses), tinyStyle);
            float statX = detailRect.x + informationWidth + 35;
            float statWidth = (detailRect.xMax - statX - 20) / 4;
            GUI.Label(new Rect(statX, y, 320, 20), "PROPOSED DESIGN STATS", tinyStyle);
            DrawStat(statX, y + 36, statWidth, "COST / CR", d.cost.ToString("N0"));
            DrawStat(statX + statWidth, y + 36, statWidth, "HEALTH", d.health.ToString("N0"));
            DrawStat(statX + statWidth * 2, y + 36, statWidth, "RANGE / M", d.range > 0 ? d.range.ToString("0.#") : "—");
            DrawStat(statX + statWidth * 3, y + 36, statWidth, "SPEED / M/S", d.speed > 0 ? d.speed.ToString("0.#") : "—");
            if (Button(new Rect(statX, y + 103, 150, 32), "FOCUS MODEL  /  F", false, Accent) && selectedEntity) FocusEntity(selectedEntity);
            if (Button(new Rect(detailRect.xMax - 105, y + 103, 85, 32), "CLOSE", false, muted))
            { selectedDefinition = null; selectedEntity = null; }
            GUI.Label(new Rect(statX, y + 146, detailRect.xMax - statX - 20, 22), "Concept specifications. Movement and combat are not simulated.", tinyStyle);
        }

        private void DrawStat(float x, float y, float width, string label, string value)
        {
            GUI.Label(new Rect(x, y, width, 18), label, tinyStyle);
            GUI.Label(new Rect(x, y + 20, width, 28), value, statStyle);
        }

        private void DrawSelectionBrackets()
        {
            if (!selectedEntity) return;
            Bounds bounds = selectedEntity.DisplayBounds;
            Vector3 p = sceneCamera.WorldToScreenPoint(bounds.center);
            if (p.z < 0) return;
            Vector3 up = sceneCamera.WorldToScreenPoint(bounds.center + sceneCamera.transform.up * bounds.extents.magnitude);
            float radius = Mathf.Clamp(Mathf.Abs(up.y - p.y) / uiScale + 8, 20, 180);
            var box = new Rect(p.x / uiScale - radius, (Screen.height - p.y) / uiScale - radius, radius * 2, radius * 2);
            const float length = 12, thickness = 2;
            Fill(new Rect(box.x, box.y, length, thickness), Accent);
            Fill(new Rect(box.x, box.y, thickness, length), Accent);
            Fill(new Rect(box.xMax - length, box.y, length, thickness), Accent);
            Fill(new Rect(box.xMax - thickness, box.y, thickness, length), Accent);
            Fill(new Rect(box.x, box.yMax - thickness, length, thickness), Accent);
            Fill(new Rect(box.x, box.yMax - length, thickness, length), Accent);
            Fill(new Rect(box.xMax - length, box.yMax - thickness, length, thickness), Accent);
            Fill(new Rect(box.xMax - thickness, box.yMax - length, thickness, length), Accent);
        }

        private bool Button(Rect rect, string label, bool selected, Color accent)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Fill(rect, new Color(accent.r, accent.g, accent.b, selected ? .22f : hover ? .15f : .055f));
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), new Color(accent.r, accent.g, accent.b, selected ? .9f : .3f));
            Color old = GUI.color;
            GUI.color = selected || hover ? accent : textColor;
            GUI.Label(new Rect(rect.x + 8, rect.y + (rect.height - 14) / 2, rect.width - 12, 20), label, tinyStyle);
            GUI.color = old;
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private static string Join(string[] values) => values == null ? "—" : string.Join(" / ", values);

        private static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
