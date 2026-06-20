// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.State
{
    /// <summary>The maintenance actions chosen for one zone on one day (TDD §3 step 6 / §4.5).</summary>
    public struct ZoneAction
    {
        public bool Mow;
        public double MowHeightIn;   // height of cut to set when mowing
        public bool Roll;
        public bool Spray;           // fungicide application
        public double IrrigationMm;  // water applied
        public double FertilizerN;   // nitrogen applied
        public bool Aerate;

        public static ZoneAction None => new ZoneAction { MowHeightIn = 0.125 };
    }

    /// <summary>
    /// The morning plan for a whole course-day: per-zone actions with a fallback default.
    /// The sim is plan-driven and deterministic — given a plan and seed the outcome is fixed.
    /// </summary>
    public sealed class DayPlan
    {
        public ZoneAction Default = ZoneAction.None;
        public readonly Dictionary<string, ZoneAction> ByZone = new Dictionary<string, ZoneAction>();

        public ZoneAction For(string zoneId)
            => ByZone.TryGetValue(zoneId, out var a) ? a : Default;

        public DayPlan Set(string zoneId, ZoneAction action)
        {
            ByZone[zoneId] = action;
            return this;
        }
    }
}
