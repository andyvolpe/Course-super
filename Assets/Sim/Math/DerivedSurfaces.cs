// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Math
{
    /// <summary>
    /// Derived playing surfaces: firmness and green speed (Stimp), recomputed from underlying
    /// state each day (TDD §4.4). Stimp is clamped to [6,15] feet. Also decays the transient
    /// roll bonus once per day.
    /// </summary>
    public static class DerivedSurfaces
    {
        public static void Recompute(ZoneState z, AgronomyTuning t)
        {
            double wilt = t.WiltPoint(z.Soil);
            double moistureNorm = Mathx.InverseLerp(wilt, t.SaturationPct, z.SoilMoisturePct);
            double omNorm = Mathx.Clamp01(z.OrganicMatterPct / 100.0);

            z.FirmnessPct = Mathx.Clamp(
                t.FirmnessBase - t.FirmnessMoistureWeight * moistureNorm - t.FirmnessOmWeight * omNorm,
                0.0, 100.0);

            double densityNorm = Mathx.Clamp01(z.DensityPct / 100.0);
            double grainNorm = Mathx.Clamp01(z.GrainPct / 100.0);
            double debtNorm = Mathx.Clamp01(z.TurfDebtPct / 100.0);
            double heightFactor = Mathx.InverseLerp(0.08, 0.30, z.GrassHeightIn); // ACTUAL length: unmown = slower

            double stimp = t.StimpBase
                + t.StimpDensityBonus * densityNorm
                - t.StimpMoisturePenalty * moistureNorm
                - t.StimpGrainPenalty * grainNorm
                - t.StimpDebtPenalty * debtNorm
                - heightFactor * 2.0
                + z.RollBonus;

            z.Stimp = Mathx.Clamp(stimp, t.StimpMin, t.StimpMax);

            // Transient roll bonus fades.
            z.RollBonus *= (1.0 - t.RollDecay);
            if (z.RollBonus < 1e-4) z.RollBonus = 0.0;
        }
    }
}
