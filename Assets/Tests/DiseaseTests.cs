// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 2.3 — disease behaviour: mismanagement gets greens sick, good practice does not.</summary>
    [TestFixture]
    public class DiseaseTests
    {
        // Start in summer (dollar-spot weather) and run a 90-day season.
        private const int SummerStart = 90;
        private const int SeasonDays = 90;

        [Test]
        public void MismanagedGreen_GetsSick_WhileWellKeptStaysClean()
        {
            int seed = 555;
            var bad = new SimHarness(seed, PlanLibrary.Bad, CourseConfig.GreensOnly(), SummerStart).Run(SeasonDays);
            var good = new SimHarness(seed, PlanLibrary.Good, CourseConfig.GreensOnly(), SummerStart).Run(SeasonDays);

            double badInfection = bad.MeanGreenInfection();
            double goodInfection = good.MeanGreenInfection();

            Assert.Greater(badInfection, 25.0, "a mismanaged green should reach high infection within a season");
            Assert.Less(goodInfection, 5.0, "a well-kept green should stay near zero infection");
            Assert.Greater(badInfection, goodInfection * 2.0, "clear separation between bad and good management");
        }

        /// <summary>
        /// Regression guard for the Phase 3.0 death-spiral fix: a well-managed green must SURVIVE a
        /// full season (it must not die of drought/debt), and mismanagement must visibly thin the
        /// canopy relative to it.
        /// </summary>
        [Test]
        public void GoodManagement_KeepsGreenAlive_AndBadManagementThinsIt()
        {
            const int seeds = 20;
            double sumGoodDensity = 0, sumBadDensity = 0;
            for (int s = 0; s < seeds; s++)
            {
                var good = new SimHarness(s, PlanLibrary.Good, CourseConfig.GreensOnly(), SummerStart).Run(SeasonDays);
                var bad = new SimHarness(s, PlanLibrary.Bad, CourseConfig.GreensOnly(), SummerStart).Run(SeasonDays);

                double goodDensity = AvgDensity(good);
                double badDensity = AvgDensity(bad);
                Assert.Greater(goodDensity, 60.0, $"seed {s}: a well-kept green must stay alive (density {goodDensity:F1})");
                sumGoodDensity += goodDensity;
                sumBadDensity += badDensity;
            }
            double meanGood = sumGoodDensity / seeds;
            double meanBad = sumBadDensity / seeds;
            Assert.Greater(meanGood, meanBad + 20.0, $"mismanagement must visibly thin turf (good {meanGood:F1} vs bad {meanBad:F1})");
        }

        private static double AvgDensity(SimHarness h)
        {
            double sum = 0; int n = 0;
            foreach (var z in h.Course.Greens) { sum += z.DensityPct; n++; }
            return n == 0 ? 0 : sum / n;
        }
    }
}
