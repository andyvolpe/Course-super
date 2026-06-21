// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Math
{
    /// <summary>Organic matter (thatch) and grain accrual (TDD §4.7). Both clamped to [0,100].</summary>
    public static class OrganicMatter
    {
        public static void Apply(ZoneState z, AgronomyTuning t, GrassProfile grass)
        {
            if (z.Type == ZoneType.Bunker) return;

            // Growth feeds thatch; microbial decomposition removes a little each day. Over-feeding
            // ACCELERATES thatch directly on top of the (already surged) clip volume — overN = 0 in-band.
            double overN = Mathx.Max0(z.NitrogenPct - grass.NOptMax);
            z.OrganicMatterPct = Mathx.Clamp(
                z.OrganicMatterPct + z.ClipVolume * t.OmFromGrowth + overN * t.OverNOmPerPt - t.OmDecomposition,
                0.0, 100.0);

            // Grain grows with the canopy (mowing knocks it down — handled in MaintenanceSystem).
            z.GrainPct = Mathx.Clamp(z.GrainPct + z.ClipVolume * t.GrainFromGrowth, 0.0, 100.0);
        }
    }
}
