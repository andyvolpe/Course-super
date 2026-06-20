// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Deterministic weather generator (TDD §3 step 1). Weather for a given (seed, dayIndex) is fixed,
    /// so replays match. Models a temperate cool-season climate: warm humid summers (dollar-spot season),
    /// cool wet shoulders, cold winters.
    /// </summary>
    public sealed class WeatherSystem
    {
        private readonly int _seed;

        public WeatherSystem(int seed) { _seed = seed; }

        public WeatherDay Generate(int dayIndex)
        {
            // Independent, reproducible stream per day.
            var rng = new Rng((ulong)unchecked((long)_seed * 1000003L + dayIndex + 1));

            int dayOfYear = ((dayIndex % GameClock.DaysPerYear) + GameClock.DaysPerYear) % GameClock.DaysPerYear;
            double yearPhase = 2.0 * System.Math.PI * (dayOfYear - 45) / GameClock.DaysPerYear;
            double seasonal = System.Math.Sin(yearPhase); // -1 winter .. +1 summer

            double meanF = 55.0 + 22.0 * seasonal + rng.Range(-5.0, 5.0);
            double diurnal = 14.0 + 8.0 * Mathx.Clamp01(0.5 + 0.5 * seasonal) + rng.Range(0.0, 6.0);
            double tmax = meanF + diurnal * 0.5;
            double tmin = meanF - diurnal * 0.5;

            // Extraterrestrial radiation (mm/day equivalent for Hargreaves), always positive.
            // Tuned so summer reference ET lands around 6-7 mm/day (temperate), not desert levels.
            double ra = 4.5 + 2.5 * seasonal;

            // Rain: more frequent in the shoulders; humid summer storms.
            double rainProb = 0.30 + 0.10 * (1.0 - System.Math.Abs(seasonal));
            double rainMm = 0.0;
            if (rng.Chance(rainProb))
            {
                // Exponential-ish depths.
                rainMm = -System.Math.Log(1.0 - rng.NextDouble() * 0.999) * 6.0;
            }

            // Relative humidity: humid transition-zone summers (~60-90%), drier in cold/clear spells.
            double humidity = Mathx.Clamp(0.55 + 0.20 * seasonal + rng.Range(-0.10, 0.10)
                                          + (rainMm > 0 ? 0.10 : 0.0), 0.25, 0.95);

            // Leaf wetness: overnight dew (more in warm humid weather) plus any rain.
            double dewHrs = 2.0 + 8.0 * humidity + rng.Range(0.0, 2.0);
            double wetnessFromRain = rainMm > 0 ? 6.0 : 0.0;
            double leafWetnessHrs = Mathx.Clamp(dewHrs + wetnessFromRain, 0.0, 24.0);

            return new WeatherDay
            {
                TminF = tmin,
                TmaxF = tmax,
                RainMm = Mathx.Max0(rainMm),
                LeafWetnessHrs = leafWetnessHrs,
                SolarRa = Mathx.Max0(ra),
                Humidity = humidity,
            };
        }
    }
}
