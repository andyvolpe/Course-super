// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>
    /// The nitrogen FERTILITY PROGRAM (Part A consequences + Part B program decisions). N is a fork,
    /// not a one-way slider: too little = dollar spot / weakness; too much = brown patch / Pythium,
    /// thatch, weak roots, spent reserves, heat-stress collapse and burn.
    /// </summary>
    [TestFixture]
    public class FertilityTests
    {
        private static readonly AgronomyTuning T = AgronomyTuning.Default;
        private static readonly GrassProfile G = GrassProfile.Mvp();

        private static GameDirector SummerDir(int seed, out CourseState course)
        {
            var cfg = CourseConfig.GreensOnly(1);
            course = CourseFactory.Build(cfg, seed);
            var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass) { WeatherInterruptsEnabled = false };
            dir.Clock.JumpTo(90); // summer — heat + disease pressure
            return dir;
        }

        private static ZoneState FreshGreen(double moisture = 16.0)
        {
            var g = CourseFactory.Build(CourseConfig.GreensOnly(1), 1).Get("green-01");
            g.SoilMoisturePct = moisture;
            return g;
        }

        // ============================================================================
        // (a) OVER-FEEDING punishes both ways — disease, thatch, roots, reserves, heat —
        //     while LOOKING greener short-term (the trap). Both seasons are sprayed and
        //     watered identically; the ONLY difference is the fertility program.
        // ============================================================================
        [Test]
        public void OverFeeding_RaisesDiseaseThatchAndCollapse_WhileLookingGreenerEarly()
        {
            var over = RunSeason(seed: 90, days: 40, plan: OverNPlan);
            var inband = RunSeason(seed: 90, days: 40, plan: InBandPlan);

            double overSurvival = SurviveHeatWave(over.Green);
            double bandSurvival = SurviveHeatWave(inband.Green);

            TestContext.WriteLine(
                $"OVER  maxN={over.MaxN:F1} root={over.FinalRoot:F2} OM={over.FinalOm:F1} carbD5={over.CarbDay5:F1} " +
                $"maxHighN={over.MaxHighNFavor:F3} earlyR={over.EarlyColorR:F3} heatSurvDens={overSurvival:F1}");
            TestContext.WriteLine(
                $"BAND  maxN={inband.MaxN:F1} root={inband.FinalRoot:F2} OM={inband.FinalOm:F1} carbD5={inband.CarbDay5:F1} " +
                $"maxHighN={inband.MaxHighNFavor:F3} earlyR={inband.EarlyColorR:F3} heatSurvDens={bandSurvival:F1}");

            // The over-fed program actually drove N out of band; the in-band program held it inside.
            Assert.Greater(over.MaxN, G.NOptMax, "the over-N program must drive N out of band");
            Assert.LessOrEqual(inband.MaxN, G.NOptMax, "the in-band program must hold N inside the band");

            // 1) Brown-patch / Pythium favorability is a HIGH-N phenomenon — present over-fed, ~absent in-band.
            Assert.Greater(over.MaxHighNFavor, 0.05, "over-feeding must raise brown-patch/Pythium favorability");
            Assert.Less(inband.MaxHighNFavor, 1e-6, "in-band feeding must not create high-N disease pressure");

            // 2) Faster organic-matter (thatch) accrual.
            Assert.Greater(over.FinalOm, inband.FinalOm + 1.0, "over-feeding must accelerate thatch");

            // 3) Shallower roots.
            Assert.Less(over.FinalRoot, inband.FinalRoot - 0.2, "over-feeding must shrink the roots");

            // 4) Carb reserves spent down faster (read early, before summer heat floors both).
            Assert.Less(over.CarbDay5, inband.CarbDay5 - 2.0, "over-feeding must spend reserves faster");

            // 5) Worse HEAT-STRESS SURVIVAL: subject both end-states to an identical heat wave — the
            //    over-fed turf (shallow roots / spent reserves) collapses where the in-band turf holds.
            Assert.Less(overSurvival, bandSurvival - 5.0, "over-fed turf must survive heat stress far worse");

            // ...yet it LOOKED greener early (lower R = deeper green): the trap.
            Assert.Less(over.EarlyColorR, inband.EarlyColorR - 1e-4, "over-fed turf must read greener short-term");
        }

        // ============================================================================
        // (b) Big SOLUBLE GRANULAR dump (spike then crash, more burn) vs equivalent N
        //     SPOON-FED foliar (steady, gentle). Measurably different curves.
        // ============================================================================
        [Test]
        public void BigGranularDump_SpikesThenCrashes_WhileSpoonFedFoliar_StaysSteady()
        {
            const int days = 24;
            var dump = RunClipCurve(seed: 42, days, GranularDumpPlan);
            var spoon = RunClipCurve(seed: 42, days, FoliarSpoonPlan);

            double dumpPeak = Max(dump.Clip), spoonPeak = Max(spoon.Clip);
            double dumpEnd = dump.Clip[days - 1], dumpSwing = dumpPeak - dumpEnd;
            double spoonSwing = Max(spoon.Clip) - Min(spoon.Clip);

            TestContext.WriteLine($"GRANULAR peak={dumpPeak:F3} end={dumpEnd:F3} carb={dump.FinalCarb:F1}");
            TestContext.WriteLine($"FOLIAR   peak={spoonPeak:F3} swing={spoonSwing:F3} carb={spoon.FinalCarb:F1}");

            // The dump SPIKES higher than the steady foliar feed...
            Assert.Greater(dumpPeak, spoonPeak * 1.15, "the soluble dump must spike growth above the spoon feed");
            // ...then CRASHES well off its own peak...
            Assert.Less(dumpEnd, dumpPeak * 0.7, "the dump must crash off its peak (spike then crash)");
            // ...while the foliar feed stays comparatively flat.
            Assert.Less(spoonSwing, dumpSwing, "spoon-fed foliar must produce a steadier curve than the dump");

            // And it BURNS more: a single big soluble dump scorches turf where the same N, spoon-fed
            // through the leaf, does not. (Isolated from growth re-greening by applying it directly.)
            var dumped = FreshGreen(); FertilitySystem.Apply(dumped, FertApplication.GranularQuick(60.0), T);
            var fed = FreshGreen();
            for (int i = 0; i < 12; i++) FertilitySystem.Apply(fed, FertApplication.FoliarSpoon(5.0), T);
            TestContext.WriteLine($"burn: granular dump dens={dumped.DensityPct:F2}, spoon-fed dens={fed.DensityPct:F2}");
            Assert.Less(dumped.DensityPct, fed.DensityPct - 1.0, "the soluble granular dump must burn more than foliar");
        }

        // ============================================================================
        // (c) HIGH N / LOW K survives heat + wear WORSE than balanced N:K. Run through a
        //     heat wave where growth (cool-season) can't re-green over the stress.
        // ============================================================================
        [Test]
        public void HighNLowK_SurvivesHeatWorse_ThanBalancedNK()
        {
            var fragile = FreshGreen(18.0);  // high N, low K
            var balanced = FreshGreen(18.0); // high N, K kept up to match
            var heatWave = new WeatherDay { TminF = 80, TmaxF = 104, LeafWetnessHrs = 0, SolarRa = 16, Humidity = 0.3 };
            double gdd = Mathx.Max0(heatWave.TmeanF - T.GddBaseF);

            for (int d = 0; d < 25; d++)
            {
                // Hold the nutrient scenario fixed each day so the ONLY difference is potassium.
                fragile.NitrogenPct = 80; fragile.PotassiumPct = 40;
                balanced.NitrogenPct = 80; balanced.PotassiumPct = 85;
                Growth.Apply(fragile, heatWave, gdd, 0.0, T, G);
                Growth.Apply(balanced, heatWave, gdd, 0.0, T, G);
            }

            TestContext.WriteLine($"after heat wave: high-N/low-K dens={fragile.DensityPct:F1}, balanced N:K dens={balanced.DensityPct:F1}");
            Assert.Less(fragile.DensityPct, balanced.DensityPct - 5.0,
                        "high-N/low-K turf must thin far more under heat than balanced N:K");
        }

        // ============================================================================
        // (d) IRON greens up with NO growth surge; GRANULAR on hot/dry/shallow uptakes poorly vs FOLIAR.
        // ============================================================================
        [Test]
        public void Iron_RaisesColour_WithNoGrowthSurge()
        {
            var plain = FreshGreen();
            var ironed = plain.Clone();

            double nBefore = ironed.NitrogenPct, densBefore = ironed.DensityPct;
            FertilitySystem.Apply(ironed, FertApplication.IronOnly(30.0), T);

            // Iron touches NEITHER growth driver — no surge, just colour.
            Assert.AreEqual(nBefore, ironed.NitrogenPct, 1e-9, "iron must not change available nitrogen");
            Assert.AreEqual(densBefore, ironed.DensityPct, 1e-9, "iron must not change density (no growth/burn)");
            Assert.Greater(ironed.IronPct, 0.0, "iron lands in its own pool");

            var plainColor = LegibilityMapping.ForCell(plain, 0, T).BaseColor;
            var ironColor = LegibilityMapping.ForCell(ironed, 0, T).BaseColor;
            Assert.Less(ironColor.R, plainColor.R - 1e-4, "iron must read greener (lower red) than the unfed turf");
            Assert.Less(ironColor.G, plainColor.G, "and deeper green overall");
        }

        [Test]
        public void GranularUptake_IsPoorOnHotDryShallowSoil_WhereFoliarStillFeeds()
        {
            var baseGreen = FreshGreen();
            baseGreen.SoilTempF = 95.0;       // too hot for cool-season uptake
            baseGreen.SoilMoisturePct = 7.0;  // too dry to move nutrients
            baseGreen.RootDepthIn = 1.5;      // shallow — can't reach soil N

            var granular = baseGreen.Clone();
            var foliar = baseGreen.Clone();
            double n0 = baseGreen.NitrogenPct;

            FertilitySystem.Apply(granular, FertApplication.GranularQuick(20.0), T);
            FertilitySystem.Apply(foliar, FertApplication.FoliarSpoon(20.0), T);

            double granularGain = granular.NitrogenPct - n0;
            double foliarGain = foliar.NitrogenPct - n0;
            TestContext.WriteLine($"hot/dry/shallow uptake: granular +{granularGain:F2} N, foliar +{foliarGain:F2} N");

            Assert.Less(granularGain, foliarGain * 0.5, "granular uptake must be poor on hot/dry/shallow soil");
            Assert.Greater(foliarGain, 5.0, "foliar still feeds the leaf regardless of the soil");
        }

        // ---- season harness ----

        private sealed class SeasonResult
        {
            public double MaxN, FinalRoot, FinalOm, FinalDensity;
            public double MaxHighNFavor, EarlyColorR, CarbDay5;
            public ZoneState Green; // end-state snapshot, for the post-season heat-wave survival check
        }

        private static SeasonResult RunSeason(int seed, int days, System.Func<CourseState, int, DayPlan> plan)
        {
            var dir = SummerDir(seed, out var course);
            var r = new SeasonResult { EarlyColorR = double.NaN, CarbDay5 = double.NaN };
            for (int d = 0; d < days; d++)
            {
                var res = dir.ResolveDay(plan(course, d));
                var g = course.Get("green-01");
                if (g.NitrogenPct > r.MaxN) r.MaxN = g.NitrogenPct;
                if (res.HighNDiseaseFavorability.TryGetValue(g.Id, out var f) && f > r.MaxHighNFavor)
                    r.MaxHighNFavor = f;
                if (d == 5) r.CarbDay5 = g.CarbReservesPct;
                if (d == 10) r.EarlyColorR = LegibilityMapping.ForCell(g, 0, T).BaseColor.R;
            }
            var z = course.Get("green-01");
            r.MaxN = System.Math.Max(r.MaxN, z.NitrogenPct);
            r.FinalRoot = z.RootDepthIn; r.FinalOm = z.OrganicMatterPct; r.FinalDensity = z.DensityPct;
            r.Green = z.Clone();
            return r;
        }

        /// <summary>Subject an end-of-season green to an identical 15-day heat wave; return surviving density.</summary>
        private static double SurviveHeatWave(ZoneState green)
        {
            var z = green.Clone();
            // A punishing wave (Tmean 95F) — too hot for cool-season turf to re-green over, so survival
            // is about the ROOTS and reserves built up beforehand, not new growth.
            var heatWave = new WeatherDay { TminF = 80, TmaxF = 110, LeafWetnessHrs = 0, SolarRa = 16, Humidity = 0.3 };
            double gdd = Mathx.Max0(heatWave.TmeanF - T.GddBaseF);
            for (int d = 0; d < 15; d++) Growth.Apply(z, heatWave, gdd, 0.0, T, G);
            return z.DensityPct;
        }

        private sealed class ClipCurve
        {
            public double[] Clip;
            public double FinalDensity, FinalCarb;
        }

        private static ClipCurve RunClipCurve(int seed, int days, System.Func<CourseState, int, DayPlan> plan)
        {
            var dir = SummerDir(seed, out var course);
            var clip = new double[days];
            for (int d = 0; d < days; d++)
            {
                dir.ResolveDay(plan(course, d));
                clip[d] = course.Get("green-01").ClipVolume; // not mown in these plans => the day's growth clip
            }
            var z = course.Get("green-01");
            return new ClipCurve { Clip = clip, FinalDensity = z.DensityPct, FinalCarb = z.CarbReservesPct };
        }

        // ---- plans (all spray + irrigate equally; the difference is purely the fertility program) ----

        private static DayPlan OverNPlan(CourseState course, int day)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None; a.IrrigationMm = 10.0; a.Spray = true;
                if (z.NitrogenPct < 78.0) a.Fert = FertApplication.GranularQuick(30.0); // hammer N, no K
                plan.Set(z.Id, a);
            }
            return plan;
        }

        private static DayPlan InBandPlan(CourseState course, int day)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None; a.IrrigationMm = 10.0; a.Spray = true;
                if (z.NitrogenPct < 42.0) a.Fert = FertApplication.GranularSlow(10.0, 6.0); // gentle, in band, K matched
                plan.Set(z.Id, a);
            }
            return plan;
        }

        private static DayPlan GranularDumpPlan(CourseState course, int day)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None; a.IrrigationMm = 8.0;
                if (day == 0) a.Fert = FertApplication.GranularQuick(60.0); // one big soluble dump
                plan.Set(z.Id, a);
            }
            return plan;
        }

        private static DayPlan FoliarSpoonPlan(CourseState course, int day)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None; a.IrrigationMm = 8.0;
                if (day % 2 == 0) a.Fert = FertApplication.FoliarSpoon(5.0); // ~equivalent total N, spoon-fed
                plan.Set(z.Id, a);
            }
            return plan;
        }

        private static double Max(double[] xs)
        {
            double m = double.NegativeInfinity;
            for (int i = 0; i < xs.Length; i++) if (xs[i] > m) m = xs[i];
            return m;
        }

        private static double Min(double[] xs)
        {
            double m = double.PositiveInfinity;
            for (int i = 0; i < xs.Length; i++) if (xs[i] < m) m = xs[i];
            return m;
        }
    }
}
