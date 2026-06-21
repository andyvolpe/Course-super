// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using System.Linq;
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.Physics;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>
    /// The ONE agronomy model instanced across ALL surfaces (per-surface profiles + resolution):
    /// every surface populates at its healthy ranges, course condition aggregates all surfaces, and
    /// the surface you maintain decides the lie a missed shot finds.
    /// </summary>
    [TestFixture]
    public class SurfaceTests
    {
        private static readonly EconomyConfig E = EconomyConfig.Default;
        private static readonly AgronomyTuning T = AgronomyTuning.Default;

        // ---- 1) Every surface populates at its profile's healthy ranges --------------------
        [Test]
        public void AllSurfaces_Populate_AtTheirHealthyRanges()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 42);

            // The MVP course has all six surfaces, at the right counts.
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Green));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Approach));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Tee));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Fairway));
            Assert.AreEqual(18, course.Zones.Count(z => z.Type == ZoneType.Rough));
            Assert.AreEqual(40, course.Zones.Count(z => z.Type == ZoneType.Bunker));

            foreach (var z in course.Zones)
            {
                var p = z.Surface;
                Assert.IsNotNull(p, $"{z.Id} must carry a surface profile");
                Assert.AreEqual(z.Type, p.Type, $"{z.Id} profile type");

                // Resolution: greens keep the 9-cell sub-grid; every other surface is a single zone.
                Assert.AreEqual(z.Type == ZoneType.Green ? 9 : 1, z.Cells.Length, $"{z.Id} resolution");

                if (p.IsTurf)
                {
                    Assert.That(z.SoilMoisturePct, Is.InRange(p.MoistureMin, p.MoistureMax), $"{z.Id} moisture");
                    Assert.That(z.DensityPct, Is.InRange(p.DensityMin, p.DensityMax), $"{z.Id} density");
                    Assert.That(z.OrganicMatterPct, Is.InRange(p.OmMin, p.OmMax), $"{z.Id} OM");
                    Assert.That(z.TurfDebtPct, Is.InRange(p.DebtMin, p.DebtMax), $"{z.Id} debt");
                    Assert.AreEqual(p.RootDepthIn, z.RootDepthIn, 1e-9, $"{z.Id} root depth");
                    Assert.AreEqual(p.MowHeightIn, z.MowHeightIn, 1e-9, $"{z.Id} mow height");
                    Assert.That(z.Stimp, Is.InRange(T.StimpMin, T.StimpMax), $"{z.Id} stimp clamp");
                    Assert.AreEqual(0.0, z.MaxInfection, 1e-9, $"{z.Id} disease-free start");
                }
                else // bunker
                {
                    Assert.AreEqual(0.0, z.DensityPct, 1e-9, $"{z.Id} sand has no turf density");
                    Assert.Greater(z.SandQualityPct, 0.0, $"{z.Id} starts with consistent sand");
                }
            }

            // Surfaces are genuinely DIFFERENT (not greens cloned everywhere): rough is mown high & lean,
            // greens tight & dense.
            var rough = course.Get("rough-01");
            var green = course.Get("green-01");
            Assert.Greater(rough.MowHeightIn, green.MowHeightIn, "rough is mown far higher than greens");
            Assert.Less(rough.DensityPct, green.DensityPct, "rough runs leaner than greens");
        }

        // ---- 2) Course condition responds to neglect on NON-green surfaces -----------------
        [Test]
        public void CourseCondition_Falls_WhenNonGreenSurfacesAreNeglected()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 7);
            double healthy = ConditionSystem.CourseCondition(course, E);

            // Leave the GREENS pristine; trash only the fairways, rough, tees, approaches and bunkers.
            foreach (var z in course.Zones)
            {
                if (z.Type == ZoneType.Green) continue;
                if (z.Type == ZoneType.Bunker) { z.SandQualityPct = 5; z.WashedOut = true; continue; }
                z.DensityPct = 18; z.TurfDebtPct = 88;
                foreach (var c in z.Cells) c.Infection = 85;
                DerivedSurfaces.Recompute(z, T);
            }
            double neglected = ConditionSystem.CourseCondition(course, E);

            TestContext.WriteLine($"condition healthy={healthy:F1}  non-green neglect={neglected:F1}");
            Assert.Less(neglected, healthy - 10.0,
                "neglecting fairways/rough/tees/bunkers (greens untouched) must visibly drag course condition down");

            // And the greens alone still read healthy — proving the drop came from the OTHER surfaces.
            double greensOnly = course.Greens.Average(g => ConditionSystem.GreenCondition(g, E));
            Assert.Greater(greensOnly, 70.0, "the greens themselves were never touched");
        }

        // ---- 3) Fairway firmness and rough density measurably change ball outcome ----------
        [Test]
        public void FairwayFirmness_ChangesRollOut()
        {
            var soft = CourseFactory.Build(CourseConfig.Mvp(), 1).Get("fairway-01");
            var firm = soft.Clone();
            soft.FirmnessPct = 25;   // wet, soft — holds
            firm.FirmnessPct = 90;   // baked, firm — runs

            double softRoll = BallPhysics.SolveLie(soft, T).RollOutFt;
            double firmRoll = BallPhysics.SolveLie(firm, T).RollOutFt;

            TestContext.WriteLine($"fairway roll-out: soft={softRoll:F1}ft firm={firmRoll:F1}ft");
            Assert.Greater(firmRoll, softRoll + 5.0, "a firm fairway must run the ball out farther than a soft one");
        }

        [Test]
        public void RoughDensity_ChangesLiePenalty()
        {
            var light = CourseFactory.Build(CourseConfig.Mvp(), 1).Get("rough-01");
            var heavy = light.Clone();
            light.DensityPct = 45;   // thin, wispy rough — playable
            heavy.DensityPct = 92;   // thick, lush rough — grabby

            var lightLie = BallPhysics.SolveLie(light, T);
            var heavyLie = BallPhysics.SolveLie(heavy, T);

            TestContext.WriteLine($"rough lie: light dist={lightLie.DistanceFactor:F2} ({lightLie.Quality}) | " +
                                  $"heavy dist={heavyLie.DistanceFactor:F2} ({heavyLie.Quality})");
            Assert.Less(heavyLie.DistanceFactor, lightLie.DistanceFactor - 0.1,
                "thicker rough must cost more distance — the miss is penalised by the rough you grew");
            Assert.IsTrue(heavyLie.Buried, "very thick rough buries the ball");
        }

        [Test]
        public void BunkerCondition_ChangesLie_AndRakingRestoresIt()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 3);
            var clean = course.Get("bunker-01-1");
            var washed = clean.Clone();
            washed.SandQualityPct = 10; washed.WashedOut = true;

            var cleanLie = BallPhysics.SolveLie(clean, T);
            var washedLie = BallPhysics.SolveLie(washed, T);
            Assert.Greater(cleanLie.DistanceFactor, washedLie.DistanceFactor, "clean sand plays better than a washed bunker");
            Assert.IsTrue(washedLie.Buried, "a washed-out bunker gives a plugged lie");

            // Raking restores consistency (and clears the washout) through the existing bunker action.
            MaintenanceSystem.ApplyMechanical(washed, new ZoneAction { Rake = true, Quality = 1.0 }, T);
            MaintenanceSystem.ApplyBunkerSand(washed, new ZoneAction { Rake = true, Quality = 1.0 },
                                              new WeatherDay { RainMm = 0 }, T);
            Assert.IsFalse(washed.WashedOut, "raking clears the washout");
            Assert.Greater(washed.SandQualityPct, 10.0, "raking restores sand consistency");
        }

        // ---- per-surface maintenance competes for the same crew-hours ----------------------
        [Test]
        public void SurfaceTasks_CompeteForCrewHours_AcrossSurfaces()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 1);
            var w = new Greenkeeper.Sim.Crew.MaintenanceWindow(Greenkeeper.Sim.Crew.CrewMember.DefaultCrew());

            // A full-course morning: mow + spray greens + gang-mow fairways/rough/tees + rake bunkers
            // can't all fit (≈37h into a 30h budget) — the triage now spans every surface.
            foreach (var g in course.Greens) w.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.WalkMow(g.Id));
            foreach (var g in course.Greens) w.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.Spray(g.Id));
            w.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.MowFairwaysAll());
            w.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.MowRoughAll());
            w.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.MowTeesAll());
            w.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.RakeBunkers());

            Assert.IsTrue(w.CouldNotFitEverything, "greens + every other surface cannot all be done in one window");
            Assert.LessOrEqual(w.UsedHours, w.BudgetHours + 1e-9, "the budget is never exceeded");

            // The gang fairway pass really does target all 18 fairways when it runs.
            var w2 = new Greenkeeper.Sim.Crew.MaintenanceWindow(Greenkeeper.Sim.Crew.CrewMember.DefaultCrew());
            w2.TryAssign(Greenkeeper.Sim.Crew.TaskCatalog.MowFairwaysAll());
            var plan = w2.ToDayPlan(course);
            Assert.IsTrue(plan.For("fairway-01").Mow && plan.For("fairway-18").Mow, "a gang pass mows every fairway");
        }
    }
}
