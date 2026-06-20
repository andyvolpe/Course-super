// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using System;
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 5.1/5.2/5.3 — weather as a deterministic system, a fallible forecast, extreme events.</summary>
    [TestFixture]
    public class WeatherTests
    {
        private static readonly AgronomyTuning T = AgronomyTuning.Default;

        // ---- 5.1 — weather is seed-deterministic --------------------------------------
        [Test]
        public void SameSeed_ProducesIdenticalWeatherSequence()
        {
            var a = new WeatherSystem(2024);
            var b = new WeatherSystem(2024);
            for (int d = 0; d < 365; d++)
            {
                var wa = a.Generate(d); var wb = b.Generate(d);
                Assert.AreEqual(wa.TmaxF, wb.TmaxF, 0.0, $"Tmax day {d}");
                Assert.AreEqual(wa.TminF, wb.TminF, 0.0, $"Tmin day {d}");
                Assert.AreEqual(wa.RainMm, wb.RainMm, 0.0, $"rain day {d}");
                Assert.AreEqual(wa.LeafWetnessHrs, wb.LeafWetnessHrs, 0.0, $"wetness day {d}");
                Assert.AreEqual(wa.Humidity, wb.Humidity, 0.0, $"humidity day {d}");
            }
        }

        [Test]
        public void DifferentSeeds_Differ()
        {
            var a = new WeatherSystem(1);
            var b = new WeatherSystem(2);
            int diffs = 0;
            for (int d = 0; d < 120; d++) if (Math.Abs(a.Generate(d).TmaxF - b.Generate(d).TmaxF) > 1e-9) diffs++;
            Assert.Greater(diffs, 60, "different seeds should give materially different weather");
        }

        // ---- 5.2 — forecast tightens toward the day, and can miss ----------------------
        [Test]
        public void Forecast_IsTighterNearTerm_AndDeterministic()
        {
            var f1 = new Forecast(77, T);
            var f2 = new Forecast(77, T);
            var weather = new WeatherSystem(77);

            double sumNear = 0, sumFar = 0; int misses = 0; int n = 0;
            for (int day = 0; day < 200; day++)
            {
                var actualNext = weather.Generate(day + 1);
                var actualFar = weather.Generate(day + 5);

                var near = f1.Predict(day, day + 1);
                var far = f1.Predict(day, day + 5);

                // Deterministic.
                Assert.AreEqual(near.Predicted.TmaxF, f2.Predict(day, day + 1).Predicted.TmaxF, 0.0);

                double errNear = Math.Abs(near.Predicted.TmaxF - actualNext.TmaxF);
                double errFar = Math.Abs(far.Predicted.TmaxF - actualFar.TmaxF);
                sumNear += errNear; sumFar += errFar;

                Assert.LessOrEqual(errNear, near.TempBandF + 1e-9, $"day+1 must stay within its (tight) band, day {day}");
                Assert.LessOrEqual(errFar, far.TempBandF + 1e-9, $"day+5 must stay within its (wide) band, day {day}");
                if (errNear > 0.25) misses++;
                n++;
            }

            Assert.Less(sumNear / n, sumFar / n, "near-term forecast must be tighter on average than far-term");
            Assert.Greater(misses, 0, "the forecast must actually miss sometimes (it's not the truth)");
        }

        [Test]
        public void Forecast_Today_EqualsActual()
        {
            var f = new Forecast(5, T);
            var w = new WeatherSystem(5);
            var p = f.Predict(currentDay: 10, targetDay: 10); // zero days out
            Assert.AreEqual(w.Generate(10).TmaxF, p.Predicted.TmaxF, 1e-9, "no uncertainty about today");
            Assert.AreEqual(0.0, p.TempBandF, 1e-9);
        }

        // ---- 5.3 — extreme events fire interrupts + effects ---------------------------
        [Test]
        public void Storm_WashesOutBunkers_AndRakingRestoresThem()
        {
            // Find a stormy day for some seed, then confirm bunkers wash out and rake clears them.
            for (int seed = 0; seed < 50; seed++)
            {
                var weather = new WeatherSystem(seed);
                for (int d = 0; d < GameClock.DaysPerYear; d++)
                {
                    if (!WeatherEvents.IsStorm(weather.Generate(d), T)) continue;

                    var cfg = CourseConfig.Mvp();
                    var course = CourseFactory.Build(cfg, seed);
                    var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
                    dir.Clock.JumpTo(d);
                    var res = dir.ResolveDay(new DayPlan());

                    StringAssert.Contains("Storm", string.Join(";", res.Interrupts));
                    Assert.IsTrue(AnyBunkerWashedOut(course), "a storm washes bunkers out");

                    // Rake them next day.
                    var plan = new DayPlan();
                    foreach (var z in course.Zones)
                        if (z.Type == ZoneType.Bunker) plan.Set(z.Id, new ZoneAction { Rake = true, Quality = 1.0 });
                    dir.ResolveDay(plan);
                    Assert.IsFalse(AnyBunkerWashedOut(course), "raking restores washed-out bunkers");
                    return; // proved it
                }
            }
            Assert.Fail("no storm found to test — distribution may be off");
        }

        [Test]
        public void Frost_BlocksMowing()
        {
            for (int seed = 0; seed < 80; seed++)
            {
                var weather = new WeatherSystem(seed);
                for (int d = 0; d < GameClock.DaysPerYear; d++)
                {
                    if (!WeatherEvents.IsFrost(weather.Generate(d), T)) continue;

                    var cfg = CourseConfig.GreensOnly();
                    var course = CourseFactory.Build(cfg, seed);
                    var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
                    dir.Clock.JumpTo(d);
                    var g = course.Get("green-01");
                    double grainBefore = g.GrainPct;

                    // Order a mow on a frost day; it must be held (no grain knockdown from mowing).
                    var plan = new DayPlan();
                    plan.Set(g.Id, new ZoneAction { Mow = true, MowHeightIn = 0.1, Quality = 1.0 });
                    var res = dir.ResolveDay(plan);

                    StringAssert.Contains("Frost", string.Join(";", res.Interrupts));
                    Assert.AreEqual(grainBefore, g.GrainPct, 1e-9, "frost should block the mow (grain unchanged by mowing)");
                    return;
                }
            }
            Assert.Fail("no frost found to test");
        }

        private static bool AnyBunkerWashedOut(Greenkeeper.Sim.State.CourseState course)
        {
            foreach (var z in course.Zones) if (z.Type == ZoneType.Bunker && z.WashedOut) return true;
            return false;
        }
    }
}
