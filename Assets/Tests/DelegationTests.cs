// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 4.2 — delegation quality gap: scheduled+capped staff vs reactive expert hand.</summary>
    [TestFixture]
    public class DelegationTests
    {
        private const int SummerStart = 90;
        private const int SeasonDays = 90;

        [Test]
        public void StaffQuality_IsCappedBelowAnExpertHand()
        {
            Assert.AreEqual(0.95, Delegation.StaffQuality(skill: 1.0, knowledge: 1.0), 1e-9, "hard 0.95 ceiling");
            Assert.AreEqual(0.825, Delegation.StaffQuality(skill: 0.5, knowledge: 0.5), 1e-9);
            Assert.Less(Delegation.StaffQuality(1.0, 1.0), Delegation.PlayerQuality, "staff never match the player");
        }

        [Test]
        public void FullyDelegated_IsMeasurablyWorseThanHandsOn()
        {
            const int seeds = 30;
            double delInf = 0, handInf = 0, delDens = 0, handDens = 0;
            for (int s = 0; s < seeds; s++)
            {
                var hands = RunSeason(s, delegated: false, skill: 0.0, knowledge: 0.0);
                var del = RunSeason(s, delegated: true, skill: 0.55, knowledge: 0.45); // a typical hand
                handInf += hands.infection; handDens += hands.density;
                delInf += del.infection; delDens += del.density;
            }
            handInf /= seeds; delInf /= seeds; handDens /= seeds; delDens /= seeds;

            TestContext.WriteLine($"hands-on: infection {handInf:F2} density {handDens:F1} | delegated: infection {delInf:F2} density {delDens:F1}");
            Assert.Greater(delInf, handInf + 1.0, "delegated turf must carry measurably more disease (the gap)");
            Assert.LessOrEqual(delDens, handDens + 1e-6, "delegated turf is never in BETTER condition than hands-on");
        }

        [Test]
        public void Gap_ShrinksWithSkill_ButNeverCloses()
        {
            const int seeds = 30;
            double hands = 0, lowSkill = 0, highSkill = 0;
            for (int s = 0; s < seeds; s++)
            {
                hands += RunSeason(s, delegated: false, 0, 0).infection;
                lowSkill += RunSeason(s, delegated: true, skill: 0.25, knowledge: 0.20).infection;
                highSkill += RunSeason(s, delegated: true, skill: 1.00, knowledge: 1.00).infection; // capped at 0.95
            }
            hands /= seeds; lowSkill /= seeds; highSkill /= seeds;

            TestContext.WriteLine($"hands-on {hands:F2} | high-skill delegated {highSkill:F2} | low-skill delegated {lowSkill:F2}");
            Assert.Less(highSkill, lowSkill, "more skill -> smaller gap");
            Assert.Greater(highSkill, hands, "but even the best staff never match the hands-on result");
        }

        // Routine season on the greens; the ONLY differences are delegation quality and spray timing:
        // hands-on sprays REACTIVELY to a readable tell at full quality; delegated sprays on a fixed
        // 14-day SCHEDULE at capped staff quality (it cannot pre-empt a threat it isn't reading).
        private static (double infection, double density) RunSeason(int seed, bool delegated, double skill, double knowledge)
        {
            var cfg = CourseConfig.GreensOnly();
            cfg.Grass.DiseaseSusceptibility = 2.5; // a dollar-spot-prone cultivar: pressure is relentless
            var t = cfg.Tuning;
            var course = CourseFactory.Build(cfg, seed);
            var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
            dir.Clock.JumpTo(SummerStart);
            double q = delegated ? Delegation.StaffQuality(skill, knowledge) : Delegation.PlayerQuality;

            double infectionDaysSum = 0; int samples = 0;
            double minDensity = double.MaxValue;
            for (int d = 0; d < SeasonDays; d++)
            {
                int day = dir.Clock.DayIndex;
                var plan = new DayPlan();
                foreach (var z in course.Greens)
                {
                    var a = ZoneAction.None;
                    a.Mow = true; a.MowHeightIn = 0.125;
                    // A disease-pressured summer: the canopy is kept lush/wet (target above FC), so dollar
                    // spot is a live threat that must be actively managed — which is where spray timing and
                    // efficacy (i.e. delegation quality) actually bite.
                    // Lush/wet canopy (disease pressure) but kept fed so the green stays alive — the
                    // residual disease then tracks spray EFFICACY (quality) and TIMING, and the better
                    // hand always leaves less.
                    a.IrrigationMm = System.Math.Max(0, (24.0 - z.SoilMoisturePct) / 0.9);
                    a.FertilizerN = z.NitrogenPct < 35.0 ? 12.0 : 0.0;
                    a.Quality = q;
                    a.Spray = delegated
                        ? (day % 14 == 0)                                  // generic calendar program, blind to the tell
                        : (z.MaxInfection > 0.0 || z.MeanExpression > 0.0  // reactive to a readable tell
                           || z.MeanPressure > t.TellPressureThreshold);
                    plan.Set(z.Id, a);
                }
                dir.ResolveDay(plan);

                // Season-long disease BURDEN (not just the endpoint, which a recent spray would mask).
                foreach (var z in course.Greens)
                {
                    infectionDaysSum += z.MaxInfection; samples++;
                    if (z.DensityPct < minDensity) minDensity = z.DensityPct;
                }
            }

            return (samples == 0 ? 0 : infectionDaysSum / samples, minDensity);
        }
    }
}
