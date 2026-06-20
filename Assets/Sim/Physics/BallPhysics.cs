// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Physics
{
    /// <summary>A putt input: power in [0,1], a unit aim direction, and the local slope gradient.</summary>
    public struct PuttInput
    {
        public double Power01;
        public Vec2 Aim;        // unit direction the player aims
        public Vec2 SlopeGrad;  // downhill gradient on the green plane (magnitude = rise/run)

        public PuttInput(double power, Vec2 aim, Vec2 slopeGrad)
        {
            Power01 = power; Aim = aim.Normalized(); SlopeGrad = slopeGrad;
        }
    }

    public struct PuttResult
    {
        public double RollDistanceFt;   // distance travelled along the (breaking) path
        public double BreakFt;          // total lateral deflection from aim
        public Vec2 Displacement;       // net 2D displacement from the ball's start
        public double RollSeconds;      // for animation only
    }

    /// <summary>An approach input: power in [0,1] and aim. Lands on the target green.</summary>
    public struct ApproachInput
    {
        public double Power01;
        public Vec2 Aim;
        public ApproachInput(double power, Vec2 aim) { Power01 = power; Aim = aim.Normalized(); }
    }

    public struct ApproachResult
    {
        public double CarryFt;
        public double FirstBounceFt;
        public double ReleaseRollFt;   // roll-out after landing (firm greens release; soft greens check)
        public double TotalFt;
    }

    /// <summary>
    /// The ball reads the course (TDD §7). All putt/approach behaviour is a deterministic function of
    /// the green's MAINTAINED state — Stimp sets roll distance, firmness sets release, the mesh slope
    /// sets break. There is no separate physics tuning to fiddle: the depth lives in the green you made.
    /// Unity only renders the path this returns.
    /// </summary>
    public static class BallPhysics
    {
        public static PuttResult SolvePutt(ZoneState green, PuttInput input, AgronomyTuning t)
        {
            t = t ?? AgronomyTuning.Default;
            double power = Mathx.Clamp01(input.Power01);
            Vec2 aim = input.Aim;

            // Base roll is set by the green's SPEED (Stimp), which the player dialed in via maintenance.
            double roll = green.Stimp * t.PuttRollFeetPerStimp * power;

            // Along-aim slope lengthens (downhill) or shortens (uphill) the roll.
            double alongSlope = input.SlopeGrad.Dot(aim); // >0 aiming downhill
            roll *= 1.0 + alongSlope * t.PuttSlopeRollFactor;
            roll = Mathx.Max0(roll);

            // Cross-slope component curves the ball (break), scaled by how far it rolls.
            Vec2 cross = input.SlopeGrad - aim * alongSlope; // perpendicular part of the gradient
            Vec2 breakVec = cross * (roll * t.PuttBreakFactor);
            double breakFt = breakVec.Length;

            Vec2 displacement = aim * roll + breakVec;

            return new PuttResult
            {
                RollDistanceFt = roll,
                BreakFt = breakFt,
                Displacement = displacement,
                RollSeconds = t.PuttAvgSpeedFps > 0 ? displacement.Length / t.PuttAvgSpeedFps : 0,
            };
        }

        public static ApproachResult SolveApproach(ZoneState green, ApproachInput input, AgronomyTuning t)
        {
            t = t ?? AgronomyTuning.Default;
            double power = Mathx.Clamp01(input.Power01);

            double carry = t.ApproachCarryFeetFull * power;
            double firmNorm = Mathx.Clamp01(green.FirmnessPct / 100.0); // firmness already folds in moisture+OM
            double bounce = firmNorm * t.ApproachBounceMaxFt;
            double release = t.ApproachReleaseBaseFt + firmNorm * t.ApproachReleaseFirmFactorFt;

            return new ApproachResult
            {
                CarryFt = carry,
                FirstBounceFt = bounce,
                ReleaseRollFt = release,
                TotalFt = carry + release,
            };
        }
    }
}
