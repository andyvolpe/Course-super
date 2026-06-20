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
        /// <summary>Applied before growth/disease: fertilizer raises available nitrogen.</summary>
        public static void ApplyInputs(ZoneState z, ZoneAction action, AgronomyTuning t)
        {
            if (z.Type == ZoneType.Bunker) return;
            if (action.FertilizerN > 0.0)
                z.NitrogenPct = Mathx.Clamp(z.NitrogenPct + action.FertilizerN, 0.0, 100.0);
        }

        /// <summary>Applied after disease: mow, roll, spray, aerate.</summary>
        public static void ApplyMechanical(ZoneState z, ZoneAction action, AgronomyTuning t)
        {
            if (z.Type == ZoneType.Bunker) return;

            if (action.Mow)
            {
                z.MowHeightIn = action.MowHeightIn;
                z.GrainPct = Mathx.Max0(z.GrainPct - t.GrainMowReduction);
                z.DensityPct = Mathx.Max0(z.DensityPct - t.MowDensityWear);
                z.ClipVolume = 0.0; // harvested
            }

            if (action.Roll)
                z.RollBonus += t.RollStimpBonus;

            if (action.Spray)
            {
                for (int i = 0; i < z.Cells.Length; i++)
                {
                    z.Cells[i].Pressure *= (1.0 - t.SprayPressureKnockdown);
                    z.Cells[i].Infection *= (1.0 - t.SprayInfectionKnockdown);
                }
                z.SprayResidualDaysLeft = t.SprayResidualDays;
            }

            if (action.Aerate)
            {
                z.OrganicMatterPct = Mathx.Max0(z.OrganicMatterPct - t.AerateOmRemoval);
                z.TurfDebtPct = Mathx.Max0(z.TurfDebtPct - t.AerateDebtRelief);
                z.DensityPct = Mathx.Max0(z.DensityPct - t.AerateDensityWear);
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
