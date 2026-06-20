// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Classifies a weather day into extreme events (Phase 5.3). Per-day events read straight from the
    /// values; flash drought needs a running count of hot, rainless days (tracked by the caller).
    /// Shared by the resolve pipeline (interrupts/effects) and the Forecast (predicting events).
    /// </summary>
    public static class WeatherEvents
    {
        public static bool IsHeatSpike(WeatherDay w, AgronomyTuning t) => w.TmaxF > t.HeatSpikeThresholdF;
        public static bool IsStorm(WeatherDay w, AgronomyTuning t) => w.RainMm > t.StormRainThresholdMm;
        public static bool IsFrost(WeatherDay w, AgronomyTuning t) => w.TminF < t.FrostThresholdF;

        /// <summary>A hot, effectively-rainless day — the building block of a flash drought.</summary>
        public static bool IsHotDry(WeatherDay w, AgronomyTuning t)
            => w.TmaxF >= t.FlashDroughtMinTmaxF && w.RainMm <= t.FlashDroughtMaxRainMm;
    }
}
