// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// The fertility PROGRAM (Part B). Turns a <see cref="FertApplication"/> into N/K/Fe pool changes,
    /// honouring the four MVP decisions:
    ///   • SOURCE / RELEASE RATE — quick-release soluble shock-feeds (high burn); slow/organic are gentle.
    ///   • FOLIAR vs GRANULAR    — foliar is fast, small, leaf-absorbed; granular uptake is GATED by
    ///                              soil temp + moisture + root depth (hot/dry/shallow = poor uptake / burn).
    ///   • POTASSIUM (K)         — a separate pool feeding stress tolerance (consumed in Growth, not here).
    ///   • IRON (Fe)             — colour only; lands in its own fast-fading pool, never touches growth.
    /// Applied at pipeline step 2 (before growth/disease) so it shapes the SAME day's outcome.
    /// </summary>
    public static class FertilitySystem
    {
        public static void Apply(ZoneState z, FertApplication fert, AgronomyTuning t)
        {
            if (!fert.Active || z.Type == ZoneType.Bunker) return;

            // --- Iron: pure colour. Straight into the fast-fading Fe pool, no growth/disease coupling. ---
            if (fert.Fe > 0.0)
                z.IronPct = Mathx.Clamp(z.IronPct + fert.Fe, 0.0, 100.0);

            double nAvail;     // N that actually reaches the available pool
            double kAvail;     // K that reaches the pool
            double burn = 0.0; // density loss (fertilizer burn)

            if (fert.Method == FertMethod.Foliar)
            {
                // Leaf-absorbed: full uptake up to a soft cap, then diminishing returns (spoon-feed to win).
                double upTo = System.Math.Min(fert.N, t.FoliarDoseSoftCap);
                double over = Mathx.Max0(fert.N - t.FoliarDoseSoftCap);
                nAvail = upTo * t.FoliarUptakeFraction + over * t.FoliarOverdoseAbsorb;
                kAvail = fert.K * t.FoliarKFraction;
                // Only an OVERDOSE foliar dump burns the leaf; a small foliar feed is the safe path.
                burn += over * t.FoliarOverdoseBurnPerPt;
            }
            else // Granular: soil uptake gated by soil temp, moisture and root depth.
            {
                double uptake = GranularUptake(z, t);  // 0..1
                nAvail = fert.N * uptake;
                kAvail = fert.K * Mathx.Lerp(0.4, t.GranularKFractionFull, uptake);

                // The fraction NOT taken up sits as salt: it leaches and, on hot/dry soil, BURNS.
                double unused = Mathx.Max0(fert.N - nAvail);
                double dryness = 1.0 - Mathx.InverseLerp(t.GranularUptakeMoistureMinPct,
                                                         t.GranularUptakeMoistureFullPct, z.SoilMoisturePct);
                burn += unused * t.GranularDryBurnPerPt * Mathx.Clamp01(dryness);

                // Release rate: quick-release soluble shock-burns the available N; slow/organic barely do.
                double releaseMult = fert.Release == FertRelease.Quick ? 1.0 : t.SlowReleaseBurnMult;
                burn += nAvail * t.QuickReleaseBurnPerPt * releaseMult;
            }

            z.NitrogenPct = Mathx.Clamp(z.NitrogenPct + nAvail, 0.0, 100.0);
            z.PotassiumPct = Mathx.Clamp(z.PotassiumPct + kAvail, 0.0, 100.0);
            if (burn > 0.0)
                z.DensityPct = Mathx.Clamp(z.DensityPct - burn, 0.0, 100.0);
        }

        /// <summary>Granular soil-uptake efficiency (0..1): poor when soil is cold/hot, dry, or roots are shallow.</summary>
        public static double GranularUptake(ZoneState z, AgronomyTuning t)
        {
            double tempF = Mathx.Clamp01(Mathx.Bell(z.SoilTempF, t.GranularUptakeTempCenterF, t.GranularUptakeTempHalfWidthF));
            double moistF = Mathx.Clamp01(Mathx.InverseLerp(t.GranularUptakeMoistureMinPct, t.GranularUptakeMoistureFullPct, z.SoilMoisturePct));
            double rootF = Mathx.Clamp01(z.RootDepthIn / t.GranularUptakeRootFullIn);
            return tempF * moistF * rootF;
        }
    }
}
