// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Math
{
    public struct WaterResult
    {
        public double Et0Mm;
        public double EtVwcLoss;
        public double DrainageVwc;
    }

    /// <summary>
    /// Daily soil-water balance (TDD §4.1). Inputs (rain + irrigation) minus reference ET
    /// (Hargreaves) minus one-directional drainage. Contains the two MANDATORY safety guards:
    ///   • drainage uses max(0, moisture - FC)        — water never drains "up"
    ///   • ET uses sqrt(max(0, Tmax - Tmin))          — never sqrt of a negative
    /// </summary>
    public static class WaterBalance
    {
        public static WaterResult Apply(ZoneState z, WeatherDay w, double irrigationMm, AgronomyTuning t)
        {
            double fc = t.FieldCapacity(z.Soil, z.OrganicMatterPct);
            double wilt = t.WiltPoint(z.Soil);
            double residual = t.ResidualMoisturePct;

            // --- Reference ET (Hargreaves), with the mandatory sqrt guard ---
            double tempRangeGuard = Mathx.SafeSqrt(w.TmaxF - w.TminF); // sqrt(max(0, Tmax-Tmin))
            double et0 = t.HargreavesC * w.SolarRa * (w.TmeanF + t.HargreavesOffset) * tempRangeGuard;
            et0 = Mathx.Max0(et0);

            double m = z.SoilMoisturePct + (w.RainMm + irrigationMm) * t.MmToVwcPct;

            // --- Drainage: only the excess above field capacity drains, guarded max(0, m-FC) ---
            double drainage = t.DrainFraction(z.Soil) * Mathx.Max0(m - fc);
            m -= drainage;

            // --- Evapotranspiration, tapering to zero as the soil dries (can't evaporate absent water) ---
            double availability = Mathx.InverseLerp(residual, fc, m); // 0 at residual, 1 at FC+
            double etPotentialVwc = et0 * t.CropCoefficient(z.Type) * t.MmToVwcPct * availability;
            double etLoss = System.Math.Min(Mathx.Max0(m - residual), Mathx.Max0(etPotentialVwc));
            m -= etLoss;

            z.SoilMoisturePct = Mathx.Clamp(m, residual, t.SaturationPct);

            return new WaterResult { Et0Mm = et0, EtVwcLoss = etLoss, DrainageVwc = drainage };
        }

        public static bool IsDroughtStressed(ZoneState z, AgronomyTuning t)
            => z.SoilMoisturePct < t.WiltPoint(z.Soil);
    }
}
