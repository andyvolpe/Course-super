// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Economy
{
    /// <summary>
    /// Scores overall course playing condition (0..100) from the greens' maintained state. This is the
    /// bridge from agronomy to economy: a healthy, fast, clean course scores high (draws golfers); a
    /// diseased, thin, debt-laden one scores low (sheds them).
    /// </summary>
    public static class ConditionSystem
    {
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

        /// <summary>Mean condition over all greens (0..100).</summary>
        public static double CourseCondition(CourseState course, EconomyConfig cfg)
        {
            double sum = 0; int n = 0;
            foreach (var g in course.Greens) { sum += GreenCondition(g, cfg); n++; }
            return n == 0 ? 0 : sum / n;
        }
    }
}
