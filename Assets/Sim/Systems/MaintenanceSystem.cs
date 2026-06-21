// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Maintenance actions (TDD §3 step 6 / §4.5). Nutrient/water inputs are applied early (so they
    /// affect the same day's growth and disease); mechanical actions are applied after disease
    /// accrual (a spray cleans up today's pressure and sets residual protection going forward).
    /// </summary>
    public static class MaintenanceSystem
    {
        /// <summary>
        /// Applied before growth/disease: fertility raises the available nutrient pools. Two paths:
        /// the legacy simple <see cref="ZoneAction.FertilizerN"/> (full-uptake N), and the full
        /// fertility PROGRAM (<see cref="ZoneAction.Fert"/>) routed through <see cref="FertilitySystem"/>.
        /// </summary>
        public static void ApplyInputs(ZoneState z, ZoneAction action, AgronomyTuning t)
        {
            if (z.Type == ZoneType.Bunker) return;
            double q = action.EffectiveQuality;
            if (action.FertilizerN > 0.0)
                z.NitrogenPct = Mathx.Clamp(z.NitrogenPct + action.FertilizerN * q, 0.0, 100.0);
            if (action.Fert.Active)
                FertilitySystem.Apply(z, action.Fert, t);
        }

        /// <summary>
        /// Applied after disease: mow, roll, spray, aerate. The BENEFICIAL part of each effect scales
        /// with delegation quality (§4.8) — a delegated hand never gets the full benefit of an expert.
        /// </summary>
        public static void ApplyMechanical(ZoneState z, ZoneAction action, AgronomyTuning t)
        {
            if (z.Type == ZoneType.Bunker)
            {
                if (action.Rake) z.WashedOut = false; // raking restores a washed-out bunker
                return;
            }
            double q = action.EffectiveQuality;

            if (action.Mow)
            {
                z.MowHeightIn = action.MowHeightIn;
                z.GrainPct = Mathx.Max0(z.GrainPct - t.GrainMowReduction * q); // cleaner cut = more grain knocked down
                z.DensityPct = Mathx.Max0(z.DensityPct - t.MowDensityWear);    // wear is not a benefit; unscaled
                z.ClipVolume = 0.0; // harvested
            }

            if (action.Roll)
                z.RollBonus += t.RollStimpBonus * q;

            if (action.Spray)
            {
                double knockP = t.SprayPressureKnockdown * q;
                double knockI = t.SprayInfectionKnockdown * q;
                for (int i = 0; i < z.Cells.Length; i++)
                {
                    z.Cells[i].Pressure *= (1.0 - knockP);
                    z.Cells[i].Infection *= (1.0 - knockI);
                }
                // Lower-quality applications give shorter residual protection (min 1 day if sprayed).
                z.SprayResidualDaysLeft = System.Math.Max(1, (int)System.Math.Round(t.SprayResidualDays * q));
            }

            if (action.Aerate)
            {
                z.OrganicMatterPct = Mathx.Max0(z.OrganicMatterPct - t.AerateOmRemoval * q);
                z.TurfDebtPct = Mathx.Max0(z.TurfDebtPct - t.AerateDebtRelief * q);
                z.DensityPct = Mathx.Max0(z.DensityPct - t.AerateDensityWear); // coring wear unscaled
                z.DaysSinceAeration = 0;
                z.AerationRecoveryDaysLeft = t.AerateRecoveryDays;
            }
        }

        /// <summary>End-of-day bookkeeping: residual/recovery timers and aeration age.</summary>
        public static void TickCounters(ZoneState z, ZoneAction action)
        {
            if (z.SprayResidualDaysLeft > 0) z.SprayResidualDaysLeft--;
            if (z.AerationRecoveryDaysLeft > 0) z.AerationRecoveryDaysLeft--;
            if (!action.Aerate) z.DaysSinceAeration++;
        }
    }
}
