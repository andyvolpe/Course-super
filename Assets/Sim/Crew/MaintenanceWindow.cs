// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System;
using System.Collections.Generic;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Crew
{
    /// <summary>
    /// The morning maintenance window (GDD §4): a day's task queue against a fixed crew-hour budget
    /// (the sum of crew availability). Assigning a task consumes hours; you CANNOT exceed the budget —
    /// over-assignment is rejected and flagged, which is exactly what forces the daily triage choice.
    /// </summary>
    public sealed class MaintenanceWindow
    {
        public readonly List<CrewMember> Crew;
        public readonly double BudgetHours;
        public readonly List<TaskOrder> Accepted = new List<TaskOrder>();
        public readonly List<TaskOrder> Rejected = new List<TaskOrder>();

        public double UsedHours { get; private set; }
        public double RemainingHours => BudgetHours - UsedHours;
        public bool CouldNotFitEverything => Rejected.Count > 0;

        public MaintenanceWindow(IEnumerable<CrewMember> crew)
        {
            Crew = new List<CrewMember>(crew);
            double budget = 0;
            foreach (var c in Crew) budget += c.AvailableHours;
            BudgetHours = budget;
        }

        /// <summary>
        /// Try to add a task. Returns true if it fits within the remaining hours (and consumes them),
        /// false if it would blow the budget (the task is recorded in <see cref="Rejected"/> instead).
        /// </summary>
        public bool TryAssign(TaskOrder task)
        {
            if (task.HoursCost <= RemainingHours + 1e-9)
            {
                Accepted.Add(task);
                UsedHours += task.HoursCost;
                return true;
            }
            Rejected.Add(task);
            return false;
        }

        public void Reset()
        {
            Accepted.Clear();
            Rejected.Clear();
            UsedHours = 0;
        }

        /// <summary>
        /// Convert the accepted tasks into a per-zone DayPlan the resolve pipeline consumes (effects
        /// land on TDD §3 step 6). <paramref name="qualityResolver"/> supplies each task's delegation
        /// quality (Phase 4.2); when null, everything is applied at expert quality 1.0.
        /// </summary>
        public DayPlan ToDayPlan(CourseState course, Func<TaskOrder, double> qualityResolver = null)
        {
            var byZone = new Dictionary<string, ZoneAction>();
            foreach (var task in Accepted)
            {
                double quality = qualityResolver != null ? qualityResolver(task) : 1.0;
                foreach (var zoneId in TargetZones(task, course))
                {
                    if (!byZone.TryGetValue(zoneId, out var a)) a = ZoneAction.None;
                    byZone[zoneId] = TaskCatalog.Fold(task, a, quality);
                }
            }

            var plan = new DayPlan();
            foreach (var kv in byZone) plan.Set(kv.Key, kv.Value);
            return plan;
        }

        private static IEnumerable<string> TargetZones(TaskOrder task, CourseState course)
        {
            if (!task.IsCourseWide) { yield return task.ZoneId; yield break; }

            ZoneType? filter = null;
            switch (task.Type)
            {
                case TaskType.TriplexMowGreens:
                case TaskType.RollGreens:
                case TaskType.ChangeCups:
                    filter = ZoneType.Green; break;
                case TaskType.MowFairways:
                    filter = ZoneType.Fairway; break;
                case TaskType.MowRough:
                    filter = ZoneType.Rough; break;
                case TaskType.MowTees:
                    filter = ZoneType.Tee; break;
                case TaskType.RakeBunkers:
                    filter = ZoneType.Bunker; break;
            }
            if (filter == null) yield break;
            foreach (var z in course.Zones)
                if (z.Type == filter.Value) yield return z.Id;
        }
    }
}
