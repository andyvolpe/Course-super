using UnityEngine;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Sim.Tournament;
using Greenkeeper.Unity.Managers;
using Greenkeeper.Unity.Play;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// One consolidated HUD replacing the six scattered IMGUI panels: a top status bar (clock / cash /
    /// reputation / condition / next event / mode), the morning-window panel (Plan mode), a context
    /// panel for the focused green with per-green actions and earned reads, a compact forecast strip,
    /// and the putt meter (Course mode). No overlaps; readable; gives real per-green control.
    /// </summary>
    public sealed class GreenkeeperHud : MonoBehaviour
    {
        public GameManager game;
        public GreenInspectionController inspection;
        public PuttingController putt;
        public GameBootstrap bootstrap;

        private int _selectedGreen;
        private Vector2 _queueScroll;
        private float _rejectFlashUntil;

        private bool Plan => bootstrap == null || bootstrap.PlanMode;
        private int Day => game.Director.Clock.DayIndex;

        private void OnGUI()
        {
            if (game == null || game.Director == null || game.Course == null) return;
            int W = Screen.width, H = Screen.height;
            int topH = Mathf.Clamp(H / 18, 30, 50);
            int foreH = Mathf.Clamp(H / 7, 84, 130);
            int contentTop = topH + 6;
            int contentBottom = H - foreH - 6;
            int leftW = (int)Mathf.Clamp(W * 0.32f, 330, 470);
            int rightW = (int)Mathf.Clamp(W * 0.30f, 330, 430);

            DrawTopBar(W, topH);
            DrawForecast(W, H, foreH);
            if (Plan) DrawWindow(new Rect(8, contentTop, leftW, contentBottom - contentTop));
            DrawContext(new Rect(W - rightW - 8, contentTop, rightW, contentBottom - contentTop));
            if (!Plan) DrawCourseOverlay(W, H, foreH);
        }

        // ---- top bar ----
        private void DrawTopBar(int W, int topH)
        {
            var eco = game.Economy;
            double cond = ConditionSystem.CourseCondition(game.Course, EconomyConfig.Default);
            GUILayout.BeginArea(new Rect(0, 0, W, topH), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Day {Day}</b> · {game.Director.Clock.Season}", Rich());
            GUILayout.FlexibleSpace();
            if (eco != null)
            {
                string cc = eco.Cash < 0 ? "#ff6060" : "#a0ffa0";
                GUILayout.Label($"Cash <color={cc}>${eco.Cash:N0}</color>", Rich());
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Rep {eco.Reputation:F0}", Rich());
                GUILayout.FlexibleSpace();
            }
            GUILayout.Label($"Condition {cond:F0}/100", Rich());
            GUILayout.FlexibleSpace();
            var ladder = game.Tournament;
            if (ladder != null && ladder.Current != null)
                GUILayout.Label($"Next: {ladder.Current.Name} in {Mathf.Max(0, ladder.DaysUntilNext(Day))}d", Rich());
            GUILayout.FlexibleSpace();
            GUILayout.Label(Plan ? "<color=#ffe0a0><b>PLAN</b></color> (TAB=walk)" : "<color=#a0e0ff><b>COURSE</b></color> (TAB=plan)", Rich());
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---- forecast strip ----
        private void DrawForecast(int W, int H, int foreH)
        {
            if (game.Forecast == null) return;
            GUILayout.BeginArea(new Rect(0, H - foreH, W, foreH), GUI.skin.box);
            GUILayout.Label("<b>Forecast</b> — bands widen further out (the gamble)", Rich());
            GUILayout.BeginHorizontal();
            foreach (var d in game.Forecast.Upcoming(Day))
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(150));
                string warn = (d.PredictedHeatSpike ? "<color=#ff8060>HEAT</color> " : "")
                            + (d.PredictedStorm ? "<color=#80a0ff>STORM</color> " : "")
                            + (d.PredictedFrost ? "<color=#a0e0ff>FROST</color> " : "");
                GUILayout.Label($"<b>+{d.DaysOut}d</b>  {(string.IsNullOrEmpty(warn) ? "" : warn)}", Rich());
                GUILayout.Label($"{d.Predicted.TmaxF:0}/{d.Predicted.TminF:0}°F ±{d.TempBandF:0}");
                GUILayout.Label($"rain {d.Predicted.RainMm:0}±{d.RainBandMm:0}  hum {d.Predicted.Humidity:0%}");
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---- morning window (Plan mode) ----
        private void DrawWindow(Rect area)
        {
            var w = game.Window;
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("<b>Morning window</b>", Rich());
            float frac = w.BudgetHours > 0 ? (float)(w.UsedHours / w.BudgetHours) : 0;
            GUILayout.Label($"Crew hours {w.UsedHours:F1}/{w.BudgetHours:F0}  (left {w.RemainingHours:F1})");
            GUILayout.Label(Bar(frac, 28), Rich());
            if (Time.realtimeSinceStartup < _rejectFlashUntil)
                GUILayout.Label("<color=#ff6060>not enough hours — cut something</color>", Rich());

            GUILayout.Space(4);
            GUILayout.Label("Add for all greens:");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Mow")) ForEachGreen(g => TaskCatalog.WalkMow(g.Id));
            if (GUILayout.Button("Roll")) ForEachGreen(g => TaskCatalog.Roll(g.Id));
            if (GUILayout.Button("Spray")) ForEachGreen(g => TaskCatalog.Spray(g.Id));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Water")) ForEachGreen(g => TaskCatalog.Water(g.Id));
            if (GUILayout.Button("Fertilize")) ForEachGreen(g => TaskCatalog.Fertilize(g.Id));
            if (GUILayout.Button("Aerate")) ForEachGreen(g => TaskCatalog.Aerate(g.Id));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rake bunkers")) AddTask(TaskCatalog.RakeBunkers());
            if (GUILayout.Button("Triplex")) AddTask(TaskCatalog.Triplex());
            if (GUILayout.Button("Clear")) game.BeginWindow();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label($"Queue ({w.Accepted.Count}):");
            _queueScroll = GUILayout.BeginScrollView(_queueScroll, GUILayout.ExpandHeight(true));
            foreach (var task in w.Accepted)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{task.Type} {Short(task.ZoneId)} {task.HoursCost:F1}h");
                GUILayout.FlexibleSpace();
                task.Delegated = GUILayout.Toggle(task.Delegated, task.Delegated ? "staff" : "me", GUILayout.Width(70));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RESOLVE DAY")) game.ResolveWindow();
            if (GUILayout.Button("SKIP quiet days")) game.SkipRoutineDays();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ---- context: the focused green ----
        private void DrawContext(Rect area)
        {
            GUILayout.BeginArea(area, GUI.skin.box);

            string id = FocusedGreenId();
            if (string.IsNullOrEmpty(id))
            {
                GUILayout.Label(Plan ? "Select a green." : "Look at a green to read it.");
                GUILayout.EndArea();
                return;
            }

            // Green selector in Plan mode.
            if (Plan)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<", GUILayout.Width(34))) _selectedGreen--;
                GUILayout.Label($"<b>{id}</b>", Rich());
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(">", GUILayout.Width(34))) _selectedGreen++;
                GUILayout.EndHorizontal();
            }
            else GUILayout.Label($"<b>{id}</b>  (looking)", Rich());

            var zone = game.Course.Get(id);
            if (zone == null) { GUILayout.EndArea(); return; }
            var obs = game.Legibility.Observe(zone, Day);
            int cell = (!Plan && inspection != null) ? Mathf.Clamp(inspection.AimedCellIndex, 0, obs.Tells.Length - 1) : 0;
            TellAppearance t = obs.Tells[cell];

            GUILayout.Space(4);
            GUILayout.Label("<b>What you can see</b>", Rich());
            GUILayout.Label(DescribeTells(t));

            GUILayout.Space(4);
            GUILayout.Label("<b>Earned reads</b>", Rich());
            GUILayout.Label(obs.InfectionRevealed ? $"infection {obs.RevealedMaxInfection:0.#}  symptom {obs.RevealedMeanExpression:0.#}" : "infection: unknown");
            GUILayout.Label(obs.MoistureMetered ? $"moisture {obs.MeteredMoisturePct:0.#}%" : "moisture: unknown");
            GUILayout.Label(obs.SoilTested ? $"N {obs.RevealedNitrogenPct:0.#}  OM {obs.RevealedOrganicMatterPct:0.#}%" : "nutrients: unknown");
            if (obs.TurfDebtShown) GUILayout.Label($"<color=#ffd0d0>turf debt {obs.TurfDebtPct:0}</color>", Rich());
            if (obs.ThreatTelegraphed) GUILayout.Label($"<color=#ffe0a0>{obs.ThreatNote}</color>", Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Scout")) game.Legibility.Scout(id, Day);
            if (GUILayout.Button("Meter")) game.Legibility.MeterReading(zone, cell, Day);
            if (GUILayout.Button("Soil test")) game.Legibility.SoilTest(id, Day);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Add to today's plan</b> (this green)", Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Water")) AddTask(TaskCatalog.Water(id));
            if (GUILayout.Button("Fertilize")) AddTask(TaskCatalog.Fertilize(id));
            if (GUILayout.Button("Spray")) AddTask(TaskCatalog.Spray(id));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Mow")) AddTask(TaskCatalog.WalkMow(id));
            if (GUILayout.Button("Roll")) AddTask(TaskCatalog.Roll(id));
            if (GUILayout.Button("Aerate")) AddTask(TaskCatalog.Aerate(id));
            GUILayout.EndHorizontal();

            // Tournament target for this green, if an event is upcoming.
            var ladder = game.Tournament;
            if (ladder != null && ladder.Current != null)
            {
                var s = ladder.Current;
                GUILayout.Space(4);
                GUILayout.Label($"<b>{s.Name}</b> target: Stimp {s.StimpMin:0.0}-{s.StimpMax:0.0}, firm {s.FirmMin:0}-{s.FirmMax:0}", Rich());
            }

            GUILayout.EndArea();
        }

        // ---- course overlay: crosshair + putt meter ----
        private void DrawCourseOverlay(int W, int H, int foreH)
        {
            GUI.Label(new Rect(W / 2 - 5, H / 2 - 10, 14, 20), "+");
            if (putt == null) return;
            var r = new Rect(W / 2 - 130, H - foreH - 74, 260, 64);
            GUILayout.BeginArea(r, GUI.skin.box);
            GUILayout.Label($"Strokes {putt.Strokes}{(putt.Holed ? "  — HOLED!" : "")}");
            GUILayout.HorizontalSlider(putt.Power, 0f, 1f);
            GUILayout.Label(putt.Charging ? (putt.ApproachMode ? "approach… release" : "putt… release") : "hold LMB putt / RMB approach");
            GUILayout.EndArea();
        }

        // ---- helpers ----
        private string FocusedGreenId()
        {
            if (!Plan) return inspection != null ? inspection.AimedZoneId : null;
            var greens = new System.Collections.Generic.List<ZoneState>(game.Course.Greens);
            if (greens.Count == 0) return null;
            _selectedGreen = ((_selectedGreen % greens.Count) + greens.Count) % greens.Count;
            return greens[_selectedGreen].Id;
        }

        private void ForEachGreen(System.Func<ZoneState, TaskOrder> make)
        {
            foreach (var g in game.Course.Greens) AddTask(make(g));
        }

        private void AddTask(TaskOrder t)
        {
            t.Delegated = true;
            if (game.Crew.Count > 0) t.AssignedCrewId = game.Crew[0].Id;
            if (!game.Window.TryAssign(t)) _rejectFlashUntil = Time.realtimeSinceStartup + 1.5f;
        }

        private static string DescribeTells(TellAppearance t)
        {
            string depth = t.BaseColor.R > 0.45 ? "pale (starved?)" : (t.BaseColor.R < 0.2 ? "deep green" : "green");
            string s = $"colour: {depth}";
            if (t.Lesions > 0.15) s += "  · <color=#e8d088>lesions</color>";
            if (t.Thinning > 0.25) s += "  · <color=#d0a070>thinning</color>";
            if (t.WetSheen > 0.3) s += "  · <color=#90b0ff>wet/soft</color>";
            if (t.WiltTint > 0.3) s += "  · <color=#b0c0c0>wilting</color>";
            return s;
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "(all)" : id;

        private static string Bar(float frac, int width)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(frac * width), 0, width);
            string col = frac >= 1f ? "#ff6060" : (frac > 0.85f ? "#ffd060" : "#80ff80");
            return $"[<color={col}>{new string('#', n)}</color>{new string('.', width - n)}]";
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
