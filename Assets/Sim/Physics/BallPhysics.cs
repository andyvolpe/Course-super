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

    /// <summary>The lie a shot finds on a surface (TDD §7): the miss is penalised by the surface YOU keep.</summary>
    public struct LieResult
    {
        public ZoneType Surface;
        public double DistanceFactor;  // multiplier on the next shot's distance (1 = clean strike)
        public double RollOutFt;       // run-out after landing (firm fairway runs, soft holds)
        public bool Flier;             // grass between club and ball — unpredictable jumper
        public bool Buried;            // ball sat down — a recovery, big distance loss
        public string Quality;         // short human-readable label
    }

    /// <summary>
    /// The ball reads the course (TDD §7). All putt/approach behaviour is a deterministic function of
    /// the green's MAINTAINED state — Stimp sets roll distance, firmness sets release, the mesh slope
    /// sets break. There is no separate physics tuning to fiddle: the depth lives in the green you made.
    /// Unity only renders the path this returns.
    /// </summary>
    public static class BallPhysics
    {
        /// <summary>
        /// Resolve the lie on whatever surface the ball came to rest on. Fairway FIRMNESS sets run-out,
        /// rough DENSITY sets the flier/buried penalty, and bunker SAND CONSISTENCY sets clean-vs-buried —
        /// every input is something the superintendent maintains.
        /// </summary>
        public static LieResult SolveLie(ZoneState surface, AgronomyTuning t)
        {
            t = t ?? AgronomyTuning.Default;
            var r = new LieResult { Surface = surface.Type, DistanceFactor = 1.0, Quality = "clean" };

            switch (surface.Type)
            {
                case ZoneType.Fairway:
                case ZoneType.Approach:
                case ZoneType.Tee:
                {
                    double firmNorm = Mathx.Clamp01(surface.FirmnessPct / 100.0);
                    // Un-mown fairway grass kills roll and grabs the club a little.
                    double cut = surface.Surface != null ? surface.Surface.MowHeightIn : surface.MowHeightIn;
                    double overgrow = Mathx.Clamp01(Mathx.Max0(surface.GrassHeightIn - cut) / t.FairwayLongGrassRangeIn);
                    r.RollOutFt = firmNorm * t.FairwayRollOutMaxFt * (1.0 - overgrow);
                    r.DistanceFactor = 1.0 - 0.25 * overgrow;
                    r.Quality = overgrow > 0.5 ? "shaggy lie (needs mowing)"
                              : firmNorm > 0.6 ? "clean, firm (runs out)" : "clean, receptive";
                    break;
                }
                case ZoneType.Rough:
                {
                    double densNorm = Mathx.Clamp01(surface.DensityPct / 100.0);
                    // Length is the real story in rough: a fun challenge, then unplayable when it gets deep.
                    double cut = surface.Surface != null ? surface.Surface.MowHeightIn : 2.5;
                    double lenNorm = Mathx.Clamp01(Mathx.Max0(surface.GrassHeightIn - cut) / t.RoughChallengeRangeIn);
                    double severity = System.Math.Max(densNorm * t.RoughDistancePenalty, lenNorm);
                    r.DistanceFactor = Mathx.Clamp(1.0 - severity, 0.15, 1.0); // floor: you can always hack it out
                    r.Buried = lenNorm >= 0.95 || densNorm >= t.RoughBuriedDensity; // deep rough = buried/unplayable
                    r.Flier = !r.Buried && (lenNorm >= 0.4 || densNorm >= t.RoughFlierDensityMin);
                    r.Quality = r.Buried ? "buried — barely playable" : (r.Flier ? "flier lie" : "light rough");
                    break;
                }
                case ZoneType.Bunker:
                {
                    double q = Mathx.Clamp01(surface.SandQualityPct / 100.0);
                    if (surface.WashedOut) q *= 0.5;
                    r.DistanceFactor = Mathx.Clamp01(t.BunkerCleanDistanceBase + t.BunkerCleanDistanceSpan * q);
                    r.Buried = surface.WashedOut || q < t.BunkerBuriedQuality;
                    r.Quality = r.Buried ? "plugged/poor sand" : "clean sand";
                    break;
                }
                default: // Green — putting surface; a clean lie by definition
                    r.Quality = "on the green";
                    break;
            }
            return r;
        }

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
