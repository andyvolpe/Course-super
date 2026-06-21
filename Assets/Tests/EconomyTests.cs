// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 6 — economy: condition -> demand -> revenue, so mismanagement costs money.</summary>
    [TestFixture]
    public class EconomyTests
    {
        private static readonly EconomyConfig E = EconomyConfig.Default;

        [Test]
        public void HealthyCourse_ScoresHigh_SickCourse_ScoresLow()
        {
            var course = CourseFactory.Build(CourseConfig.GreensOnly(), 1);
            double healthy = ConditionSystem.CourseCondition(course, E);
            Assert.Greater(healthy, 70.0, "a fresh, healthy course should score well");

            foreach (var g in course.Greens)
            {
                g.DensityPct = 25; g.TurfDebtPct = 80;
                foreach (var c in g.Cells) c.Infection = 90;
                Greenkeeper.Sim.Math.DerivedSurfaces.Recompute(g, AgronomyTuning.Default);
            }
            double sick = ConditionSystem.CourseCondition(course, E);
            Assert.Less(sick, 40.0, "a diseased, thin, debt-laden course should score poorly");
            Assert.Less(sick, healthy - 30.0, "clear separation");
        }

        [Test]
        public void GoodManagement_IsProfitable_Mismanagement_BleedsCash()
        {
            double start = EconomyConfig.Default.StartingCash;
            double goodCash = RunSeasonCash(PlanGood);
            double badCash = RunSeasonCash(PlanNeglect);

            TestContext.WriteLine($"start ${start:N0} -> end cash — good: ${goodCash:N0}  neglect: ${badCash:N0}");
            Assert.Greater(goodCash, start, "a well-run season must end in net PROFIT");
            Assert.Less(badCash, start, "a do-nothing season must end in net LOSS (fixed costs drain regardless)");
            Assert.Less(badCash, 0.0, "doing nothing for a season should bury you in the red, not merely earn less");
        }

        [Test]
        public void Reputation_LagsCondition_RatherThanSnapping()
        {
            var cfg = CourseConfig.GreensOnly();
            var course = CourseFactory.Build(cfg, 3);
            var dir = new GameDirector(course, 3, cfg.Tuning, cfg.Grass)
            {
                Economy = new EconomyState(E),
                EconomyConfig = E,
            };
            dir.Clock.JumpTo(90);

            // Establish a strong reputation with several good days.
            for (int i = 0; i < 20; i++) dir.ResolveDay(PlanGood(course));
            double repHigh = dir.Economy.Reputation;
            double condHigh = dir.Economy.Latest.ConditionIndex;
            Assert.Greater(repHigh, 60.0);

            // Tank the course instantly; reputation must not crash in a single day.
            foreach (var g in course.Greens)
            {
                g.DensityPct = 5; g.TurfDebtPct = 95;
                foreach (var c in g.Cells) c.Infection = 90;
            }
            var led = EconomySystem.Settle(dir.Economy, course, new WeatherDay { TmaxF = 80, TminF = 60 },
                                           new DayPlan(), 200, Season.Summer, E, AgronomyTuning.Default);
            Assert.Less(led.ConditionIndex, 40.0, "condition dropped immediately");
            Assert.Greater(dir.Economy.Reputation, led.ConditionIndex + 15.0, "but reputation lags it (EMA)");
        }

        // ---- helpers ----

        private static double RunSeasonCash(System.Func<CourseState, DayPlan> planFn)
        {
            var cfg = CourseConfig.GreensOnly();
            var course = CourseFactory.Build(cfg, 7);
            var dir = new GameDirector(course, 7, cfg.Tuning, cfg.Grass)
            {
                Economy = new EconomyState(E),
                EconomyConfig = E,
            };
            dir.Clock.JumpTo(90); // summer
            for (int d = 0; d < 90; d++) dir.ResolveDay(planFn(course));
            return dir.Economy.Cash;
        }

        private static DayPlan PlanGood(CourseState course)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None;
                a.Mow = true; a.MowHeightIn = 0.125;
                a.IrrigationMm = System.Math.Max(0, (16.0 - z.SoilMoisturePct) / 0.9);
                a.FertilizerN = z.NitrogenPct < 35 ? 12 : 0;
                a.Spray = (z.MaxInfection > 0 || z.MeanPressure > AgronomyTuning.Default.TellPressureThreshold);
                plan.Set(z.Id, a);
            }
            return plan;
        }

        private static DayPlan PlanNeglect(CourseState course)
        {
            // Do nothing but keep the lights on (overhead still accrues). Course slides; rounds dry up.
            return new DayPlan();
        }
    }
}
