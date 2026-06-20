// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
// The four keystone tests (Test Spec): determinism, invariant fuzz, behavioural severity, fairness.
using System.Collections.Generic;
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    [TestFixture]
    public class KeystoneTests
    {
        // ---- T1 — Determinism -------------------------------------------------------------
        [Test]
        public void T1_Determinism_SameSeedAndPlan_DeepEqualFinalState()
        {
            const int seed = 31337;
            var a = new SimHarness(seed, new PlanLibrary.RandomPlanner(seed).Plan).Run(150);
            var b = new SimHarness(seed, new PlanLibrary.RandomPlanner(seed).Plan).Run(150);

            var va = a.FinalStateValues();
            var vb = b.FinalStateValues();
            Assert.AreEqual(va.Count, vb.Count, "state vector length differs");
            for (int i = 0; i < va.Count; i++)
                Assert.AreEqual(va[i], vb[i], 0.0, $"state diverged at index {i}");
        }

        // ---- T2 — Invariant fuzz ----------------------------------------------------------
        [Test]
        public void T2_InvariantFuzz_12000RandomDays_StayInRange()
        {
            int seed = 8675309;
            var cfg = CourseConfig.Mvp();
            var course = CourseFactory.Build(cfg, seed);
            var director = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
            var planner = new PlanLibrary.RandomPlanner(seed);

            for (int day = 0; day < 12000; day++)
            {
                Assert.DoesNotThrow(() => director.ResolveDay(planner.Plan(director.Clock.DayIndex, course)),
                    $"resolve threw on day {day}");
                string violation = StateValidator.FirstViolation(course, cfg.Tuning);
                Assert.IsNull(violation, $"invariant violated on day {day}: {violation}");
            }
        }

        // ---- T3 — Behavioural severity ----------------------------------------------------
        [Test]
        public void T3_BadPlanInfection_ExceedsTwiceGoodPlan_Over300Seeds()
        {
            const int seeds = 300;
            const int start = 90;   // summer
            const int days = 90;

            double sumBad = 0, sumGood = 0;
            for (int s = 0; s < seeds; s++)
            {
                var bad = new SimHarness(s, PlanLibrary.Bad, CourseConfig.GreensOnly(), start).Run(days);
                var good = new SimHarness(s, PlanLibrary.Good, CourseConfig.GreensOnly(), start).Run(days);
                sumBad += bad.MeanGreenInfection();
                sumGood += good.MeanGreenInfection();
            }
            double meanBad = sumBad / seeds;
            double meanGood = sumGood / seeds;

            TestContext.WriteLine($"mean bad={meanBad:F2} mean good={meanGood:F2}");
            Assert.Greater(meanBad, 2.0 * meanGood, $"bad ({meanBad:F2}) should exceed 2x good ({meanGood:F2})");
        }

        // ---- T4 — Fairness (KEYSTONE) -----------------------------------------------------
        [Test]
        public void T4a_Fairness_NoExpressionWithoutReadableTell_Over300Seeds()
        {
            const int seeds = 300;
            const int start = 90;
            const int days = 120;

            int expressions = 0;
            for (int s = 0; s < seeds; s++)
            {
                var h = new SimHarness(s, new PlanLibrary.RandomPlanner(s).Plan, CourseConfig.GreensOnly(),
                                       start, bypassFairnessGate: false).Run(days);
                foreach (var e in h.AllExpressions())
                {
                    expressions++;
                    Assert.IsTrue(h.TellInPrior3Days(e.ZoneId, e.DayIndex),
                        $"seed {s}: expression on {e.ZoneId} day {e.DayIndex} had NO readable tell in the prior 3 days");
                }
            }
            // Sanity: the audit actually saw some expressions to police.
            Assert.Greater(expressions, 0, "expected at least some legitimate (gated) expressions to occur");
            TestContext.WriteLine($"audited {expressions} gated expressions; none illegible");
        }

        [Test]
        public void T4b_Fairness_HasTeeth_DetectsUngatedExpressions()
        {
            // Inject ungated expressions (bypass the gate) and assert the SAME audit catches them.
            const int seeds = 50;
            const int start = 90;
            const int days = 60;

            int violations = 0;
            for (int s = 0; s < seeds; s++)
            {
                var h = new SimHarness(s, new PlanLibrary.RandomPlanner(s).Plan, CourseConfig.GreensOnly(),
                                       start, bypassFairnessGate: true).Run(days);
                foreach (var e in h.AllExpressions())
                    if (!h.TellInPrior3Days(e.ZoneId, e.DayIndex))
                        violations++;
            }
            Assert.Greater(violations, 0,
                "the fairness audit must DETECT ungated expressions (teeth check) — found none, so the test is blind");
            TestContext.WriteLine($"teeth check caught {violations} ungated expressions");
        }
    }
}
