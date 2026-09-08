using UnityEngine;

namespace NeonFrontier
{
    public sealed partial class GameFlowController
    {
        void DrawHud()
        {
            hoverTitle = hoverBody = hoverFooter = null;
            DrawHealthBars();
            MetalPanel(new Rect(0, 0, width, 55), ink, false);
            Label(new Rect(21, 14, 246, 28), "NEON FRONTIER", body);
            Label(new Rect(278, 18, 290, 23), "VERDANT REACH / " + ChosenFaction.ToUpperInvariant(), tiny);
            Label(new Rect(width / 2 - 92, 7, 228, 39), "$ " + Simulation.Credits.ToString("N0"), number);
            Label(new Rect(width / 2 + 155, 18, 238, 24), "SUPPLIES  +" + Simulation.IncomePerMinute + " / MIN", tiny);
            Label(new Rect(width - 328, 17, 131, 28), Clock(Simulation.Elapsed), body);
            if (Button(new Rect(width - 165, 9, 150, 38), "MENU  [ESC]")) PauseMatch();
            MetalPanel(new Rect(width - 344, 69, 330, 48), ink, false);
            Label(new Rect(width - 329, 82, 303, 26), "OBJECTIVE: Destroy enemy headquarters", tiny);
            if (dragging && Vector2.Distance(dragStart, dragEnd) > 7)
            {
                Rect selectionBox = ScreenBox(GuiMouse(dragStart), GuiMouse(dragEnd));
                Fill(selectionBox, new Color(.6f, .85f, .4f, .09f)); Border(selectionBox, new Color(.61f, .89f, .36f), 1);
            }

            float y = height - 266;
            // A continuous, machined command deck anchors radar, unit identification and orders.
            MetalPanel(new Rect(0, y, width, 266), new Color(.16f, .18f, .135f));
            Fill(new Rect(1, y + 3, width - 2, 3), lime);
            MetalPanel(new Rect(8, y + 13, 293, 242), ink);
            Label(new Rect(22, y + 23, 165, 22), "BATTLEFIELD RADAR", tiny);
            Label(new Rect(218, y + 23, 62, 22), "N  ^", tiny);
            DrawMap(MinimapRect, true);
            DrawPowerMeter(new Rect(23, height - 29, 252, 8));
            Tooltip(MinimapRect, "BATTLEFIELD RADAR", "Left-click to move the camera. Right-click to send selected forces to that location.", ChosenFaction == "Vanguard" ? "BLUE / FRIENDLY   RED / HOSTILE   WHITE / CAMERA" : "RED / FRIENDLY   BLUE / HOSTILE   WHITE / CAMERA");

            var unit = Selection.Count > 0 ? Selection[0] : null;
            MetalPanel(new Rect(311, y + 13, 358, 242), ink);
            Label(new Rect(326, y + 24, 327, 28), Selection.Count > 1 ? Selection.Count + " UNITS SELECTED" : unit ? unit.Definition.displayName : "COMMAND NETWORK", body);
            Portrait(new Rect(326, y + 64, 170, 128), unit ? unit.Definition : HeadquartersDefinition());
            Border(new Rect(326, y + 64, 170, 128), new Color(.41f, .43f, .34f), 1);
            if (unit)
            {
                DrawIdentification(unit, new Rect(507, y + 64, 146, 161));
                Color condition = unit.Health / unit.MaxHealth > .5f ? new Color(.44f, .75f, .27f) : unit.Health / unit.MaxHealth > .25f ? lime : red;
                Fill(new Rect(327, y + 201, 168, 9), Color.black);
                Fill(new Rect(328, y + 202, 166 * Mathf.Clamp01(unit.Health / unit.MaxHealth), 7), condition);
                Label(new Rect(326, y + 215, 172, 23), Mathf.CeilToInt(unit.Health) + " / " + Mathf.CeilToInt(unit.MaxHealth) + "  HP", centerLabel);
                Tooltip(new Rect(326, y + 64, 170, 174), unit.Definition.displayName, unit.Definition.description,
                    unit.Team == 0 ? unit.CurrentOrder + (unit.IsStructure ? " / Right-click sets rally point" : " / F focuses camera") : "HOSTILE CONTACT", unit.Team == 0 ? Accent : red);
            }
            else
            {
                Label(new Rect(507, y + 68, 146, 67), "Select a unit or structure to issue orders.", small);
                Label(new Rect(507, y + 145, 146, 66), "TAB  Select army\nSPACE  Find HQ\nB  Construction", tiny);
                Label(new Rect(326, y + 206, 170, 26), ChosenFaction.ToUpperInvariant(), centerLabel);
            }

            DrawCommands(new Rect(684, y + 21, width - 861, 223), unit);
            DrawOrderButtons(new Rect(width - 164, y + 20, 148, 226), unit);
            string notice = placing ? "PLACE " + ShortName(placing).ToUpperInvariant() + " / " + (string.IsNullOrEmpty(placementReason) ? "Left-click to build" : placementReason) + " / Right-click cancels"
                : attackMove ? "ATTACK MOVE / Left-click a destination / Esc cancels" : Time.unscaledTime < noticeUntil ? localNotice : Simulation.Notice;
            if (!string.IsNullOrEmpty(notice))
            {
                MetalPanel(new Rect(311, y - 41, width - 325, 33), ink, false);
                Label(new Rect(325, y - 35, width - 352, 25), notice, small);
            }
            DrawControlGroups(y);
            DrawTooltip();
        }

        ArmyDefinition HeadquartersDefinition()
        {
            if (roster != null) foreach (var definition in roster)
                if (definition && definition.id == ChosenFaction + "_Command") return definition;
            return null;
        }

        void DrawPowerMeter(Rect rect)
        {
            int generated = Simulation.PowerGenerated(), consumed = Simulation.PowerConsumed();
            bool power = Simulation.HasPower();
            Fill(rect, Color.black);
            float capacity = Mathf.Max(1, generated, consumed);
            Fill(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * generated / capacity, rect.height - 2), new Color(.36f, .54f, .19f));
            float demand = (rect.width - 2) * consumed / capacity;
            Fill(new Rect(rect.x + 1, rect.y + 1, demand, rect.height - 2), power ? lime : red);
            Fill(new Rect(rect.x + demand, rect.y - 2, 2, rect.height + 4), white);
            Tooltip(new Rect(rect.x, rect.y - 4, rect.width, rect.height + 10), power ? "POWER GRID OPERATIONAL" : "LOW POWER",
                "Generated: " + generated + "   Demand: " + consumed + "\nBuild power plants to support production, research and base defenses.", power ? "RESERVE " + (generated - consumed) : "BUILD ADDITIONAL POWER PLANTS", power ? lime : red);
        }

        void DrawIdentification(RtsUnit unit, Rect rect)
        {
            Label(new Rect(rect.x, rect.y, rect.width, 37), unit.Definition.category.ToUpperInvariant() + " / T" + unit.Definition.tier, tiny);
            Label(new Rect(rect.x, rect.y + 33, rect.width, 57), unit.Definition.role, small);
            Rule(rect.x, rect.y + 94, rect.width);
            Label(new Rect(rect.x, rect.y + 102, rect.width, 22), unit.CurrentOrder, tiny);
            string rank = unit.VeteranRank > 0 ? new string('*', unit.VeteranRank) + "  " + (unit.VeteranRank == 3 ? "HEROIC" : unit.VeteranRank == 2 ? "ELITE" : "VETERAN") : "REGULAR";
            string status = unit.Key == "Airfield" ? "PLANE SLOTS " + Simulation.AirfieldSlotsUsed(unit) + " / 4" : unit.IsFixedWing ? "AMMO " + unit.AircraftAmmo + " / 4" : unit.IsStructure ? "RALLY POINT READY" : rank;
            Label(new Rect(rect.x, rect.y + 131, rect.width, 22), status, tiny);
            if (unit.BuildProgress < 1)
            {
                Fill(new Rect(rect.x, rect.y + 158, rect.width, 5), Color.black);
                Fill(new Rect(rect.x, rect.y + 158, rect.width * unit.BuildProgress, 5), lime);
            }
        }

        void DrawOrderButtons(Rect rect, RtsUnit unit)
        {
            bool friendly = unit && unit.Team == 0;
            Label(new Rect(rect.x, rect.y, rect.width, 20), "FIELD COMMANDS", tiny);
            if (Button(new Rect(rect.x, rect.y + 29, rect.width, 35), "ATTACK [X]", attackMove, friendly && !unit.IsStructure))
            { attackMove = true; Notify("ATTACK MOVE / Choose a destination"); }
            if (Button(new Rect(rect.x, rect.y + 71, rect.width, 35), "HOLD [H]", false, friendly)) Simulation.StopUnits(FriendlySelection);
            if (Button(new Rect(rect.x, rect.y + 113, rect.width, 35), "BUILD [B]", buildPanel && SelectedWorker(), SelectedWorker()))
            { buildPanel = !buildPanel; RefreshProduction(); }
            if (Button(new Rect(rect.x, rect.y + 155, rect.width, 35), "FOCUS [F]", false, unit)) FocusCamera(unit.Position, 36);
            Label(new Rect(rect.x, rect.y + 200, rect.width, 25), "ARMY " + Simulation.UnitCount(0) + "/" + SkirmishSimulation.UnitCap, tiny);
        }

        void DrawCommands(Rect rect, RtsUnit unit)
        {
            if (!unit || unit.Team != 0)
            {
                Label(new Rect(rect.x, rect.y + 2, rect.width, 24), unit ? "ENEMY INTELLIGENCE" : "COMMAND AND CONTROL", tiny);
                if (unit)
                {
                    Label(new Rect(rect.x, rect.y + 48, rect.width - 18, 93), unit.Definition.description, body);
                    Label(new Rect(rect.x, rect.y + 161, rect.width, 43), "Right-click this target with your forces selected to concentrate fire.", small);
                }
                else
                {
                    Label(new Rect(rect.x, rect.y + 44, rect.width - 16, 65), "Build an economy. Produce a balanced force.\nControl the crossings and destroy the enemy headquarters.", body);
                    Rule(rect.x, rect.y + 125, rect.width - 15);
                    Label(new Rect(rect.x, rect.y + 142, rect.width - 16, 75), "LEFT CLICK / DRAG   Select     RIGHT CLICK   Move or attack\nSHIFT + CLICK   Add selection     CTRL + 1–5   Assign a group\nWASD   Pan     Q / E   Orbit     WHEEL   Zoom", small);
                }
                return;
            }
            if (unit.Key == "Tech") { DrawResearch(rect, unit); return; }
            bool construction = buildPanel && SelectedWorker();
            Label(new Rect(rect.x, rect.y + 1, rect.width, 21), construction ? "CONSTRUCTION / SELECT A BLUEPRINT" : unit.IsStructure ? "PRODUCTION / SELECT TO RECRUIT" : "UNIT COMMAND", tiny);
            if (production.Count > 0)
            {
                int columns = 5;
                float cardWidth = (rect.width - 20) / columns;
                float cardHeight = 76;
                for (int i = 0; i < production.Count; i++)
                {
                    var definition = production[i];
                    Rect card = new Rect(rect.x + (i % columns) * (cardWidth + 5), rect.y + 29 + (i / columns) * 83, cardWidth, cardHeight);
                    string reason = Simulation.GetRequirement(definition);
                    if (string.IsNullOrEmpty(reason) && Simulation.Credits < definition.cost) reason = "Insufficient funds";
                    if (!construction && string.IsNullOrEmpty(reason)) Simulation.CanQueueUnit(unit, definition, out reason);
                    bool available = string.IsNullOrEmpty(reason);
                    bool hover = card.Contains(Event.current.mousePosition);
                    MetalPanel(card, hover ? new Color(.36f, .37f, .24f) : panel, false);
                    Portrait(new Rect(card.x + 3, card.y + 3, card.width - 6, 42), definition, !available);
                    Fill(new Rect(card.x + 4, card.y + 4, 50, 18), new Color(.025f, .03f, .018f, .92f));
                    Label(new Rect(card.x + 8, card.y + 4, 57, 19), "$" + definition.cost, tiny);
                    if (!available) Label(new Rect(card.xMax - 47, card.y + 4, 44, 19), "LOCK", tiny);
                    Label(new Rect(card.x + 3, card.y + 47, card.width - 6, 28), ShortName(definition), cardLabel);
                    if (hover) Border(card, available ? lime : red, 2);
                    Tooltip(card, definition.displayName, definition.role + "\n" + definition.description,
                        available ? "$" + definition.cost + " / TIER " + definition.tier + (construction ? " / CLICK TO PLACE" : " / CLICK TO QUEUE") : reason, available ? lime : red);
                    if (GUI.Button(card, GUIContent.none, GUIStyle.none))
                    {
                        if (!available) Notify(reason);
                        else if (construction) BeginPlacement(definition);
                        else if (Simulation.QueueUnit(unit, definition)) audioSource.PlayOneShot(clickSound);
                        else Notify(Simulation.Notice);
                    }
                }
            }
            else DrawSelectionSummary(rect, unit);
            if (unit.Production.Count > 0) DrawProductionQueue(new Rect(rect.x, rect.y + 197, rect.width, 28), unit);
            else if (unit.IsStructure) Label(new Rect(rect.x, rect.y + 199, rect.width, 23), "QUEUE EMPTY / Right-click the battlefield to set a rally point", tiny);
        }

        void DrawProductionQueue(Rect rect, RtsUnit unit)
        {
            Label(new Rect(rect.x, rect.y + 5, 105, 20), "QUEUE " + unit.Production.Count + "/5", tiny);
            for (int i = 0; i < unit.Production.Count; i++)
            {
                var order = unit.Production[i];
                Rect card = new Rect(rect.x + 87 + i * 56, rect.y - 2, 49, 29);
                Portrait(card, order.Definition);
                Border(card, i == 0 ? lime : muted, 1);
                if (i == 0) Fill(new Rect(card.x, card.yMax - 3, card.width * order.Progress, 3), lime);
                string queueDetail = "Waiting in the production queue";
                if (i == 0)
                {
                    string eta = ProductionTimeLabel(unit, order, out string blockedReason);
                    queueDetail = string.IsNullOrEmpty(blockedReason) ? "Training / " + eta + " remaining" : "PAUSED / " + blockedReason;
                }
                Tooltip(card, order.Definition.displayName, queueDetail, "CLICK TO CANCEL / FULL REFUND");
                if (GUI.Button(card, GUIContent.none, GUIStyle.none)) { Simulation.CancelProduction(unit, i); break; }
            }
            var active = unit.Production.Count > 0 ? unit.Production[0] : null;
            if (active != null)
            {
                float x = rect.x + 383;
                string eta = ProductionTimeLabel(unit, active, out _);
                Label(new Rect(x, rect.y + 4, Mathf.Max(0, rect.width - 383), 24), eta + "  /  " + Mathf.FloorToInt(active.Progress * 100) + "%", tiny);
            }
        }

        string ProductionTimeLabel(RtsUnit producer, ProductionOrder order, out string blockedReason)
        {
            blockedReason = Simulation.GetRequirement(order.Definition, producer.Team);
            if (!string.IsNullOrEmpty(blockedReason)) return "PAUSED";
            float rate = (Simulation.HasPower(producer.Team) ? 1f : .3f) *
                (Simulation.HasUpgrade("Logistics", producer.Team) ? 1.3f : 1f);
            return Mathf.CeilToInt(order.Remaining / rate) + "s";
        }

        void DrawSelectionSummary(Rect rect, RtsUnit unit)
        {
            Label(new Rect(rect.x, rect.y + 37, rect.width, 33), unit.IsWorker ? "CONSTRUCTION UNIT READY" : "COMBAT GROUP READY", body);
            Label(new Rect(rect.x, rect.y + 79, rect.width, 45), unit.IsWorker ? "Open construction to expand your base. Place structures near your headquarters or existing infrastructure." : "Right-click to move or focus fire. Attack move engages hostile units along your route.", small);
            int count = Mathf.Min(Selection.Count, 9);
            for (int i = 0; i < count; i++)
            {
                RtsUnit selected = Selection[i];
                var portrait = new Rect(rect.x + i * 61, rect.y + 136, 55, 43);
                Portrait(portrait, selected.Definition);
                Border(portrait, selected.Team == 0 ? Accent : red, 1);
                Tooltip(portrait, selected.Definition.displayName, selected.Definition.role, "CLICK TO SELECT THIS UNIT");
                if (GUI.Button(portrait, GUIContent.none, GUIStyle.none)) { SelectUnit(selected); break; }
            }
        }

        void DrawResearch(Rect rect, RtsUnit unit)
        {
            Label(new Rect(rect.x, rect.y + 1, rect.width, 23), "RESEARCH & DEVELOPMENT / ARMY UPGRADES", tiny);
            string[] keys = { "Armor", "Weapons", "Logistics" };
            string[] names = { "COMPOSITE ARMOR", "ADVANCED MUNITIONS", "FIELD LOGISTICS" };
            string[] descriptions = { "+25% maximum health for existing units and future recruits.", "+20% weapon damage for your army and defensive emplacements.", "+30% production speed at all recruitment structures." };
            float w = (rect.width - 16) / 3;
            for (int i = 0; i < 3; i++)
            {
                string key = keys[i];
                bool owned = Simulation.HasUpgrade(key);
                string reason = Simulation.UpgradeRequirement(key);
                int cost = Simulation.UpgradeCost(key);
                bool available = !owned && string.IsNullOrEmpty(reason) && Simulation.Credits >= cost && unit.BuildProgress >= 1;
                var card = new Rect(rect.x + i * (w + 8), rect.y + 34, w, 153);
                MetalPanel(card, owned ? new Color(.21f, .28f, .14f) : panel);
                Label(new Rect(card.x + 12, card.y + 15, w - 24, 35), names[i], centerLabel);
                Label(new Rect(card.x + 12, card.y + 61, w - 24, 53), descriptions[i], small);
                Label(new Rect(card.x + 12, card.y + 121, w - 24, 24), owned ? "RESEARCHED" : "$" + cost, centerLabel);
                if (card.Contains(Event.current.mousePosition)) Border(card, available ? lime : muted, 2);
                Tooltip(card, names[i], descriptions[i], owned ? "UPGRADE COMPLETE" : !string.IsNullOrEmpty(reason) ? reason : Simulation.Credits < cost ? "Insufficient funds" : "CLICK TO RESEARCH / $" + cost);
                if (GUI.Button(card, GUIContent.none, GUIStyle.none))
                {
                    if (available && Simulation.PurchaseUpgrade(key)) audioSource.PlayOneShot(commandSound);
                    else Notify(owned ? "This upgrade is already researched." : string.IsNullOrEmpty(reason) ? "Insufficient funds or construction incomplete." : reason);
                }
            }
            Label(new Rect(rect.x, rect.y + 204, rect.width, 23), "Research applies immediately to your army.", tiny);
        }

        void DrawControlGroups(float hudY)
        {
            for (int i = 0; i < 5; i++)
            {
                int alive = 0;
                if (groups.TryGetValue(i, out var group)) foreach (var unit in group) if (unit && unit.IsAlive && unit.Team == 0) alive++;
                var rect = new Rect(17 + i * 57, hudY - 37, 51, 28);
                MetalPanel(rect, alive > 0 ? panel : ink, false);
                Label(rect, (i + 1) + (alive > 0 ? " / " + alive : ""), centerLabel);
                Tooltip(rect, "CONTROL GROUP " + (i + 1), "Ctrl + " + (i + 1) + " assigns selected units. Press " + (i + 1) + " or click here to recall.", alive + " UNITS ASSIGNED");
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && alive > 0)
                {
                    ClearSelection(); foreach (var unit in group) if (unit && unit.IsAlive && unit.Team == 0) AddSelection(unit);
                    buildPanel = SelectedWorker(); RefreshProduction();
                }
            }
        }
    }
}
