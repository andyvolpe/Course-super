// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.Physics;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Step A+.1 — the ball reads the green's maintained state (TDD §7).</summary>
    [TestFixture]
    public class BallPhysicsTests
    {
        private static readonly AgronomyTuning T = AgronomyTuning.Default;

        private static Greenkeeper.Sim.State.ZoneState Green()
            => CourseFactory.Build(CourseConfig.GreensOnly(1), 1).Get("green-01");

        [Test]
        public void FasterGreen_RollsFartherForIdenticalPower()
        {
            var slow = Green(); slow.Stimp = 9.0;
            var fast = Green(); fast.Stimp = 12.0;
            var input = new PuttInput(power: 0.8, aim: new Vec2(1, 0), slopeGrad: Vec2.Zero);

            double slowRoll = BallPhysics.SolvePutt(slow, input, T).RollDistanceFt;
            double fastRoll = BallPhysics.SolvePutt(fast, input, T).RollDistanceFt;

            Assert.Greater(fastRoll, slowRoll, "a faster (higher-Stimp) green must roll farther for the same power");
            // The ratio tracks the Stimp ratio exactly (roll is a pure function of green speed).
            Assert.AreEqual(12.0 / 9.0, fastRoll / slowRoll, 1e-9);
        }

        [Test]
        public void FirmerGreen_ReleasesMoreOnAnApproach()
        {
            var soft = Green(); soft.FirmnessPct = 30.0;  // wet/soft -> checks up
            var firm = Green(); firm.FirmnessPct = 85.0;  // firm -> releases
            var input = new ApproachInput(power: 0.7, aim: new Vec2(1, 0));

            var softR = BallPhysics.SolveApproach(soft, input, T);
            var firmR = BallPhysics.SolveApproach(firm, input, T);

            Assert.AreEqual(softR.CarryFt, firmR.CarryFt, 1e-9, "same power carries the same distance in the air");
            Assert.Greater(firmR.ReleaseRollFt, softR.ReleaseRollFt, "a firmer green releases more after landing");
            Assert.Greater(firmR.TotalFt, softR.TotalFt, "so the firm green plays longer overall");
            Assert.Greater(firmR.FirstBounceFt, softR.FirstBounceFt, "and bounces higher");
        }

        [Test]
        public void Slope_BreaksThePutt_AndDownhillRollsLonger()
        {
            var g = Green(); g.Stimp = 11.0;
            var aim = new Vec2(1, 0);

            // Cross slope (downhill to the +Y side) curves the ball off the aim line.
            var crossSlope = new PuttInput(0.7, aim, new Vec2(0, 0.1));
            var flat = new PuttInput(0.7, aim, Vec2.Zero);
            Assert.Greater(BallPhysics.SolvePutt(g, crossSlope, T).BreakFt, BallPhysics.SolvePutt(g, flat, T).BreakFt,
                "cross-slope must break the putt");

            // Downhill (slope along the aim) rolls farther than flat than uphill.
            double downhill = BallPhysics.SolvePutt(g, new PuttInput(0.7, aim, new Vec2(0.1, 0)), T).RollDistanceFt;
            double level = BallPhysics.SolvePutt(g, flat, T).RollDistanceFt;
            double uphill = BallPhysics.SolvePutt(g, new PuttInput(0.7, aim, new Vec2(-0.1, 0)), T).RollDistanceFt;
            Assert.Greater(downhill, level);
            Assert.Greater(level, uphill);
        }

        [Test]
        public void ZeroPower_DoesNotMove()
        {
            var g = Green();
            var r = BallPhysics.SolvePutt(g, new PuttInput(0, new Vec2(1, 0), Vec2.Zero), T);
            Assert.AreEqual(0.0, r.RollDistanceFt, 1e-9);
        }
    }
}
