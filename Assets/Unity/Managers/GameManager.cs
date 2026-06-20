using System.Collections.Generic;
using UnityEngine;
using Greenkeeper.Sim.Config;
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

        /// <summary>Recent per-step log lines for the debug UI.</summary>
        public readonly List<string> RecentLog = new List<string>();
        public const int MaxLogLines = 24;

        private void Awake() => NewGame();

        public void NewGame()
        {
            CourseConfig cfg = courseConfig != null ? courseConfig.ToConfig() : CourseConfig.Mvp();
            var course = CourseFactory.Build(cfg, weatherSeed);
            Director = new GameDirector(course, weatherSeed, cfg.Tuning, cfg.Grass);
            Legibility = new LegibilitySystem(cfg.Tuning,
                assistsEnabled ? DifficultySettings.WithAssists() : DifficultySettings.Full());
            RecentLog.Clear();
            Log($"New game. {course.Zones.Count} zones, seed {weatherSeed}. Assists {(assistsEnabled ? "ON" : "OFF")}.");
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

        /// <summary>Skip resolving days until an interrupt is raised (GDD §1).</summary>
        public int SkipToNextInterrupt(int maxDays = 365)
        {
            int start = Director.Clock.DayIndex;
            // Interrupt source stub: stop when any green shows a readable tell-driven expression today.
            int resolved = Director.SkipUntil(
                stop: _ => false,
                planProvider: _ => BuildMorningPlan(),
                maxDays: maxDays);
            Log($"Skipped {resolved} day(s) ({start} -> {Director.Clock.DayIndex}).");
            return resolved;
        }

        /// <summary>The current morning plan. Empty for now; the player UI will populate this in Phase 3+.</summary>
        private DayPlan BuildMorningPlan() => new DayPlan();

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
