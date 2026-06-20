// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Math
{
    /// <summary>Organic matter (thatch) and grain accrual (TDD §4.7). Both clamped to [0,100].</summary>
    public static class OrganicMatter
    {
        public static void Apply(ZoneState z, AgronomyTuning t)
        {
            if (z.Type == ZoneType.Bunker) return;

            // Growth feeds thatch; microbial decomposition removes a little each day.
            z.OrganicMatterPct = Mathx.Clamp(
                z.OrganicMatterPct + z.ClipVolume * t.OmFromGrowth - t.OmDecomposition, 0.0, 100.0);

            // Grain grows with the canopy (mowing knocks it down — handled in MaintenanceSystem).
            z.GrainPct = Mathx.Clamp(z.GrainPct + z.ClipVolume * t.GrainFromGrowth, 0.0, 100.0);
        }
    }
}
