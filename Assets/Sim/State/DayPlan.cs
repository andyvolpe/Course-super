// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.State
{
    /// <summary>How a fertilizer is carried to the plant (a real program decision).</summary>
    public enum FertMethod
    {
        /// <summary>Soil-applied prills. Uptake is GATED by soil temp, moisture and root depth.</summary>
        Granular,
        /// <summary>Leaf-applied spray. Fast, small, leaf-absorbed; barely touches the soil.</summary>
        Foliar,
    }

    /// <summary>The source/release rate of the nitrogen (a real program decision).</summary>
    public enum FertRelease
    {
        /// <summary>Quick-release soluble — fast green-up, big spike, high burn + leach risk.</summary>
        Quick,
        /// <summary>Slow-release coated — steady, low burn, low leach.</summary>
        Slow,
        /// <summary>Organic — gentlest, steady, feeds the soil.</summary>
        Organic,
    }

    /// <summary>
    /// One fertility application (Part B — N as a PROGRAM). N/K/Fe amounts plus the two carrier
    /// decisions (method + release). Inactive by default so plans that never set it are unaffected.
    /// </summary>
    public struct FertApplication
    {
        public bool Active;
        public double N;   // nitrogen units in this application
        public double K;   // potassium units (stress tolerance)
        public double Fe;  // iron units (colour without growth)
        public FertMethod Method;
        public FertRelease Release;

        public static FertApplication GranularQuick(double n, double k = 0, double fe = 0) =>
            new FertApplication { Active = true, N = n, K = k, Fe = fe, Method = FertMethod.Granular, Release = FertRelease.Quick };
        public static FertApplication GranularSlow(double n, double k = 0, double fe = 0) =>
            new FertApplication { Active = true, N = n, K = k, Fe = fe, Method = FertMethod.Granular, Release = FertRelease.Slow };
        public static FertApplication FoliarSpoon(double n, double k = 0, double fe = 0) =>
            new FertApplication { Active = true, N = n, K = k, Fe = fe, Method = FertMethod.Foliar, Release = FertRelease.Quick };
        public static FertApplication IronOnly(double fe) =>
            new FertApplication { Active = true, Fe = fe, Method = FertMethod.Foliar, Release = FertRelease.Quick };
    }

    /// <summary>The maintenance actions chosen for one zone on one day (TDD §3 step 6 / §4.5).</summary>
    public struct ZoneAction
    {
        public bool Mow;
        public double MowHeightIn;   // height of cut to set when mowing
        public bool Roll;
        public bool Spray;           // fungicide application
        public double IrrigationMm;  // water applied
        public double FertilizerN;   // nitrogen applied (legacy simple path — full uptake)
        public FertApplication Fert; // full fertility program (Part B); Active=false => ignored
        public bool Aerate;
        public bool Rake;            // rake bunkers (clears storm washout)

        /// <summary>
        /// Delegation quality in (0,1] scaling the BENEFICIAL part of this action (Phase 4.2 / §4.8).
        /// Convention: a value &lt;= 0 means "unspecified" and is treated as 1.0 (an expert hand), so
        /// plans that never set it are unaffected. Use <see cref="EffectiveQuality"/> to read it.
        /// </summary>
        public double Quality;

        public double EffectiveQuality => Quality <= 0.0 ? 1.0 : (Quality > 1.0 ? 1.0 : Quality);

        public static ZoneAction None => new ZoneAction { MowHeightIn = 0.125, Quality = 1.0 };
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
