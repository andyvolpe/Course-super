// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>Per-day shared state handed to the systems while resolving a day.</summary>
    public sealed class ResolveContext
    {
        public int DayIndex;
        public WeatherDay Weather;
        public Rng Rng;
        public AgronomyTuning Tuning;
        public GrassProfile Grass;
        public DayResult Result;

        /// <summary>
        /// TEETH SWITCH for T4. When true the fairness gate is bypassed and ungated expressions are
        /// allowed to fire. The keystone fairness test flips this on to prove the audit DETECTS
        /// illegible symptoms. In normal play this is ALWAYS false.
        /// </summary>
        public bool BypassFairnessGate;
    }
}
