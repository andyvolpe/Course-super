using UnityEngine;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Tournament;
using Greenkeeper.Unity.Managers;
using Greenkeeper.Unity.Play;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Single consolidated HUD, styled with <see cref="GreenkeeperTheme"/> (calm championship palette).
    /// Top status bar, morning-window panel (Plan), focused-green context panel with per-green actions,
    /// forecast strip, and the putt meter (Course). No overlaps.
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

        // Button actions are DEFERRED to the top of the next OnGUI pass and run OUTSIDE any layout
        // group. Mutating state (adding tasks, resolving a day) mid-layout changes the control count
        // between the Layout and Repaint passes, which corrupts IMGUI ("Invalid GUILayout state").
        private System.Action _pending;

        private GreenkeeperTheme T => GreenkeeperTheme.I;
        private bool Plan => bootstrap == null || bootstrap.PlanMode;
        private int Day => game.Director.Clock.DayIndex;

        private void OnGUI()
        {
            if (game == null || game.Director == null || game.Course == null) return;

            // Run any queued button action before opening a single layout group (see _pending).
            if (_pending != null) { var act = _pending; _pending = null; act(); }

            int W = Screen.width, H = Screen.height;
            T.Ensure(Mathf.Clamp(H / 56, 13, 28));

            int topH = Mathf.Clamp(H / 17, 34, 54);
            int foreH = Mathf.Clamp(H / 7, 92, 140);
            int cTop = topH + 8, cBot = H - foreH - 8;
            int leftW = (int)Mathf.Clamp(W * 0.30f, 340, 470);
            int rightW = (int)Mathf.Clamp(W * 0.30f, 340, 440);

            DrawTopBar(W, topH);
            DrawForecast(W, H, foreH);
            if (Plan) DrawWindow(new Rect(10, cTop, leftW, cBot - cTop));
            DrawContext(new Rect(W - rightW - 10, cTop, rightW, cBot - cTop));
            if (!Plan) DrawCourseOverlay(W, H, foreH);
        }

        private void DrawTopBar(int W, int topH)
        {
            var eco = game.Economy;
            double cond = ConditionSystem.CourseCondition(game.Course, EconomyConfig.Default);
            GUILayout.BeginArea(new Rect(0, 0, W, topH), T.Panel);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>GREENKEEPER</b>", T.GoldText);
            Sep(); GUILayout.Label($"Day <b>{Day}</b> · {game.Director.Clock.Season}", T.Body);
            if (eco != null)
            {
                Sep(); GUILayout.Label($"Cash {Money(eco.Cash)}", T.Body);
                Sep(); GUILayout.Label($"Reputation <b>{eco.Reputation:F0}</b>", T.Body);
            }
            Sep(); GUILayout.Label($"Condition <b>{cond:F0}</b>", T.Body);
            var ladder = game.Tournament;
            if (ladder != null && ladder.Current != null)
            { Sep(); GUILayout.Label($"{ladder.Current.Name} in <b>{Mathf.Max(0, ladder.DaysUntilNext(Day))}d</b>", T.Body); }
            GUILayout.FlexibleSpace();
            GUILayout.Label(Plan ? "<b>PLAN</b>  ·  TAB to walk" : "<b>COURSE</b>  ·  TAB to plan", T.GoldText);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawForecast(int W, int H, int foreH)
        {
            if (game.Forecast == null) return;
            GUILayout.BeginArea(new Rect(0, H - foreH, W, foreH), T.Panel);
            GUILayout.Label("FORECAST  —  uncertainty widens further out", T.Section);
            GUILayout.BeginHorizontal();
            foreach (var d in game.Forecast.Upcoming(Day))
            {
                GUILayout.BeginVertical(GUILayout.Width(150));
                string warn = (d.PredictedHeatSpike ? $"<color=#{Hex(T.Clay)}>HEAT</color> " : "")
                            + (d.PredictedStorm ? "<color=#9CC0E8>STORM</color> " : "")
                            + (d.PredictedFrost ? "<color=#BFE0EC>FROST</color> " : "");
                GUILayout.Label($"<b>+{d.DaysOut}d</b>  {warn}", T.Body);
                GUILayout.Label($"{d.Predicted.TmaxF:0}/{d.Predicted.TminF:0}°F ±{d.TempBandF:0}", T.Dim);
                GUILayout.Label($"rain {d.Predicted.RainMm:0}±{d.RainBandMm:0} · hum {d.Predicted.Humidity:0%}", T.Dim);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawWindow(Rect area)
        {
            var w = game.Window;
            GUILayout.BeginArea(area, T.Panel);
            GUILayout.Label("MORNING WINDOW", T.Section);

            float frac = w.BudgetHours > 0 ? (float)(w.UsedHours / w.BudgetHours) : 0;
            var br = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
            T.Bar(br, frac, $"crew hours  {w.UsedHours:F1} / {w.BudgetHours:F0}");
            if (Time.realtimeSinceStartup < _rejectFlashUntil)
                GUILayout.Label($"<color=#{Hex(T.Clay)}>not enough hours — cut something</color>", T.Body);

            GUILayout.Space(6);
            GUILayout.Label("Order for all greens", T.Dim);
            Row(("Mow", () => ForEachGreen(g => TaskCatalog.WalkMow(g.Id))),
                ("Roll", () => ForEachGreen(g => TaskCatalog.Roll(g.Id))),
                ("Spray", () => ForEachGreen(g => TaskCatalog.Spray(g.Id))));
            Row(("Water", () => ForEachGreen(g => TaskCatalog.Water(g.Id))),
                ("Aerate", () => ForEachGreen(g => TaskCatalog.Aerate(g.Id))));
            GUILayout.Label("Fertility (all greens)", T.Dim);
            Row(("Foliar N+K", () => ForEachGreen(g => TaskCatalog.FeedFoliar(g.Id))),
                ("Granular slow", () => ForEachGreen(g => TaskCatalog.FeedGranularSlow(g.Id))));
            Row(("Granular QUICK", () => ForEachGreen(g => TaskCatalog.FeedGranularQuick(g.Id))),
                ("Iron — colour", () => ForEachGreen(g => TaskCatalog.Iron(g.Id))));
            Row(("Rake bunkers", () => AddTask(TaskCatalog.RakeBunkers())),
                ("Triplex", () => AddTask(TaskCatalog.Triplex())),
                ("Clear", () => game.BeginWindow()));

            GUILayout.Space(6);
            GUILayout.Label($"Queue · {w.Accepted.Count} task(s)", T.Dim);
            _queueScroll = GUILayout.BeginScrollView(_queueScroll, GUILayout.ExpandHeight(true));
            foreach (var task in w.Accepted)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{task.Type} {Short(task.ZoneId)} · {task.HoursCost:F1}h", T.Body);
                GUILayout.FlexibleSpace();
                task.Delegated = GUILayout.Toggle(task.Delegated, task.Delegated ? "staff" : "me", T.Toggle, GUILayout.Width(76));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            Row(("RESOLVE DAY", () => game.ResolveWindow()),
                ("SKIP quiet days", () => game.SkipRoutineDays()));
            GUILayout.EndArea();
        }

        private void DrawContext(Rect area)
        {
            GUILayout.BeginArea(area, T.Panel);
            string id = FocusedGreenId();
            if (string.IsNullOrEmpty(id))
            {
                GUILayout.Label(Plan ? "Select a green." : "Look at a green to read it.", T.Body);
                GUILayout.EndArea();
                return;
            }

            GUILayout.BeginHorizontal();
            if (Plan && GUILayout.Button("<", T.Button, GUILayout.Width(38))) _pending = () => _selectedGreen--;
            GUILayout.Label($"{id.ToUpper()}{(Plan ? "" : "  (looking)")}", T.Section);
            if (Plan && GUILayout.Button(">", T.Button, GUILayout.Width(38))) _pending = () => _selectedGreen++;
            GUILayout.EndHorizontal();

            var zone = game.Course.Get(id);
            if (zone == null) { GUILayout.EndArea(); return; }
            var obs = game.Legibility.Observe(zone, Day);
            int cell = (!Plan && inspection != null) ? Mathf.Clamp(inspection.AimedCellIndex, 0, obs.Tells.Length - 1) : 0;
            TellAppearance t = obs.Tells[cell];

            GUILayout.Space(4);
            GUILayout.Label("WHAT YOU CAN SEE", T.Section);
            GUILayout.Label(DescribeTells(t), T.Body);

            GUILayout.Space(2);
            GUILayout.Label("EARNED READS", T.Section);
            GUILayout.Label(obs.InfectionRevealed ? $"infection {obs.RevealedMaxInfection:0.#} · symptom {obs.RevealedMeanExpression:0.#}" : "infection — unknown", obs.InfectionRevealed ? T.Body : T.Dim);
            GUILayout.Label(obs.MoistureMetered ? $"moisture {obs.MeteredMoisturePct:0.#}%" : "moisture — unknown", obs.MoistureMetered ? T.Body : T.Dim);
            GUILayout.Label(obs.SoilTested
                ? $"N {obs.RevealedNitrogenPct:0.#} · K {obs.RevealedPotassiumPct:0.#} · Fe {obs.RevealedIronPct:0.#} · OM {obs.RevealedOrganicMatterPct:0.#}%"
                : "nutrients — unknown", obs.SoilTested ? T.Body : T.Dim);
            if (obs.TurfDebtShown) GUILayout.Label($"<color=#{Hex(T.Clay)}>turf debt {obs.TurfDebtPct:0}</color>", T.Body);
            if (obs.ThreatTelegraphed) GUILayout.Label($"<color=#{Hex(T.Gold)}>{obs.ThreatNote}</color>", T.Body);
            Row(("Scout", () => game.Legibility.Scout(id, Day)),
                ("Meter", () => game.Legibility.MeterReading(zone, cell, Day)),
                ("Soil test", () => game.Legibility.SoilTest(id, Day)));

            GUILayout.Space(2);
            GUILayout.Label("ADD FOR THIS GREEN", T.Section);
            Row(("Water", () => AddTask(TaskCatalog.Water(id))),
                ("Spray", () => AddTask(TaskCatalog.Spray(id))),
                ("Mow", () => AddTask(TaskCatalog.WalkMow(id))));
            Row(("Roll", () => AddTask(TaskCatalog.Roll(id))),
                ("Aerate", () => AddTask(TaskCatalog.Aerate(id))));

            GUILayout.Space(2);
            GUILayout.Label("FERTILITY PROGRAM — N is a fork, not a slider", T.Section);
            Row(($"Foliar N+K ({TaskCatalog.SpoonN:0}N/{TaskCatalog.SpoonK:0}K)", () => AddTask(TaskCatalog.FeedFoliar(id))),
                ($"Granular slow ({TaskCatalog.GranularN:0}N/{TaskCatalog.GranularK:0}K)", () => AddTask(TaskCatalog.FeedGranularSlow(id))));
            Row(($"Granular QUICK ({TaskCatalog.GranularN:0}N)", () => AddTask(TaskCatalog.FeedGranularQuick(id))),
                ($"Iron — colour ({TaskCatalog.IronFe:0}Fe)", () => AddTask(TaskCatalog.Iron(id))));

            var ladder = game.Tournament;
            if (ladder != null && ladder.Current != null)
            {
                var s = ladder.Current;
                GUILayout.Space(4);
                GUILayout.Label($"<b>{s.Name}</b> target — Stimp {s.StimpMin:0.0}-{s.StimpMax:0.0}, firm {s.FirmMin:0}-{s.FirmMax:0}", T.Dim);
            }
            GUILayout.EndArea();
        }

        private void DrawCourseOverlay(int W, int H, int foreH)
        {
            GUI.Label(new Rect(W / 2 - 5, H / 2 - 12, 16, 24), "<b>+</b>", T.GoldText);
            if (putt == null) return;
            GUILayout.BeginArea(new Rect(W / 2 - 140, H - foreH - 84, 280, 74), T.Panel);
            GUILayout.Label($"Strokes <b>{putt.Strokes}</b>{(putt.Holed ? "   <color=#9CC196>HOLED!</color>" : "")}", T.Body);
            var br = GUILayoutUtility.GetRect(10, 16, GUILayout.ExpandWidth(true));
            T.Bar(br, putt.Power, "");
            GUILayout.Label(putt.Charging ? (putt.ApproachMode ? "approach — release to strike" : "putt — release to strike") : "hold LMB putt · RMB approach", T.Dim);
            GUILayout.EndArea();
        }

        // ---- helpers ----
        private void Sep() => GUILayout.Label("<color=#C8A14B>  ·  </color>", T.Body, GUILayout.ExpandWidth(false));

        private void Row(params (string label, System.Action act)[] buttons)
        {
            GUILayout.BeginHorizontal();
            foreach (var b in buttons) if (GUILayout.Button(b.label, T.Button)) _pending = b.act; // deferred
            GUILayout.EndHorizontal();
        }

        private string FocusedGreenId()
        {
            if (!Plan) return inspection != null ? inspection.AimedZoneId : null;
            var greens = new System.Collections.Generic.List<ZoneState>(game.Course.Greens);
            if (greens.Count == 0) return null;
            _selectedGreen = ((_selectedGreen % greens.Count) + greens.Count) % greens.Count;
            return greens[_selectedGreen].Id;
        }

        private void ForEachGreen(System.Func<ZoneState, TaskOrder> make)
        { foreach (var g in game.Course.Greens) AddTask(make(g)); }

        private void AddTask(TaskOrder t)
        {
            t.Delegated = true;
            if (game.Crew.Count > 0) t.AssignedCrewId = game.Crew[0].Id;
            if (!game.Window.TryAssign(t)) _rejectFlashUntil = Time.realtimeSinceStartup + 1.5f;
        }

        private string DescribeTells(TellAppearance t)
        {
            string depth = t.BaseColor.R > 0.45 ? "pale (starved?)" : (t.BaseColor.R < 0.2 ? "deep green" : "healthy green");
            string s = $"colour: {depth}";
            if (t.Lesions > 0.15) s += "  · <color=#E8D088>lesions</color>";
            if (t.Thinning > 0.25) s += "  · <color=#D6A878>thinning</color>";
            if (t.WetSheen > 0.3) s += "  · <color=#9CC0E8>wet/soft</color>";
            if (t.WiltTint > 0.3) s += "  · <color=#BFD0CC>wilting</color>";
            return s;
        }

        private string Money(double v) => $"<color=#{Hex(v < 0 ? T.Clay : T.Sage)}><b>${v:N0}</b></color>";
        private static string Short(string id) => string.IsNullOrEmpty(id) ? "(all)" : id;
        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
