// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 3.1/3.2/3.3 — the legibility mapping, the single gate, and the assist layer.</summary>
    [TestFixture]
    public class LegibilityTests
    {
        private static readonly AgronomyTuning T = AgronomyTuning.Default;

        private static ZoneState Green()
        {
            var g = CourseFactory.Build(CourseConfig.GreensOnly(1), 1).Get("green-01");
            // Neutral moisture so colour-depth tests aren't perturbed by wilt/wet tints.
            g.SoilMoisturePct = 16.0;
            return g;
        }

        // ---- 3.1 — fairness: tells NARROW, never pinpoint --------------------------------

        [Test]
        public void NPushedTurf_LooksTheSameAs_HealthyVigorous()
        {
            var vigorous = Green(); vigorous.NitrogenPct = 50.0; vigorous.DensityPct = 90.0;
            var pushed = Green(); pushed.NitrogenPct = 90.0; pushed.DensityPct = 90.0;

            var a = LegibilityMapping.ForCell(vigorous, 0, T).BaseColor;
            var b = LegibilityMapping.ForCell(pushed, 0, T).BaseColor;

            Assert.AreEqual(a.R, b.R, 1e-9, "N-push must not be distinguishable from vigour by colour");
            Assert.AreEqual(a.G, b.G, 1e-9);
            Assert.AreEqual(a.B, b.B, 1e-9);
        }

        [Test]
        public void StarvedTurf_LooksPalerThanFedTurf()
        {
            var fed = Green(); fed.NitrogenPct = 50.0; fed.DensityPct = 90.0;
            var starved = Green(); starved.NitrogenPct = 10.0; starved.DensityPct = 90.0;

            var f = LegibilityMapping.ForCell(fed, 0, T).BaseColor;
            var s = LegibilityMapping.ForCell(starved, 0, T).BaseColor;

            // Paler = lighter/yellower = higher R and G channels.
            Assert.Greater(s.R, f.R, "starved turf should read paler");
            Assert.Greater(s.G, f.G);
        }

        [Test]
        public void TurfDebt_IsNotEncodedInTheVisualTell()
        {
            var low = Green(); low.TurfDebtPct = 5.0;
            var high = Green(); high.TurfDebtPct = 95.0; // identical density/N/moisture — only debt differs

            var a = LegibilityMapping.ForCell(low, 0, T);
            var b = LegibilityMapping.ForCell(high, 0, T);

            Assert.AreEqual(a.BaseColor.R, b.BaseColor.R, 1e-9, "debt must never show directly");
            Assert.AreEqual(a.Thinning, b.Thinning, 1e-9);
            Assert.AreEqual(a.Lesions, b.Lesions, 1e-9);
        }

        [Test]
        public void Symptoms_ShowOnlyWhereExpressed_AndThinningTracksDensity()
        {
            var g = Green(); g.DensityPct = 40.0;        // thin
            g.Cells[4].ExpressionSeverity = 60.0;        // one sick cell
            var sick = LegibilityMapping.ForCell(g, 4, T);
            var clean = LegibilityMapping.ForCell(g, 0, T);

            Assert.Greater(sick.Lesions, 0.0, "expressed cell shows lesions");
            Assert.AreEqual(0.0, clean.Lesions, 1e-9, "unexpressed cell shows none");
            Assert.Greater(sick.Thinning, 0.5, "low density reads as thinning");
        }

        // ---- 3.2 — the gate hides hidden state until earned ------------------------------

        [Test]
        public void FullDifficulty_HidesEverythingUntilEarned()
        {
            var g = Green(); g.Cells[0].Infection = 40.0;
            var leg = new LegibilitySystem(T, DifficultySettings.Full());

            var obs = leg.Observe(g, dayIndex: 10);
            Assert.IsNotNull(obs.Tells, "free visual tells are always available");
            Assert.IsFalse(obs.InfectionRevealed, "infection detail must be earned by scouting");
            Assert.IsFalse(obs.MoistureMetered, "exact moisture must be earned by a meter");
            Assert.IsFalse(obs.SoilTested, "nutrient trend must be earned by a soil test");
            Assert.IsFalse(obs.TurfDebtShown, "turf debt is never shown on full difficulty");
        }

        [Test]
        public void Scout_RevealsInfection_ThenGoesStale()
        {
            var g = Green(); g.Cells[0].Infection = 40.0;
            var leg = new LegibilitySystem(T, DifficultySettings.Full());

            leg.Scout(g.Id, dayIndex: 10);
            Assert.IsTrue(leg.Observe(g, 11).InfectionRevealed, "scouting reveals infection");
            Assert.AreEqual(40.0, leg.Observe(g, 11).RevealedMaxInfection, 1e-9);
            Assert.IsFalse(leg.Observe(g, 99).InfectionRevealed, "the reveal goes stale");
        }

        [Test]
        public void Meter_ReturnsTrueVwc_ForThatReadingOnly()
        {
            var g = Green(); g.SoilMoisturePct = 23.5;
            var leg = new LegibilitySystem(T, DifficultySettings.Full());

            double reading = leg.MeterReading(g, cellIndex: 4, dayIndex: 10);
            Assert.AreEqual(23.5, reading, 1e-9, "meter returns true VWC");
            Assert.IsTrue(leg.Observe(g, 10).MoistureMetered, "fresh reading is shown");
            Assert.IsFalse(leg.Observe(g, 50).MoistureMetered, "stale reading is not");
        }

        [Test]
        public void TurfDebt_ShownOnlyWithAssists_NeverOnFull()
        {
            var g = Green(); g.TurfDebtPct = 70.0;

            var full = new LegibilitySystem(T, DifficultySettings.Full());
            full.Scout(g.Id, 10); // even fully scouted...
            Assert.IsFalse(full.Observe(g, 10).TurfDebtShown, "...full difficulty never surfaces debt");

            var assisted = new LegibilitySystem(T, DifficultySettings.WithAssists());
            var obs = assisted.Observe(g, 10);
            Assert.IsTrue(obs.TurfDebtShown, "assists surface the debt bar");
            Assert.AreEqual(70.0, obs.TurfDebtPct, 1e-9);
            Assert.IsTrue(obs.InfectionRevealed, "assists auto-scout");
        }

        // ---- 3.3 — assists change SURFACING only, never the simulation -------------------

        [Test]
        public void Assists_DoNotChangeTheSimulation_DeterminismHolds()
        {
            // Run the same seed+plan three ways: observing with full difficulty, observing with
            // assists, and not observing at all. The sim state must be byte-identical in all cases.
            var control = Run(observe: null);
            var full = Run(new LegibilitySystem(T, DifficultySettings.Full()));
            var assisted = Run(new LegibilitySystem(T, DifficultySettings.WithAssists()));

            AssertDeepEqual(control, full, "full-difficulty observation must not affect the sim");
            AssertDeepEqual(control, assisted, "assisted observation must not affect the sim");
        }

        private static System.Collections.Generic.List<double> Run(LegibilitySystem observe)
        {
            int seed = 777;
            var cfg = CourseConfig.GreensOnly();
            var course = CourseFactory.Build(cfg, seed);
            var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
            dir.Clock.JumpTo(90);
            var planner = new PlanLibrary.RandomPlanner(seed);
            for (int d = 0; d < 120; d++)
            {
                dir.ResolveDay(planner.Plan(dir.Clock.DayIndex, course));
                if (observe != null)
                    foreach (var z in course.Greens) observe.Observe(z, dir.Clock.DayIndex);
            }
            var values = new System.Collections.Generic.List<double>();
            course.CollectStateValues(values);
            return values;
        }

        private static void AssertDeepEqual(System.Collections.Generic.List<double> a,
                                            System.Collections.Generic.List<double> b, string msg)
        {
            Assert.AreEqual(a.Count, b.Count, msg);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i], 0.0, $"{msg} (index {i})");
        }
    }
}
