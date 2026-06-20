// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Turf debt (TDD §4.5): the single mismanagement accumulator. Offenses add to it; carbohydrate
    /// reserves only SCALE the accrual rate (low reserves amplify every offence). A clean day pays
    /// debt down. Past the bleed threshold (~45) debt erodes density — visible thinning — before it
    /// can drive collapse.
    /// </summary>
    public static class TurfDebtSystem
    {
        public static void Apply(ZoneState z, ZoneAction action, ResolveContext ctx)
        {
            if (z.Type == ZoneType.Bunker) return;
            var t = ctx.Tuning;

            double offenses = 0.0;

            // Scalping: mown below the grass's safe height.
            if (action.Mow && action.MowHeightIn < ctx.Grass.MinSafeMowHeightIn)
                offenses += t.OffenseScalp;

            // Mechanical traffic on saturated turf.
            if ((action.Mow || action.Roll) && z.SoilMoisturePct > t.WetTrafficMoisturePct)
                offenses += t.OffenseWetTraffic;

            // Drought stress.
            if (z.SoilMoisturePct < t.WiltPoint(z.Soil))
                offenses += t.OffenseDrought;

            // Chronic over-watering.
            if (z.SoilMoisturePct > t.OverwaterMoisturePct)
                offenses += t.OffenseOverwater;

            // Nitrogen starvation.
            if (z.NitrogenPct < t.NStarvationPct)
                offenses += t.OffenseNStarvation;

            // Thatch left uncored.
            if (z.OrganicMatterPct > t.HighOmPct && z.DaysSinceAeration > t.AerationOverdueDays)
                offenses += t.OffenseSkippedAeration;

            // Active disease with no spray cover.
            if (z.MaxInfection > 0.0 && z.SprayResidualDaysLeft <= 0)
                offenses += t.OffenseDiseaseUntreated;

            // Reserves scale accrual: depleted turf can't absorb abuse.
            double reserveAmp = 1.0 + t.DebtReserveAmplification * (1.0 - z.CarbReservesPct / 100.0);

            if (offenses > 0.0)
                z.TurfDebtPct += offenses * reserveAmp;
            else
                z.TurfDebtPct -= t.DebtRecoveryPerDay;

            z.TurfDebtPct = Mathx.Clamp(z.TurfDebtPct, 0.0, 100.0);

            // Bleed density once debt is past the threshold.
            if (z.TurfDebtPct > t.DebtBleedThreshold)
            {
                double bleed = (z.TurfDebtPct - t.DebtBleedThreshold) * t.DebtDensityBleed;
                z.DensityPct = Mathx.Clamp(z.DensityPct - bleed, 0.0, 100.0);
            }
        }
    }
}
