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

            // Per-surface recuperation (green = 1.0, rough slower) instances the ONE growth model.
            double surfaceRecup = z.Surface != null ? z.Surface.GrowthRecuperation : 1.0;
            double trafficWear = z.Surface != null ? z.Surface.TrafficWearPerDay : 0.0;

            double tempFactor = Mathx.Bell(w.TmeanF, t.GrowthTempCenterF, t.GrowthTempHalfWidthF);
            double moistureFactor = Mathx.InverseLerp(wilt, fc, z.SoilMoisturePct);
            double nFactor = Mathx.Clamp01(z.NitrogenPct / t.NitrogenOptimum);

            double potential = gddToday * t.GrowthPerGdd * grass.RecuperativeRate * surfaceRecup;
            double growth = Mathx.Max0(potential * tempFactor * moistureFactor * nFactor);

            // Clip yield scales with how much canopy there is to cut.
            z.ClipVolume = growth * (z.DensityPct / 100.0) * t.ClipPerGrowth;

            // Carbohydrate reserves: photosynthesis credits, respiration + growth debits.
            double photo = t.PhotosynthesisMax * tempFactor * (z.DensityPct / 100.0);
            double heatBurn = t.HeatRespirationPerDegOverF * Mathx.Max0(w.TmeanF - t.GrowthTempCenterF);
            double respiration = t.RespirationBase + heatBurn;
            z.CarbReservesPct = Mathx.Clamp(
                z.CarbReservesPct + photo - respiration - growth * t.CarbCostPerGrowth, 0.0, 100.0);

            // Density: growth recovers it; baseline + per-surface TRAFFIC wear erode it (tees/divots
            // highest, rough lowest). Disease/debt subtract elsewhere.
            double densGain = growth * t.DensityGainPerGrowth * grass.RecuperativeRate * surfaceRecup;
            z.DensityPct = Mathx.Clamp(z.DensityPct + densGain - t.DensityNaturalWear - trafficWear, 0.0, 100.0);

            // Nitrogen: uptake by growth + leaching with drainage. Potassium is consumed by growth too
            // (so a push that isn't matched by K feeding drifts toward the fragile high-N/low-K state).
            double uptake = growth * t.NitrogenUptakePerGrowth;
            double leach = drainageVwc * t.NitrogenLeachPerDrainage;
            z.NitrogenPct = Mathx.Clamp(z.NitrogenPct - uptake - leach, 0.0, 100.0);

            // Iron is colour only and fades fast — decay it here (Growth runs daily for living turf).
            if (z.IronPct > 0.0) z.IronPct = Mathx.Max0(z.IronPct - t.IronDecayPerDay);

            // ---- OVER-N consequences (Part A). overN = 0 in-band, so the model below is dormant
            //      until the turf is over-fed; every term keys off it. ----
            double overN = Mathx.Max0(z.NitrogenPct - grass.NOptMax);
            if (overN > 0.0)
            {
                // Lush top growth SURGES clippings (mow hours / scalp risk)...
                z.ClipVolume += overN * t.OverNClipSurgePerPt * (z.DensityPct / 100.0);
                // ...SPENDS carbohydrate reserves to fuel it...
                z.CarbReservesPct = Mathx.Clamp(z.CarbReservesPct - overN * t.OverNCarbDrainPerPt, 0.0, 100.0);
                // ...and SHRINKS the roots (shallow, weak, heat-stress collapse waiting to happen).
                z.RootDepthIn = Mathx.Max0(z.RootDepthIn - overN * t.OverNRootShrinkPerPt);
            }
            else if (z.RootDepthIn < t.RootDepthBaselineIn && z.CarbReservesPct > 30.0)
            {
                // Fed correctly and with reserves to spare, roots recover slowly toward baseline.
                z.RootDepthIn = System.Math.Min(t.RootDepthBaselineIn, z.RootDepthIn + t.RootRecoveryPerDay);
            }

            // ---- Heat/wear FRAGILITY from N:K imbalance + over-N lushness (Part B / K). Zero when K
            //      keeps pace with N and N is in-band, so default turf is untouched. ----
            double fragility = Mathx.Max0(z.NitrogenPct - z.PotassiumPct) * t.NKImbalanceWeight
                             + overN * t.OverNFragilityWeight;
            double heatOver = Mathx.Max0(w.TmeanF - t.HeatStressThresholdF);
            if (fragility > 0.0)
            {
                double stressLoss = fragility * (heatOver * t.FragilityHeatLossPerDegPerPt + t.FragilityWearLossPerPt);
                z.DensityPct = Mathx.Max0(z.DensityPct - stressLoss);
            }

            // Shallow roots can't reach water/cool under heat — the over-N "weak roots" debt comes due as
            // heat-stress collapse. rootDeficit = 0 at baseline depth, so default turf is untouched.
            double rootDeficit = Mathx.Clamp01((t.RootDepthBaselineIn - z.RootDepthIn) / t.RootDepthBaselineIn);
            if (rootDeficit > 0.0 && heatOver > 0.0)
                z.DensityPct = Mathx.Max0(z.DensityPct - rootDeficit * heatOver * t.RootHeatLossPerDeg);
        }
    }
}
