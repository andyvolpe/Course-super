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
    }
}
