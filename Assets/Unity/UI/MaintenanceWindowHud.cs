using UnityEngine;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Phase 4.3 — the morning window screen: "here's what the crew will handle, here's what I'm
    /// taking today." Shows the crew-hour budget and remaining hours as you assign; over-assignment is
    /// blocked with a clear "not enough hours" state; each task toggles delegate-to-staff vs do-it-
    /// yourself and picks a crew member; "Resolve Window" runs the day. This is the real action
    /// interface (the Phase 3.0 debug readout stays available behind a toggle).
    ///
    /// SETUP: add next to GameManager. Assign the debug panel ref to keep it behind a toggle.
    /// </summary>
    public sealed class MaintenanceWindowHud : MonoBehaviour
    {
        public GameManager game;
        public GreenDebugPanel debugPanel;   // kept behind a toggle
        public bool showDebug;

        private Vector2 _scroll;
        private float _rejectFlashUntil;

        private void Awake()
        {
            if (game == null) game = FindObjectOfType<GameManager>();
            if (debugPanel == null) debugPanel = FindObjectOfType<GreenDebugPanel>();
        }

        private void Update()
        {
            if (debugPanel != null) debugPanel.enabled = showDebug;
        }

        private void OnGUI()
        {
            if (game == null || game.Window == null || game.Course == null) return;
            var w = game.Window;

            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20), GUI.skin.box);
            GUILayout.Label($"<b>MORNING WINDOW</b>  —  day {game.Director.Clock.DayIndex} / {game.Director.Clock.Season}", Rich());

            // --- Budget bar ---
            float frac = w.BudgetHours > 0 ? (float)(w.UsedHours / w.BudgetHours) : 0f;
            GUILayout.Label($"Crew hours: {w.UsedHours:F1} / {w.BudgetHours:F0}   (remaining {w.RemainingHours:F1})");
            GUILayout.Label(BudgetBar(frac), Rich());
            if (Time.realtimeSinceStartup < _rejectFlashUntil)
                GUILayout.Label("<color=#ff6060>NOT ENOUGH HOURS — cut something or delegate less</color>", Rich());

            // --- Add tasks ---
            GUILayout.Space(4);
            GUILayout.Label("<b>Add tasks</b>", Rich());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Walk-mow greens")) AddPerGreen(z => TaskCatalog.WalkMow(z.Id));
            if (GUILayout.Button("Triplex greens")) Add(TaskCatalog.Triplex());
            if (GUILayout.Button("Roll greens")) AddPerGreen(z => TaskCatalog.Roll(z.Id));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Spray greens")) AddPerGreen(z => TaskCatalog.Spray(z.Id));
            if (GUILayout.Button("Water greens")) AddPerGreen(z => TaskCatalog.Water(z.Id));
            if (GUILayout.Button("Fertilize greens")) AddPerGreen(z => TaskCatalog.Fertilize(z.Id));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Aerate greens")) AddPerGreen(z => TaskCatalog.Aerate(z.Id));
            if (GUILayout.Button("Mow fairways")) AddPerType(ZoneType.Fairway, z => TaskCatalog.MowFairway(z.Id));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rake bunkers")) Add(TaskCatalog.RakeBunkers());
            if (GUILayout.Button("Change cups")) Add(TaskCatalog.ChangeCups());
            if (GUILayout.Button("Clear")) game.BeginWindow();
            GUILayout.EndHorizontal();

            // --- Queue ---
            GUILayout.Space(4);
            GUILayout.Label($"<b>Queue</b> ({w.Accepted.Count} tasks)", Rich());
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(220));
            foreach (var task in w.Accepted)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{task.Type} {Short(task.ZoneId)} {task.HoursCost:F1}h", GUILayout.Width(220));
                bool diy = !task.Delegated;
                bool newDiy = GUILayout.Toggle(diy, diy ? "DO IT MYSELF" : "delegate", GUILayout.Width(110));
                task.Delegated = !newDiy;
                if (task.Delegated)
                {
                    string crewName = CrewName(task.AssignedCrewId);
                    if (GUILayout.Button(crewName, GUILayout.Width(90))) CycleCrew(task);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (w.CouldNotFitEverything)
                GUILayout.Label($"<color=#ffd0d0>{w.Rejected.Count} task(s) didn't fit the budget.</color>", Rich());

            // --- Resolve / debug ---
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RESOLVE WINDOW")) game.ResolveWindow();
            if (GUILayout.Button("SKIP routine days")) game.SkipRoutineDays();
            showDebug = GUILayout.Toggle(showDebug, "debug readout");
            GUILayout.EndHorizontal();

            if (game.Director.InterruptRaised)
                GUILayout.Label("<color=#ff6060><b>INTERRUPT — a crisis pulled you back in. Attend the window.</b></color>", Rich());

            GUILayout.Space(4);
            GUILayout.Label("Log:");
            for (int i = game.RecentLog.Count - 1; i >= 0 && i >= game.RecentLog.Count - 8; i--)
                GUILayout.Label(game.RecentLog[i]);

            GUILayout.EndArea();
        }

        private void Add(TaskOrder t)
        {
            t.Delegated = true;
            if (game.Crew.Count > 0) t.AssignedCrewId = game.Crew[0].Id;
            if (!game.Window.TryAssign(t)) _rejectFlashUntil = Time.realtimeSinceStartup + 1.5f;
        }

        private void AddPerGreen(System.Func<ZoneState, TaskOrder> make)
        {
            foreach (var z in game.Course.Greens) Add(make(z));
        }

        private void AddPerType(ZoneType type, System.Func<ZoneState, TaskOrder> make)
        {
            foreach (var z in game.Course.Zones) if (z.Type == type) Add(make(z));
        }

        private void CycleCrew(TaskOrder task)
        {
            var crew = game.Crew;
            int idx = 0;
            for (int i = 0; i < crew.Count; i++) if (crew[i].Id == task.AssignedCrewId) { idx = i; break; }
            task.AssignedCrewId = crew[(idx + 1) % crew.Count].Id;
        }

        private string CrewName(string id)
        {
            foreach (var c in game.Crew) if (c.Id == id) return c.Name;
            return "staff";
        }

        private static string Short(string zoneId) => string.IsNullOrEmpty(zoneId) ? "(all)" : zoneId;

        private static string BudgetBar(float frac)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(frac * 24f), 0, 24);
            string col = frac >= 1f ? "#ff6060" : (frac > 0.85f ? "#ffd060" : "#80ff80");
            return $"[<color={col}>{new string('#', n)}</color>{new string('.', 24 - n)}]";
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
