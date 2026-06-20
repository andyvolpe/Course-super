// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Math
{
    /// <summary>Soil temperature (lagged air temp) and growing-degree-day accrual (TDD §4.7).</summary>
    public static class SoilThermal
    {
        /// <returns>GDD accrued today.</returns>
        public static double Apply(ZoneState z, WeatherDay w, AgronomyTuning t)
        {
            // Soil temperature lags air mean.
            z.SoilTempF += t.SoilTempLag * (w.TmeanF - z.SoilTempF);

            // GDD with base, guarded non-negative.
            double gddToday = Mathx.Max0(w.TmeanF - t.GddBaseF);
            z.GddAccum = Mathx.Max0(z.GddAccum + gddToday);
            return gddToday;
        }
    }
}
