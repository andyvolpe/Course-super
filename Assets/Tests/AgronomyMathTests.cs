// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 2.2 — water / drainage / ET guards and whole-pipeline invariants.</summary>
    [TestFixture]
    public class AgronomyMathTests
    {
        private static ZoneState BuildGreen(SoilType soil, int seed = 1)
        {
            var cfg = new CourseConfig();
            cfg.Zones.Add(new ZoneSpec("g", ZoneType.Green, soil, 1, 3));
            return CourseFactory.Build(cfg, seed).Get("g");
        }

        [Test]
        public void ZeroWaterPlusHeat_MoistureDecreasesMonotonically_UntilClamped()
        {
            var t = AgronomyTuning.Default;
            var z = BuildGreen(SoilType.UsgaSpec);
            var hotDry = new WeatherDay { TminF = 70, TmaxF = 98, RainMm = 0, LeafWetnessHrs = 0, SolarRa = 16 };

            double prev = z.SoilMoisturePct;
            double last = prev;
            bool reachedFloor = false;
            for (int day = 0; day < 60; day++)
            {
                WaterBalance.Apply(z, hotDry, irrigationMm: 0, t);
                Assert.LessOrEqual(z.SoilMoisturePct, prev + 1e-9, $"moisture rose on day {day} with no input");
                prev = z.SoilMoisturePct;
                last = z.SoilMoisturePct;
                if (z.SoilMoisturePct <= t.ResidualMoisturePct + 1e-6) reachedFloor = true;
            }
            Assert.IsTrue(reachedFloor, "moisture should bottom out at the residual floor");
            Assert.That(last, Is.InRange(t.ResidualMoisturePct - 1e-6, t.ResidualMoisturePct + 1e-6));
        }

        [Test]
        public void UsgaGreen_DrainsFasterThanPushUp_AtEqualInput()
        {
            var t = AgronomyTuning.Default;
            var usga = BuildGreen(SoilType.UsgaSpec, 3);
            var push = BuildGreen(SoilType.PushUp, 3);

            // Equalise everything except soil type, then saturate both well above their FC.
            foreach (var z in new[] { usga, push })
            {
                z.OrganicMatterPct = 35.0;
                z.SoilMoisturePct = 38.0;
            }
            var mild = new WeatherDay { TminF = 60, TmaxF = 75, RainMm = 0, LeafWetnessHrs = 0, SolarRa = 12 };

            var ru = WaterBalance.Apply(usga, mild, 0, t);
            var rp = WaterBalance.Apply(push, mild, 0, t);

            Assert.Greater(ru.DrainageVwc, rp.DrainageVwc, "USGA spec should drain more");
            Assert.Less(usga.SoilMoisturePct, push.SoilMoisturePct, "USGA green should end drier");
        }

        [Test]
        public void FiveHundredDays_RandomLegalInputs_NeverNaNorOutOfRange()
        {
            int seed = 20240620;
            var cfg = CourseConfig.Mvp();
            var course = CourseFactory.Build(cfg, seed);
            var director = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
            var planner = new PlanLibrary.RandomPlanner(seed);

            for (int day = 0; day < 500; day++)
            {
                director.ResolveDay(planner.Plan(director.Clock.DayIndex, course));
                string violation = StateValidator.FirstViolation(course, cfg.Tuning);
                Assert.IsNull(violation, $"invariant violated on day {day}: {violation}");
            }
        }
    }
}
