using System.Collections.Generic;
using UnityEngine;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;
using Greenkeeper.Unity.Play;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// The DIEGETIC management interface (GDD §3.2). Nothing here floats in the void: every panel is the
    /// readout of a physical object the player walked up to and used in the maintenance building — the
    /// wall course-map, the wall calendar, the crew board, the NOAA forecast sheet, the soil clipboard
    /// and the fert/spray log. The act is physical; the data it opens is clean and legible. All reads go
    /// through the sim's legibility gate; this layer never invents hidden state.
    /// </summary>
    public sealed class MaintenanceRoomHud : MonoBehaviour
    {
        public GameManager game;
        public RoomInteractionController room;

        private GreenkeeperTheme T => GreenkeeperTheme.I;
        private int Day => game.Director.Clock.DayIndex;

        // Defer button side-effects to the next pass, OUTSIDE any layout group (IMGUI stability).
        private System.Action _pending;
        private int _mapHole = -1;                  // hole selected on the course map (-1 = none)
        private ZoneType _mapSurface = ZoneType.Green;
        private Vector2 _scrollMap, _scrollQueue, _scrollLog;
        private float _rejectFlashUntil;
        private Texture2D _white;

        private void Awake() { if (game == null) game = FindFirstObjectByType<GameManager>(); }

        private void OnGUI()
        {
            if (game == null || game.Director == null || game.Course == null || room == null) return;
            if (_pending != null) { var a = _pending; _pending = null; a(); }

            int W = Screen.width, H = Screen.height;
            T.Ensure(Mathf.Clamp(H / 56, 13, 28));

            if (room.PanelOpen) { DrawOpenPanel(W, H); return; }
            if (room.InRoom) DrawRoomWalking(W, H);
            // Out on the course, GreenkeeperHud owns the screen (status bar + putt meter + green reads).
        }

        // ---- walking the building: crosshair + the focus prompt + the depth-scaled loop hint ----
        private void DrawRoomWalking(int W, int H)
        {
            GUI.Label(new Rect(W / 2 - 6, H / 2 - 10, 16, 22), "<b>+</b>", T.GoldText);

            var f = room.Focused;
            if (f != null)
            {
                string verb = f.IsPickup
                    ? (f.Kind == DiegeticKind.MoistureMeter && room.CarryingMeter ? "set down" : "pick up")
                    : "use";
                string label = string.IsNullOrEmpty(f.Label) ? f.Kind.ToString() : f.Label;
                var r = new Rect(W / 2 - 190, H / 2 + 26, 380, 34);
                GUILayout.BeginArea(r, T.Panel);
                GUILayout.Label($"<b>[{room.useKey}]</b>  {label}", T.GoldText);
                GUILayout.EndArea();
            }

            // The maintenance building banner + the friction that scales with the depth dial (§0.4).
            GUILayout.BeginArea(new Rect(10, 10, 540, 96), T.Panel);
            GUILayout.Label($"MAINTENANCE BUILDING  ·  Day <b>{Day}</b> · {game.Director.Clock.Season}", T.Section);
            GUILayout.Label(room.RoutineDelegated
                ? "Routine is <b>delegated</b> — glance the crew board and run the day."
                : "<b>Hands-on</b> — read the forecast, check the map, schedule on the calendar.", T.Dim);
            if (room.CarryingMeter) GUILayout.Label("carrying the <b>moisture meter</b>", T.GoldText);
            GUILayout.EndArea();
        }

        // ---- a station's clean panel, centered ----
        private void DrawOpenPanel(int W, int H)
        {
            int pw = Mathf.Clamp((int)(W * 0.7f), 560, 1100);
            int ph = Mathf.Clamp((int)(H * 0.8f), 420, 900);
            var area = new Rect((W - pw) / 2, (H - ph) / 2, pw, ph);
            GUILayout.BeginArea(area, T.Panel);

            var kind = room.Open.Kind;
            GUILayout.BeginHorizontal();
            GUILayout.Label(TitleFor(kind), T.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"Close [{room.useKey}/Esc]", T.Button, GUILayout.Width(160))) _pending = () => room.Close();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            switch (kind)
            {
                case DiegeticKind.CourseMap:     DrawCourseMap(); break;
                case DiegeticKind.Calendar:      DrawCalendar(); break;
                case DiegeticKind.CrewBoard:     DrawCrewBoard(); break;
                case DiegeticKind.Forecast:      DrawForecast(); break;
                case DiegeticKind.SoilClipboard: DrawClipboard(); break;
                case DiegeticKind.FertLog:       DrawFertLog(); break;
            }
            GUILayout.EndArea();
        }

        private string TitleFor(DiegeticKind k) => k switch
        {
            DiegeticKind.CourseMap => "COURSE MAP",
            DiegeticKind.Calendar => "CALENDAR — schedule & delegate",
            DiegeticKind.CrewBoard => "CREW BOARD",
            DiegeticKind.Forecast => "NOAA FORECAST",
            DiegeticKind.SoilClipboard => "SOIL TEST CLIPBOARD",
            DiegeticKind.FertLog => "FERT / SPRAY LOG",
            _ => k.ToString(),
        };

        // =================== COURSE MAP (keystone) ===================
        private void DrawCourseMap()
        {
            GUILayout.Label("At-a-glance health across every surface — tells only (colour/stress), never hidden numbers. " +
                            "Pick a hole to route crew; walk out to find out WHAT'S wrong.", T.Dim);
            GUILayout.Space(4);

            // Column header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Hole", T.GoldText, GUILayout.Width(80));
            foreach (var s in Surfaces) GUILayout.Label(SurfaceShort(s), T.Dim, GUILayout.Width(54));
            GUILayout.Label("bunkers", T.Dim, GUILayout.Width(80));
            GUILayout.EndHorizontal();

            _scrollMap = GUILayout.BeginScrollView(_scrollMap, GUILayout.ExpandHeight(true));
            int holes = HoleCount();
            for (int hole = 1; hole <= holes; hole++)
            {
                GUILayout.BeginHorizontal();
                bool selHole = _mapHole == hole;
                if (GUILayout.Button($"#{hole}", selHole ? T.Toggle : T.Button, GUILayout.Width(80)))
                    _pending = () => { _mapHole = hole; };

                foreach (var s in Surfaces)
                {
                    var z = game.Course.Get(ZoneId(s, hole));
                    var chip = GUILayoutUtility.GetRect(48, 22, GUILayout.Width(54));
                    bool sel = selHole && _mapSurface == s;
                    if (Chip(chip, z != null ? ZoneTellColor(z) : new Color(0.2f, 0.2f, 0.2f), sel))
                        _pending = () => { _mapHole = hole; _mapSurface = s; };
                }

                // bunkers: worst sand consistency on the hole (washed = red)
                var br = GUILayoutUtility.GetRect(72, 22, GUILayout.Width(80));
                Chip(br, BunkerColor(hole), false);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            DrawMapRouting();
        }

        private void DrawMapRouting()
        {
            GUILayout.Space(4);
            if (_mapHole < 1) { GUILayout.Label("Select a hole/surface to send crew there.", T.Dim); return; }
            string id = ZoneId(_mapSurface, _mapHole);
            var z = game.Course.Get(id);
            GUILayout.Label($"ROUTE CREW — hole <b>#{_mapHole}</b> · {_mapSurface}", T.Section);
            if (z == null) { GUILayout.Label("No such surface.", T.Dim); return; }

            if (z.Type == ZoneType.Bunker)
            {
                Row(("Rake all bunkers", () => AddTask(TaskCatalog.RakeBunkers())));
                return;
            }
            Row(("Mow", () => AddTask(TaskCatalog.WalkMow(id))),
                ("Water", () => AddTask(TaskCatalog.Water(id))),
                ("Spray", () => AddTask(TaskCatalog.Spray(id))));
            Row(("Roll", () => AddTask(TaskCatalog.Roll(id))),
                ("Aerate", () => AddTask(TaskCatalog.Aerate(id))),
                ("Mow whole hole", () => { foreach (var s in Surfaces) { var hz = game.Course.Get(ZoneId(s, _mapHole)); if (hz != null) AddTask(TaskCatalog.WalkMow(hz.Id)); } }));
            QueueFooter();
        }

        // =================== CALENDAR (schedule + delegate) ===================
        private void DrawCalendar()
        {
            var w = game.Window;
            GUILayout.Label("Lay out today's work and decide what you do yourself vs hand to the crew (§4.2).", T.Dim);
            float frac = w.BudgetHours > 0 ? (float)(w.UsedHours / w.BudgetHours) : 0;
            var br = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            T.Bar(br, frac, $"crew hours  {w.UsedHours:F1} / {w.BudgetHours:F0}");
            if (Time.realtimeSinceStartup < _rejectFlashUntil)
                GUILayout.Label($"<color=#{Hex(T.Clay)}>not enough hours — cut something</color>", T.Body);

            GUILayout.Space(4);
            GUILayout.Label("Greens — order for all", T.Dim);
            Row(("Mow", () => ForEachGreen(g => TaskCatalog.WalkMow(g.Id))),
                ("Roll", () => ForEachGreen(g => TaskCatalog.Roll(g.Id))),
                ("Spray", () => ForEachGreen(g => TaskCatalog.Spray(g.Id))),
                ("Water", () => ForEachGreen(g => TaskCatalog.Water(g.Id))));
            Row(("Foliar N+K", () => ForEachGreen(g => TaskCatalog.FeedFoliar(g.Id))),
                ("Granular slow", () => ForEachGreen(g => TaskCatalog.FeedGranularSlow(g.Id))),
                ("Iron — colour", () => ForEachGreen(g => TaskCatalog.Iron(g.Id))),
                ("Aerate", () => ForEachGreen(g => TaskCatalog.Aerate(g.Id))));
            GUILayout.Label("Whole course — competes for the same hours", T.Dim);
            Row(("Mow fairways", () => AddTask(TaskCatalog.MowFairwaysAll())),
                ("Mow rough", () => AddTask(TaskCatalog.MowRoughAll())),
                ("Mow tees", () => AddTask(TaskCatalog.MowTeesAll())),
                ("Rake bunkers", () => AddTask(TaskCatalog.RakeBunkers())));

            QueueFooter();
            Row(("RESOLVE DAY (run my plan)", () => game.ResolveWindow()),
                ("Clear queue", () => game.BeginWindow()));
        }

        private void QueueFooter()
        {
            var w = game.Window;
            GUILayout.Space(4);
            GUILayout.Label($"Queue · {w.Accepted.Count} task(s) · each: who does it?", T.Dim);
            _scrollQueue = GUILayout.BeginScrollView(_scrollQueue, GUILayout.Height(140));
            foreach (var task in w.Accepted)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{task.Type} {Short(task.ZoneId)} · {task.HoursCost:F1}h", T.Body);
                GUILayout.FlexibleSpace();
                task.Delegated = GUILayout.Toggle(task.Delegated, task.Delegated ? "staff" : "me", T.Toggle, GUILayout.Width(80));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // =================== CREW BOARD (depth dial + delegated run) ===================
        private void DrawCrewBoard()
        {
            GUILayout.Label("The crew, the depth dial, and — on a delegated routine day — the one-glance run (§0.4).", T.Dim);
            GUILayout.Space(4);
            foreach (var c in game.Crew)
            {
                double q = Delegation.StaffQuality(c);
                GUILayout.Label($"<b>{c.Name}</b> · skill {c.Skill:0.00} · delegated quality {q:0%} · {c.AvailableHours:0.#}h", T.Body);
            }

            GUILayout.Space(6);
            GUILayout.Label("DEPTH DIAL", T.Section);
            bool del = GUILayout.Toggle(room.RoutineDelegated, room.RoutineDelegated ? "Routine: DELEGATED (glance & run)" : "Routine: RECLAIMED (full ritual)", T.Toggle, GUILayout.Height(30));
            if (del != room.RoutineDelegated) _pending = () => room.RoutineDelegated = del;

            GUILayout.Space(6);
            GUILayout.Label("ASSISTS (surfacing only — never the sim)", T.Section);
            bool assist = GUILayout.Toggle(game.assistsEnabled, game.assistsEnabled ? "Assists ON" : "Assists OFF", T.Toggle, GUILayout.Height(28));
            if (assist != game.assistsEnabled) _pending = () => game.SetAssists(assist);

            GUILayout.Space(8);
            if (room.RoutineDelegated)
            {
                if (!room.ForecastReadToday)
                    GUILayout.Label($"<color=#{Hex(T.Clay)}>You haven't read the forecast today.</color>", T.Body);
                Row(("RUN TODAY (delegated)", () => game.SkipRoutineDays(1)),
                    ("SKIP quiet days →", () => game.SkipRoutineDays()));
            }
            else
            {
                GUILayout.Label("Reclaimed — schedule the day on the calendar, then Resolve there.", T.Dim);
            }
        }

        // =================== NOAA FORECAST ===================
        private void DrawForecast()
        {
            GUILayout.Label("Reading the weather is a deliberate act. Bands widen further out — the gap is the gamble (§7).", T.Dim);
            GUILayout.Space(4);
            if (game.Forecast == null) { GUILayout.Label("No forecast service.", T.Dim); return; }
            foreach (var d in game.Forecast.Upcoming(Day))
            {
                GUILayout.BeginHorizontal(T.Panel);
                string warn = (d.PredictedHeatSpike ? $"<color=#{Hex(T.Clay)}>HEAT</color> " : "")
                            + (d.PredictedStorm ? "<color=#9CC0E8>STORM</color> " : "")
                            + (d.PredictedFrost ? "<color=#BFE0EC>FROST</color> " : "");
                GUILayout.Label($"<b>+{d.DaysOut}d</b>", T.GoldText, GUILayout.Width(70));
                GUILayout.Label($"{d.Predicted.TmaxF:0}/{d.Predicted.TminF:0}°F ±{d.TempBandF:0}", T.Body, GUILayout.Width(180));
                GUILayout.Label($"rain {d.Predicted.RainMm:0}±{d.RainBandMm:0}mm", T.Body, GUILayout.Width(180));
                GUILayout.Label($"hum {d.Predicted.Humidity:0%}  conf {d.Confidence:0%}", T.Dim, GUILayout.Width(180));
                GUILayout.Label(string.IsNullOrEmpty(warn) ? "" : warn, T.Body);
                GUILayout.EndHorizontal();
            }
        }

        // =================== SOIL CLIPBOARD ===================
        private void DrawClipboard()
        {
            GUILayout.Label("Soil-test results you've earned (R on a green out on the course). Untested greens stay blank.", T.Dim);
            GUILayout.Space(4);
            _scrollLog = GUILayout.BeginScrollView(_scrollLog, GUILayout.ExpandHeight(true));
            foreach (var g in game.Course.Greens)
            {
                var obs = game.Legibility.Observe(g, Day);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{g.Id}</b>", T.Body, GUILayout.Width(120));
                if (obs.SoilTested)
                    GUILayout.Label($"N {obs.RevealedNitrogenPct:0.#} · K {obs.RevealedPotassiumPct:0.#} · Fe {obs.RevealedIronPct:0.#} · OM {obs.RevealedOrganicMatterPct:0.#}%", T.Body);
                else
                    GUILayout.Label("— not tested —", T.Dim);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // =================== FERT / SPRAY LOG ===================
        private void DrawFertLog()
        {
            GUILayout.Label("Spray residual protection (in days) per green, and the recent maintenance record.", T.Dim);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            foreach (var g in game.Course.Greens)
            {
                GUILayout.BeginVertical(GUILayout.Width(110));
                GUILayout.Label(Short(g.Id), T.Dim);
                GUILayout.Label(g.SprayResidualDaysLeft > 0 ? $"<color=#{Hex(T.Sage)}>{g.SprayResidualDaysLeft}d</color>" : "—", T.Body);
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("RECORD", T.Section);
            _scrollLog = GUILayout.BeginScrollView(_scrollLog, GUILayout.ExpandHeight(true));
            var log = game.RecentLog;
            for (int i = log.Count - 1; i >= 0; i--) GUILayout.Label(log[i], T.Dim);
            GUILayout.EndScrollView();
        }

        // ---- tells → colour (mirrors SurfaceRenderer; honest, gated) ----
        private Color ZoneTellColor(ZoneState zone)
        {
            if (zone.Type == ZoneType.Bunker)
            {
                float q = Mathf.Clamp01((float)(zone.SandQualityPct / 100.0));
                if (zone.WashedOut) q *= 0.5f;
                return Color.Lerp(new Color(0.45f, 0.38f, 0.28f), new Color(0.86f, 0.78f, 0.58f), q);
            }
            var obs = game.Legibility.Observe(zone, Day);
            int idx = 0; double worst = -1;
            for (int i = 0; i < obs.Tells.Length; i++)
            {
                double sev = obs.Tells[i].Lesions + obs.Tells[i].Thinning;
                if (sev > worst) { worst = sev; idx = i; }
            }
            var t = obs.Tells.Length > 0 ? obs.Tells[idx] : default;
            Color c = new Color((float)t.BaseColor.R, (float)t.BaseColor.G, (float)t.BaseColor.B, 1f);
            c = Color.Lerp(c, new Color(0.55f, 0.60f, 0.58f), (float)t.WiltTint * 0.6f);
            c = Color.Lerp(c, new Color(0.72f, 0.64f, 0.40f), (float)t.Lesions * 0.85f);
            c = Color.Lerp(c, new Color(0.34f, 0.26f, 0.18f), (float)t.Thinning * 0.7f);
            return c;
        }

        private Color BunkerColor(int hole)
        {
            // worst (lowest-quality / washed) bunker on the hole
            Color worst = new Color(0.2f, 0.2f, 0.2f); double worstQ = 2;
            for (int b = 1; b <= 3; b++)
            {
                var z = game.Course.Get($"bunker-{hole:00}-{b}");
                if (z == null) continue;
                double q = z.WashedOut ? 0 : z.SandQualityPct;
                if (q < worstQ) { worstQ = q; worst = ZoneTellColor(z); }
            }
            return worst;
        }

        // ---- small IMGUI helpers ----
        private static readonly ZoneType[] Surfaces = { ZoneType.Tee, ZoneType.Fairway, ZoneType.Rough, ZoneType.Green };
        private static string SurfaceShort(ZoneType t) => t switch
        { ZoneType.Tee => "tee", ZoneType.Fairway => "fwy", ZoneType.Rough => "rgh", ZoneType.Green => "grn", _ => t.ToString() };
        private static string ZoneId(ZoneType t, int hole) => t switch
        {
            ZoneType.Green => $"green-{hole:00}", ZoneType.Approach => $"approach-{hole:00}",
            ZoneType.Tee => $"tee-{hole:00}", ZoneType.Fairway => $"fairway-{hole:00}",
            ZoneType.Rough => $"rough-{hole:00}", _ => $"green-{hole:00}",
        };
        private int HoleCount()
        {
            int n = 0; foreach (var g in game.Course.Greens) n++; return n;
        }

        private Texture2D White
        {
            get { if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); } return _white; }
        }

        private bool Chip(Rect r, Color c, bool selected)
        {
            var prev = GUI.color;
            GUI.color = selected ? T.Gold : new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, White);
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), White);
            GUI.color = prev;
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private void Row(params (string label, System.Action act)[] buttons)
        {
            GUILayout.BeginHorizontal();
            foreach (var b in buttons) if (GUILayout.Button(b.label, T.Button)) _pending = b.act;
            GUILayout.EndHorizontal();
        }

        private void ForEachGreen(System.Func<ZoneState, TaskOrder> make)
        { foreach (var g in game.Course.Greens) AddTask(make(g)); }

        private void AddTask(TaskOrder t)
        {
            // From the room, work is queued as delegated by default; flip per-task on the calendar.
            t.Delegated = true;
            if (game.Crew.Count > 0) t.AssignedCrewId = game.Crew[0].Id;
            if (!game.Window.TryAssign(t)) _rejectFlashUntil = Time.realtimeSinceStartup + 1.5f;
        }

        private static string Short(string id) => string.IsNullOrEmpty(id) ? "(all)" : id;
        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
