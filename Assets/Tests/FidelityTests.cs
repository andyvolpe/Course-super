// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 4.4 — fidelity scales with tension: routine days skip; crises stop the skip.</summary>
    [TestFixture]
    public class FidelityTests
    {
        // weatherInterrupts=false isolates the disease-break / skip mechanics from weather noise.
        private static GameDirector Director(int seed, int startDay, out CourseState course, bool weatherInterrupts = false)
        {
            var cfg = CourseConfig.GreensOnly();
            course = CourseFactory.Build(cfg, seed);
            var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass) { WeatherInterruptsEnabled = weatherInterrupts };
            dir.Clock.JumpTo(startDay);
            return dir;
        }

        private static DayPlan WetUnsprayed(CourseState course)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None; a.Mow = true; a.MowHeightIn = 0.125;
                a.IrrigationMm = 22.0; a.Spray = false; // brew disease
                plan.Set(z.Id, a);
            }
            return plan;
        }

        [Test]
        public void QuietSpringDays_SkipFreely_NoInterrupt()
        {
            var dir = Director(seed: 3, startDay: 0, out var course); // early spring: cool + (no plan) dry
            int resolved = dir.SkipUntil(stop: d => d.Clock.DayIndex >= 45);
            Assert.AreEqual(45, resolved, "quiet days should skip straight through");
            Assert.IsFalse(dir.InterruptRaised, "no crisis in a cool, dry spring");
        }

        [Test]
        public void DiseaseBreak_RaisesInterrupt_AndStopsTheSkip()
        {
            var dir = Director(seed: 11, startDay: 90, out var course); // summer
            DayResult crisis = null;
            int day = 0;
            for (; day < 120; day++)
            {
                var r = dir.ResolveDay(WetUnsprayed(course));
                if (r.IsInterruptDay) { crisis = r; break; }
            }
            Assert.IsNotNull(crisis, "a wet, unsprayed summer green must eventually trigger a disease-break interrupt");
            Assert.IsTrue(dir.InterruptRaised);
            CollectionAssert.IsNotEmpty(crisis.Interrupts);
            StringAssert.Contains("Disease break", string.Join(";", crisis.Interrupts));

            // And a routine skip would have stopped on exactly that crisis.
            var dir2 = Director(seed: 11, startDay: 90, out var course2);
            int skipped = dir2.SkipUntil(stop: _ => false, planProvider: _ => WetUnsprayed(course2), maxDays: 200);
            Assert.Less(skipped, 200, "the skip must stop at the interrupt, not run to the cap");
            Assert.IsTrue(dir2.InterruptRaised);
        }

        [Test]
        public void MowingOnFrost_DamagesTheTurf()
        {
            for (int seed = 0; seed < 80; seed++)
            {
                var weather = new WeatherSystem(seed);
                for (int d = 0; d < GameClock.DaysPerYear; d++)
                {
                    if (!WeatherEvents.IsFrost(weather.Generate(d), AgronomyTuning.Default)) continue;

                    // Two identical greens; on the same frost day, one is mown and one is left alone.
                    var cfg = CourseConfig.GreensOnly(1);
                    var courseM = CourseFactory.Build(cfg, 1);
                    var courseL = CourseFactory.Build(cfg, 1);
                    var dirM = new GameDirector(courseM, seed, cfg.Tuning, cfg.Grass);
                    var dirL = new GameDirector(courseL, seed, cfg.Tuning, cfg.Grass);
                    dirM.Clock.JumpTo(d); dirL.Clock.JumpTo(d);

                    var mowPlan = new DayPlan();
                    mowPlan.Set("green-01", new ZoneAction { Mow = true, MowHeightIn = 0.125, Quality = 1.0 });
                    var res = dirM.ResolveDay(mowPlan);
                    dirL.ResolveDay(new DayPlan()); // left alone

                    var gM = courseM.Get("green-01"); var gL = courseL.Get("green-01");
                    StringAssert.Contains("Frost", string.Join(";", res.Interrupts));
                    Assert.Less(gM.DensityPct, gL.DensityPct - 3.0, "mowing frozen turf must cost density vs leaving it");
                    Assert.Greater(gM.TurfDebtPct, gL.TurfDebtPct + 3.0, "and it must register as turf debt");
                    return;
                }
            }
            Assert.Fail("no frost day found to test");
        }

        [Test]
        public void HeatSpike_RaisesInterrupt_OverASummer()
        {
            // Heat spikes are weather-driven; over a couple of summers at least one >95F day occurs.
            var dir = Director(seed: 5, startDay: 90, out var course, weatherInterrupts: true);
            bool sawHeat = false;
            for (int i = 0; i < 540 && !sawHeat; i++)
            {
                var r = dir.ResolveDay(new DayPlan());
                foreach (var s in r.Interrupts) if (s.StartsWith("Heat spike")) sawHeat = true;
            }
            Assert.IsTrue(sawHeat, "a heat-spike interrupt should fire on a hot enough day");
        }

        [Test]
        public void Interrupt_CanBeCleared_ThenSkipResumes()
        {
            var dir = Director(seed: 11, startDay: 90, out var course);
            dir.SkipUntil(stop: _ => false, planProvider: _ => WetUnsprayed(course), maxDays: 200);
            Assert.IsTrue(dir.InterruptRaised);

            dir.ClearInterrupt();
            int more = dir.SkipUntil(stop: _ => false, planProvider: _ => WetUnsprayed(course), maxDays: 5);
            Assert.GreaterOrEqual(more, 1, "after clearing, the skip resumes (resolves at least one more day)");
        }
    }
}
