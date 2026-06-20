// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Sim.Tournament;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 7 — the tournament rung: graded setup, payout, progression.</summary>
    [TestFixture]
    public class TournamentTests
    {
        private static CourseState Greens(int seed = 1) => CourseFactory.Build(CourseConfig.GreensOnly(), seed);

        private static TournamentSpec Spec(int day = 120) => new TournamentSpec
        {
            Name = "Test Cup", DayIndex = day,
            StimpMin = 11.0, StimpMax = 12.5, FirmMin = 60, FirmMax = 85,
            MaxInfection = 5, MinDensity = 80, ConsistencyToleranceStimp = 0.5,
            PassScore = 70, PrizeMoney = 40000, ReputationGain = 12,
        };

        private static void SetGreens(CourseState c, double stimp, double firm, double density, double infection = 0)
        {
            foreach (var g in c.Greens)
            {
                g.Stimp = stimp; g.FirmnessPct = firm; g.DensityPct = density;
                foreach (var cell in g.Cells) cell.Infection = infection;
            }
        }

        [Test]
        public void CourseOnSpec_GradesWell_OffSpec_Fails()
        {
            var spec = Spec();

            var good = Greens();
            SetGreens(good, stimp: 11.7, firm: 72, density: 90, infection: 0);
            var goodResult = TournamentSystem.Evaluate(good, spec);
            Assert.IsTrue(goodResult.Passed, $"a course on spec should pass (scored {goodResult.Score:F0})");
            Assert.GreaterOrEqual((int)goodResult.Grade, (int)TournamentGrade.Silver);

            var bad = Greens();
            SetGreens(bad, stimp: 8.5, firm: 35, density: 55, infection: 40); // slow, soft, thin, diseased
            var badResult = TournamentSystem.Evaluate(bad, spec);
            Assert.IsFalse(badResult.Passed, $"an off-spec course should fail (scored {badResult.Score:F0})");
            Assert.AreEqual(TournamentGrade.Fail, badResult.Grade);
        }

        [Test]
        public void ConsistentGreens_BeatErraticOnes_AtTheSameMean()
        {
            var spec = Spec();
            var consistent = Greens();
            SetGreens(consistent, 11.7, 72, 90);

            var erratic = Greens();
            SetGreens(erratic, 11.7, 72, 90);
            // Same mean Stimp (~11.7) but wildly varying green to green.
            int i = 0;
            foreach (var g in erratic.Greens) { g.Stimp = (i++ % 2 == 0) ? 9.7 : 13.7; }

            double cScore = TournamentSystem.Evaluate(consistent, spec).Score;
            double eScore = TournamentSystem.Evaluate(erratic, spec).Score;
            Assert.Greater(cScore, eScore, "consistency must matter — erratic greens grade lower at the same mean");
        }

        [Test]
        public void PassingPaysOut_FailingDingsReputation()
        {
            var eco = new EconomyState(EconomyConfig.Default);
            double startCash = eco.Cash, startRep = eco.Reputation;

            var win = Greens();
            SetGreens(win, 11.7, 72, 90);
            var ladder = new TournamentLadder(new System.Collections.Generic.List<TournamentSpec> { Spec(120) });
            var res = ladder.ProcessDay(120, win, eco);
            Assert.IsNotNull(res);
            Assert.Greater(eco.Cash, startCash, "a passed tournament pays prize money");
            Assert.Greater(eco.Reputation, startRep, "and lifts reputation");
            Assert.AreEqual(1, ladder.CurrentRung, "the ladder advances");

            var eco2 = new EconomyState(EconomyConfig.Default);
            double rep2 = eco2.Reputation;
            var flub = Greens();
            SetGreens(flub, 8.0, 30, 50, 50);
            var ladder2 = new TournamentLadder(new System.Collections.Generic.List<TournamentSpec> { Spec(120) });
            var res2 = ladder2.ProcessDay(120, flub, eco2);
            Assert.IsFalse(res2.Passed);
            Assert.AreEqual(0.0, res2.PrizeAwarded, 1e-9, "a flub pays nothing");
            Assert.Less(eco2.Reputation, rep2, "and dings standing");
        }

        [Test]
        public void Ladder_FiresOnlyOnTheDay_AndIsWiredIntoResolve()
        {
            var cfg = CourseConfig.GreensOnly();
            var course = CourseFactory.Build(cfg, 5);
            var dir = new GameDirector(course, 5, cfg.Tuning, cfg.Grass)
            {
                Economy = new EconomyState(EconomyConfig.Default),
                Tournament = new TournamentLadder(new System.Collections.Generic.List<TournamentSpec> { Spec(95) }),
            };
            dir.Clock.JumpTo(93);
            SetGreens(course, 11.7, 72, 90);

            var d93 = dir.ResolveDay(new DayPlan());
            Assert.IsNull(d93.Tournament, "no tournament before the day");

            SetGreens(course, 11.7, 72, 90); // hold on spec for the event day
            var d94 = dir.ResolveDay(new DayPlan());
            Assert.IsNull(d94.Tournament);

            SetGreens(course, 11.7, 72, 90);
            var d95 = dir.ResolveDay(new DayPlan());
            Assert.IsNotNull(d95.Tournament, "the tournament fires on its day");
            Assert.IsTrue(dir.InterruptRaised, "and pulls the player in (stops the skip)");
            StringAssert.Contains("Tournament", string.Join(";", d95.Interrupts));
        }
    }
}
