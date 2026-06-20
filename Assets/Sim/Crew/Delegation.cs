// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Math;

namespace Greenkeeper.Sim.Crew
{
    /// <summary>
    /// The depth dial (GDD §4.2, TDD §4.8). A task can be delegated to staff or kept hands-on; the
    /// applied quality scales the task's beneficial deltas. Staff quality is hard-capped at 0.95 —
    /// staff NEVER match an expert hand (1.0). The gap shrinks with skill but never closes.
    /// </summary>
    public static class Delegation
    {
        public const double PlayerQuality = 1.0;
        public const double StaffQualityCeiling = 0.95;

        /// <summary>staffQuality = clamp(0.55 + 0.4*skill + 0.15*knowledge, 0, 0.95).</summary>
        public static double StaffQuality(double skill, double knowledge)
            => Mathx.Clamp(0.55 + 0.4 * skill + 0.15 * knowledge, 0.0, StaffQualityCeiling);

        public static double StaffQuality(CrewMember crew)
            => crew == null ? StaffQuality(0.5, 0.5) : StaffQuality(crew.Skill, crew.Knowledge);

        /// <summary>
        /// Builds the per-task quality resolver for a window: hands-on tasks apply at 1.0, delegated
        /// tasks at the assigned crew member's staff quality (or a default if unassigned).
        /// </summary>
        public static System.Func<TaskOrder, double> Resolver(IEnumerable<CrewMember> crew)
        {
            var byId = new Dictionary<string, CrewMember>();
            if (crew != null) foreach (var c in crew) byId[c.Id] = c;
            return task =>
            {
                if (!task.Delegated) return PlayerQuality;
                CrewMember c = null;
                if (!string.IsNullOrEmpty(task.AssignedCrewId)) byId.TryGetValue(task.AssignedCrewId, out c);
                return StaffQuality(c);
            };
        }
    }
}
