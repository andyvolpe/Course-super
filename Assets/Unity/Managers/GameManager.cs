using System.Collections.Generic;
using UnityEngine;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Unity.Config;
using Greenkeeper.Unity.Save;

namespace Greenkeeper.Unity.Managers
{
    /// <summary>
    /// The Unity-side owner of the headless sim (TDD §9). Builds a CourseState from an authored
    /// CourseConfigSO, owns the GameDirector, and exposes the morning-plan → resolve → skip loop to
    /// the UI. All simulation lives in Greenkeeper.Sim; this is just the bridge.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Authoring")]
        public CourseConfigSO courseConfig;
        public int weatherSeed = 12345;

        [Header("Difficulty (surfacing only — never the sim)")]
        public bool assistsEnabled;

        public GameDirector Director { get; private set; }
        public CourseState Course => Director?.Course;

        /// <summary>The single legibility gate (GDD §3). The Unity layer renders only what this permits.</summary>
        public LegibilitySystem Legibility { get; private set; }

        /// <summary>The crew and the current morning maintenance window (GDD §4).</summary>
        public List<CrewMember> Crew { get; private set; }
        public MaintenanceWindow Window { get; private set; }

        /// <summary>The fallible weather forecast the player plans against (Phase 5).</summary>
        public Forecast Forecast { get; private set; }

        /// <summary>Course finances (Phase 6): condition -> demand -> revenue, minus costs.</summary>
        public EconomyState Economy => Director?.Economy;

        /// <summary>Recent per-step log lines for the debug UI.</summary>
        public readonly List<string> RecentLog = new List<string>();
        public const int MaxLogLines = 24;

        private void Awake() => NewGame();

        public void NewGame()
        {
            CourseConfig cfg = courseConfig != null ? courseConfig.ToConfig() : CourseConfig.Mvp();
            var course = CourseFactory.Build(cfg, weatherSeed);
            var econCfg = EconomyConfig.Default;
            Director = new GameDirector(course, weatherSeed, cfg.Tuning, cfg.Grass)
            {
                Economy = new EconomyState(econCfg),
                EconomyConfig = econCfg,
            };
            Legibility = new LegibilitySystem(cfg.Tuning,
                assistsEnabled ? DifficultySettings.WithAssists() : DifficultySettings.Full());
            Crew = CrewMember.DefaultCrew();
            Forecast = new Forecast(weatherSeed, cfg.Tuning);
            BeginWindow();
            RecentLog.Clear();
            Log($"New game. {course.Zones.Count} zones, seed {weatherSeed}. Assists {(assistsEnabled ? "ON" : "OFF")}.");
        }

        /// <summary>Open a fresh morning window for the current day (clears the queue, full hour budget).</summary>
        public void BeginWindow() => Window = new MaintenanceWindow(Crew);

        /// <summary>
        /// Resolve today's window: accepted tasks become the plan, delegated tasks apply at staff
        /// quality (the depth dial), then the day advances and a fresh window opens. Returns the result.
        /// </summary>
        public DayResult ResolveWindow()
        {
            var plan = Window.ToDayPlan(Course, Delegation.Resolver(Crew));
            var result = Director.ResolveDay(plan);
            Log($"D{result.DayIndex}: ran window — {Window.Accepted.Count} tasks, " +
                $"{Window.UsedHours:F1}/{Window.BudgetHours:F0}h used" +
                (Window.CouldNotFitEverything ? $", {Window.Rejected.Count} cut for hours" : ""));
            foreach (var line in result.Log) Log($"  {line}");
            BeginWindow();
            return result;
        }

        /// <summary>Toggle the assist layer. Changes only what's surfaced — the sim run is identical.</summary>
        public void SetAssists(bool on)
        {
            assistsEnabled = on;
            if (Legibility != null)
                Legibility.Settings = on ? DifficultySettings.WithAssists() : DifficultySettings.Full();
            Log($"Assists {(on ? "ON" : "OFF")} (surfacing only).");
        }

        /// <summary>Resolve one day with the current morning plan (no-op until the player UI sets actions).</summary>
        public void ResolveDay()
        {
            var result = Director.ResolveDay(BuildMorningPlan());
            foreach (var line in result.Log) Log($"D{result.DayIndex}: {line}");
        }

        /// <summary>
        /// Fidelity scaling (Phase 4.4 / GDD §1): fly through routine days on the delegated auto-program,
        /// and STOP the moment an interrupt (heat spike / fresh disease break) fires — which yanks the
        /// player into a full morning window for that day. Returns days skipped.
        /// </summary>
        public int SkipRoutineDays(int maxDays = 365)
        {
            int start = Director.Clock.DayIndex;
            Director.ClearInterrupt();
            int resolved = Director.SkipUntil(
                stop: _ => false,
                planProvider: day => AutoRoutinePlan(),
                maxDays: maxDays);

            if (Director.InterruptRaised)
            {
                // Re-open the window so the player attends the crisis day at full attention.
                BeginWindow();
                Log($"Skipped {resolved} day(s) ({start} -> {Director.Clock.DayIndex}); INTERRUPT — into the window.");
            }
            else
            {
                Log($"Skipped {resolved} day(s) ({start} -> {Director.Clock.DayIndex}).");
            }
            return resolved;
        }

        /// <summary>The crew's standard delegated program for a routine day (staff quality applies).</summary>
        private DayPlan AutoRoutinePlan()
        {
            var w = new Crew.MaintenanceWindow(Crew);
            foreach (var z in Course.Greens)
            {
                var mow = Crew.TaskCatalog.WalkMow(z.Id); mow.Delegated = true; mow.AssignedCrewId = Crew[0].Id; w.TryAssign(mow);
                var water = Crew.TaskCatalog.Water(z.Id); water.Delegated = true; water.AssignedCrewId = Crew[0].Id; w.TryAssign(water);
            }
            // A scheduled (calendar) spray — the delegated routine, blind to the live tell by design.
            if (Director.Clock.DayIndex % 14 == 0)
                foreach (var z in Course.Greens)
                {
                    var spray = Crew.TaskCatalog.Spray(z.Id); spray.Delegated = true; spray.AssignedCrewId = Crew[1 % Crew.Count].Id; w.TryAssign(spray);
                }
            return w.ToDayPlan(Course, Crew.Delegation.Resolver(Crew));
        }

        public void SaveGame()
        {
            var data = SaveData.Capture(Course, weatherSeed, Director.Clock.DayIndex);
            SaveSystem.Save(data);
            Log($"Saved (schema v{data.SchemaVersion}) to {SaveSystem.DefaultPath}");
        }

        private void Log(string line)
        {
            RecentLog.Add(line);
            if (RecentLog.Count > MaxLogLines) RecentLog.RemoveAt(0);
            Debug.Log($"[Greenkeeper] {line}");
        }
    }
}
