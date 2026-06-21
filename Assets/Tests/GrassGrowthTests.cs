// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.Physics;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>
    /// Grass LENGTH: it grows when you don't mow (faster in the growing seasons), mowing resets it,
    /// and length has playability consequences — greens slow down, fairways stop running out, and the
    /// rough goes from a fun challenge to unplayable.
    /// </summary>
    [TestFixture]
    public class GrassGrowthTests
    {
        private static readonly AgronomyTuning T = AgronomyTuning.Default;
        private static readonly GrassProfile G = GrassProfile.Mvp();

        private static GameDirector Dir(int seed, int startDay, out CourseState course)
        {
            var cfg = CourseConfig.GreensOnly(1);
            course = CourseFactory.Build(cfg, seed);
            var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass) { WeatherInterruptsEnabled = false };
            dir.Clock.JumpTo(startDay);
            return dir;
        }

        private static DayPlan NoMow(CourseState c)
        {
            var p = new DayPlan();
            foreach (var z in c.Greens) { var a = ZoneAction.None; a.IrrigationMm = 8; a.FertilizerN = z.NitrogenPct < 40 ? 6 : 0; p.Set(z.Id, a); }
            return p;
        }

        [Test]
        public void GrassGrowsWhenUnmown_AndMowingResetsIt()
        {
            var dir = Dir(7, 75, out var course); // spring
            var g = course.Get("green-01");
            double start = g.GrassHeightIn;
            for (int i = 0; i < 20; i++) dir.ResolveDay(NoMow(course));
            Assert.Greater(g.GrassHeightIn, start + 0.2, "unmown grass must grow noticeably over three weeks");

            var mow = new DayPlan();
            mow.Set("green-01", new ZoneAction { Mow = true, MowHeightIn = 0.125, Quality = 1.0 });
            dir.ResolveDay(mow);
            Assert.AreEqual(0.125, g.GrassHeightIn, 1e-6, "mowing cuts the blade length back to the set height");
        }

        [Test]
        public void GrowsFasterInSpring_ThanInMidsummerHeat()
        {
            var spring = Dir(7, 60, out var cSpring);   // cool-season growing weather
            var summer = Dir(7, 110, out var cSummer);  // peak heat — cool-season growth stalls
            for (int i = 0; i < 21; i++) { spring.ResolveDay(NoMow(cSpring)); summer.ResolveDay(NoMow(cSummer)); }

            double springLen = cSpring.Get("green-01").GrassHeightIn;
            double summerLen = cSummer.Get("green-01").GrassHeightIn;
            TestContext.WriteLine($"3-week unmown length: spring {springLen:F2}\"  summer {summerLen:F2}\"");
            Assert.Greater(springLen, summerLen, "cool-season grass grows faster in spring than in midsummer heat");
        }

        [Test]
        public void UnmownGreen_GetsSlower()
        {
            var dir = Dir(7, 70, out var course);
            var g = course.Get("green-01");
            var mow = new DayPlan(); mow.Set("green-01", new ZoneAction { Mow = true, MowHeightIn = 0.125, Quality = 1.0 });
            dir.ResolveDay(mow);
            double fastStimp = g.Stimp;
            for (int i = 0; i < 18; i++) dir.ResolveDay(NoMow(course));
            TestContext.WriteLine($"green speed: freshly mown {fastStimp:F1} -> unmown {g.Stimp:F1}");
            Assert.Less(g.Stimp, fastStimp - 0.5, "a green left unmown loses green speed (Stimp)");
        }

        [Test]
        public void Rough_GoesFromChallenge_ToUnplayable_AsItGrows()
        {
            var rough = CourseFactory.Build(CourseConfig.Mvp(), 1).Get("rough-01");

            rough.GrassHeightIn = rough.Surface.MowHeightIn;            // freshly cut
            var cut = BallPhysics.SolveLie(rough, T);
            rough.GrassHeightIn = rough.Surface.MowHeightIn + 1.5;      // grown — a challenge
            var challenge = BallPhysics.SolveLie(rough, T);
            rough.GrassHeightIn = rough.Surface.MowHeightIn + 4.0;      // deep — unplayable
            var deep = BallPhysics.SolveLie(rough, T);

            TestContext.WriteLine($"rough distance factor: cut {cut.DistanceFactor:F2} -> challenge {challenge.DistanceFactor:F2} -> deep {deep.DistanceFactor:F2} (buried={deep.Buried})");
            Assert.Greater(cut.DistanceFactor, challenge.DistanceFactor, "longer rough costs more distance");
            Assert.Greater(challenge.DistanceFactor, deep.DistanceFactor, "deeper rough costs even more");
            Assert.IsTrue(deep.Buried, "deep rough buries the ball (barely playable)");
        }

        [Test]
        public void UnmownFairway_StopsRunningOut()
        {
            var fw = CourseFactory.Build(CourseConfig.Mvp(), 1).Get("fairway-01");
            fw.FirmnessPct = 80;
            fw.GrassHeightIn = fw.Surface.MowHeightIn;        // tight
            double tightRoll = BallPhysics.SolveLie(fw, T).RollOutFt;
            fw.GrassHeightIn = fw.Surface.MowHeightIn + 2.0;  // shaggy
            double shaggyRoll = BallPhysics.SolveLie(fw, T).RollOutFt;
            TestContext.WriteLine($"fairway roll-out: tight {tightRoll:F1}ft -> shaggy {shaggyRoll:F1}ft");
            Assert.Less(shaggyRoll, tightRoll - 5.0, "an unmown fairway no longer runs the ball out");
        }
    }
}
