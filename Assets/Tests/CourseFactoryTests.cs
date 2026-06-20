// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using System.Linq;
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 2.1 — MVP course generation + healthy-start invariants.</summary>
    [TestFixture]
    public class CourseFactoryTests
    {
        [Test]
        public void Mvp_Has18Greens_18Tees_18Fairways_40Bunkers()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 42);
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Green));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Tee));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Fairway));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Rough));
            Assert.AreEqual(40, course.Zones.Count(z => z.Type == ZoneType.Bunker));
        }

        [Test]
        public void Greens_AreSubZonedInto9Cells()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 42);
            foreach (var g in course.Greens)
                Assert.AreEqual(9, g.Cells.Length, $"{g.Id} should have a 3x3 sub-cell grid");
        }

        [Test]
        public void Greens_StartInHealthyRanges()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 42);
            foreach (var g in course.Greens)
            {
                Assert.That(g.SoilMoisturePct, Is.InRange(CourseFactory.HealthyMoistureMin, CourseFactory.HealthyMoistureMax), $"{g.Id} moisture");
                Assert.That(g.DensityPct, Is.InRange(CourseFactory.HealthyDensityMin, CourseFactory.HealthyDensityMax), $"{g.Id} density");
                Assert.That(g.OrganicMatterPct, Is.InRange(CourseFactory.HealthyOmMin, CourseFactory.HealthyOmMax), $"{g.Id} OM");
                Assert.That(g.TurfDebtPct, Is.InRange(CourseFactory.HealthyDebtMin, CourseFactory.HealthyDebtMax), $"{g.Id} debt");
                Assert.AreEqual(CourseFactory.HealthyRootDepthIn, g.RootDepthIn, 1e-9, $"{g.Id} root depth");
                Assert.That(g.Stimp, Is.InRange(6.0, 15.0), $"{g.Id} stimp clamp");
                // No disease at day zero.
                Assert.AreEqual(0.0, g.MaxInfection, 1e-9, $"{g.Id} should start disease-free");
            }
        }
    }
}
