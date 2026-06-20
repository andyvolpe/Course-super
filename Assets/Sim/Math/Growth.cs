// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Math
{
    /// <summary>
    /// Growth, clip volume, carbohydrate reserves and nitrogen dynamics (TDD §4.7).
    /// Growth is clamped >= 0; reserves, density and nitrogen are clamped to [0,100] each tick.
    /// </summary>
    public static class Growth
    {
        public static void Apply(ZoneState z, WeatherDay w, double gddToday, double drainageVwc,
                                 AgronomyTuning t, GrassProfile grass)
        {
            if (z.Type == ZoneType.Bunker) { z.ClipVolume = 0; return; }

            double fc = t.FieldCapacity(z.Soil, z.OrganicMatterPct);
            double wilt = t.WiltPoint(z.Soil);

            double tempFactor = Mathx.Bell(w.TmeanF, t.GrowthTempCenterF, t.GrowthTempHalfWidthF);
            double moistureFactor = Mathx.InverseLerp(wilt, fc, z.SoilMoisturePct);
            double nFactor = Mathx.Clamp01(z.NitrogenPct / t.NitrogenOptimum);

            double potential = gddToday * t.GrowthPerGdd * grass.RecuperativeRate;
            double growth = Mathx.Max0(potential * tempFactor * moistureFactor * nFactor);

            // Clip yield scales with how much canopy there is to cut.
            z.ClipVolume = growth * (z.DensityPct / 100.0) * t.ClipPerGrowth;

            // Carbohydrate reserves: photosynthesis credits, respiration + growth debits.
            double photo = t.PhotosynthesisMax * tempFactor * (z.DensityPct / 100.0);
            double heatBurn = t.HeatRespirationPerDegOverF * Mathx.Max0(w.TmeanF - t.GrowthTempCenterF);
            double respiration = t.RespirationBase + heatBurn;
            z.CarbReservesPct = Mathx.Clamp(
                z.CarbReservesPct + photo - respiration - growth * t.CarbCostPerGrowth, 0.0, 100.0);

            // Density: growth recovers it; baseline wear erodes it (disease/debt subtract elsewhere).
            double densGain = growth * t.DensityGainPerGrowth * grass.RecuperativeRate;
            z.DensityPct = Mathx.Clamp(z.DensityPct + densGain - t.DensityNaturalWear, 0.0, 100.0);

            // Nitrogen: uptake by growth + leaching with drainage.
            double uptake = growth * t.NitrogenUptakePerGrowth;
            double leach = drainageVwc * t.NitrogenLeachPerDrainage;
            z.NitrogenPct = Mathx.Clamp(z.NitrogenPct - uptake - leach, 0.0, 100.0);
        }
    }
}
