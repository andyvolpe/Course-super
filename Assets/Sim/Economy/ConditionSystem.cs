// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Economy
{
    /// <summary>
    /// Scores overall course playing condition (0..100) from the maintained state of EVERY surface.
    /// This is the bridge from agronomy to economy: a healthy, fast, clean course scores high (draws
    /// golfers); a diseased, thin, debt-laden one scores low (sheds them).
    ///
    /// The roll-up is weighted by each surface's visibility/playability (greens heaviest, then
    /// approaches/fairways, then tees/rough, bunkers lightest), so neglecting the fairways or rough —
    /// not just the greens — now drags demand down.
    /// </summary>
    public static class ConditionSystem
    {
        /// <summary>Condition of a single green (0..100) — density, health, debt, SPEED and firmness.</summary>
        public static double GreenCondition(ZoneState g, EconomyConfig cfg)
        {
            double density = Mathx.Clamp01(g.DensityPct / 100.0);
            double health = Mathx.Clamp01(1.0 - g.MaxInfection / 100.0);
            double debt = Mathx.Clamp01(1.0 - g.TurfDebtPct / 100.0);
            double speed = 1.0 - Mathx.Clamp01(System.Math.Abs(g.Stimp - cfg.StimpTarget) / cfg.StimpTolerance);
            double firm = Mathx.Clamp01(g.FirmnessPct / 100.0);

            double score = cfg.WDensity * density + cfg.WHealth * health + cfg.WDebt * debt
                         + cfg.WSpeed * speed + cfg.WFirm * firm;
            return Mathx.Clamp(score * 100.0, 0.0, 100.0);
        }

        /// <summary>Condition of any one surface (0..100). Greens use green speed; other turf surfaces
        /// drop the speed term (Stimp is meaningless off the green); bunkers score on sand consistency.</summary>
        public static double SurfaceCondition(ZoneState z, EconomyConfig cfg)
        {
            if (z.Type == ZoneType.Green) return GreenCondition(z, cfg);

            if (z.Type == ZoneType.Bunker)
            {
                double q = Mathx.Clamp01(z.SandQualityPct / 100.0);
                if (z.WashedOut) q *= 0.4; // a washed-out bunker plays terribly until raked
                return Mathx.Clamp(q * 100.0, 0.0, 100.0);
            }

            // Other turf (approach/fairway/tee/rough): density, health, debt, firmness — no speed term.
            double density = Mathx.Clamp01(z.DensityPct / 100.0);
            double health = Mathx.Clamp01(1.0 - z.MaxInfection / 100.0);
            double debt = Mathx.Clamp01(1.0 - z.TurfDebtPct / 100.0);
            double firm = Mathx.Clamp01(z.FirmnessPct / 100.0);
            double wSum = cfg.WDensity + cfg.WHealth + cfg.WDebt + cfg.WFirm; // speed weight redistributed out
            double score = (cfg.WDensity * density + cfg.WHealth * health + cfg.WDebt * debt + cfg.WFirm * firm) / wSum;
            return Mathx.Clamp(score * 100.0, 0.0, 100.0);
        }

        /// <summary>
        /// Course condition (0..100): the per-surface conditions aggregated, weighted by each surface's
        /// ConditionWeight. A greens-only course reduces to the mean green condition (the weights cancel).
        /// </summary>
        public static double CourseCondition(CourseState course, EconomyConfig cfg)
        {
            double weighted = 0, totalWeight = 0;
            foreach (var z in course.Zones)
            {
                double w = z.Surface != null ? z.Surface.ConditionWeight : 1.0;
                if (w <= 0) continue;
                weighted += w * SurfaceCondition(z, cfg);
                totalWeight += w;
            }
            return totalWeight <= 0 ? 0 : weighted / totalWeight;
        }
    }
}
