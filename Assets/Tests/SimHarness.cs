// Greenkeeper.Tests — references Greenkeeper.Sim + NUnit only (no UnityEngine), so the same files
// run in the Unity Test Runner (EditMode) and headless via `dotnet test`.
using System;
using System.Collections.Generic;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>
    /// Test driver (Test Spec): builds a CourseState from config, runs N days with a scripted
    /// per-day plan function and seeded weather, and returns final state + an event log + a per-day
    /// legibility-tell log.
    /// </summary>
    public sealed class SimHarness
    {
        public readonly GameDirector Director;
        public CourseState Course => Director.Course;
        public readonly List<DayResult> History = new List<DayResult>();

        private readonly Func<int, CourseState, DayPlan> _planFn;
        public readonly AgronomyTuning Tuning;

        public SimHarness(int seed,
                          Func<int, CourseState, DayPlan> planFn = null,
                          CourseConfig cfg = null,
                          int startDay = 0,
                          bool bypassFairnessGate = false)
        {
            cfg = cfg ?? CourseConfig.Mvp();
            Tuning = cfg.Tuning;
            var course = CourseFactory.Build(cfg, seed);
            Director = new GameDirector(course, seed, cfg.Tuning, cfg.Grass)
            {
                BypassFairnessGate = bypassFairnessGate
            };
            Director.Clock.JumpTo(startDay);
            _planFn = planFn;
        }

        public SimHarness Run(int days)
        {
            for (int i = 0; i < days; i++)
            {
                DayPlan plan = _planFn != null ? _planFn(Director.Clock.DayIndex, Course) : new DayPlan();
                History.Add(Director.ResolveDay(plan ?? new DayPlan()));
            }
            return this;
        }

        /// <summary>Flattened numeric snapshot of the whole course — used for deep-equality (T1).</summary>
        public List<double> FinalStateValues()
        {
            var values = new List<double>();
            Course.CollectStateValues(values);
            return values;
        }

        public IEnumerable<ExpressionEvent> AllExpressions()
        {
            foreach (var day in History)
                foreach (var e in day.Expressions)
                    yield return e;
        }

        /// <summary>Mean visible expression severity across all green sub-cells at end of run.</summary>
        public double MeanGreenExpressionSeverity()
        {
            double sum = 0; int count = 0;
            foreach (var z in Course.Greens)
                foreach (var c in z.Cells) { sum += c.ExpressionSeverity; count++; }
            return count == 0 ? 0 : sum / count;
        }

        /// <summary>Mean active infection across all green sub-cells at end of run.</summary>
        public double MeanGreenInfection()
        {
            double sum = 0; int count = 0;
            foreach (var z in Course.Greens)
                foreach (var c in z.Cells) { sum += c.Infection; count++; }
            return count == 0 ? 0 : sum / count;
        }

        /// <summary>
        /// Was a readable tell present for <paramref name="zoneId"/> on any day in the inclusive
        /// window [dayIndex-3, dayIndex]? History index == DayIndex when the run starts at day 0.
        /// </summary>
        public bool TellInPrior3Days(string zoneId, int dayIndex)
        {
            for (int d = dayIndex; d >= 0 && d >= dayIndex - 3; d--)
            {
                int idx = HistoryIndexForDay(d);
                if (idx < 0) continue;
                if (History[idx].TellByZone.TryGetValue(zoneId, out var tell) && tell) return true;
            }
            return false;
        }

        private int HistoryIndexForDay(int dayIndex)
        {
            if (History.Count == 0) return -1;
            int startDay = History[0].DayIndex;
            int idx = dayIndex - startDay;
            return (idx >= 0 && idx < History.Count) ? idx : -1;
        }
    }
}
