// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Config
{
    /// <summary>
    /// Per-SurfaceType profile (TDD §2 / §5): instances the ONE agronomy model across every surface at
    /// the right fidelity and tuning. Same ZoneState + pipeline — only these coefficients and the
    /// healthy-start ranges differ. Greens reproduce the legacy constants EXACTLY (susceptibility 1.0,
    /// recuperation 1.0, zero traffic wear), so green behaviour is byte-identical to before.
    ///
    /// Multipliers are relative to the green baseline:
    ///   • DiseaseSusceptibility / GrowthRecuperation scale the disease + growth math per surface.
    ///   • TrafficWearPerDay is extra daily density loss (tee divots/foot traffic highest, rough lowest).
    ///   • ConditionWeight is the surface's share of course condition (visibility/playability).
    /// </summary>
    public sealed class SurfaceProfile
    {
        public ZoneType Type;
        public string GrassName = "Creeping Bentgrass";
        public bool IsTurf = true;
        public int GridSize = 1;          // simulation resolution: greens 3 (9-cell), everyone else 1
        public double MowHeightIn = 0.50;

        // Healthy-start ranges (the §5 contract, per surface).
        public double MoistureMin = 18.0, MoistureMax = 24.0;
        public double DensityMin = 80.0, DensityMax = 88.0;
        public double RootDepthIn = 5.0;
        public double OmMin = 35.0, OmMax = 42.0;
        public double DebtMin = 8.0, DebtMax = 12.0;

        // Per-surface agronomy multipliers (green baseline = 1.0 / 0.0).
        public double DiseaseSusceptibility = 1.0;
        public double GrowthRecuperation = 1.0;
        public double TrafficWearPerDay = 0.0;   // extra daily density loss (divots/foot/cart traffic)

        // Condition roll-up: this surface's weight in the course-condition aggregate.
        public double ConditionWeight = 1.0;

        // Bunker (non-turf) start: sand consistency 0..100 (clean firm sand = high; washed/settled = low).
        public double SandQualityStart = 0.0;

        /// <summary>The canonical MVP profile for a surface (the §5 per-surface tuning table).</summary>
        public static SurfaceProfile For(ZoneType type)
        {
            switch (type)
            {
                case ZoneType.Green:
                    // EXACT legacy greens (must match CourseFactory.Healthy* + neutral multipliers).
                    return new SurfaceProfile
                    {
                        Type = type, GrassName = "Creeping Bentgrass (greens)", GridSize = 3, MowHeightIn = 0.125,
                        MoistureMin = 18.0, MoistureMax = 22.0, DensityMin = 88.0, DensityMax = 92.0,
                        RootDepthIn = 6.0, OmMin = 33.0, OmMax = 37.0, DebtMin = 8.0, DebtMax = 12.0,
                        DiseaseSusceptibility = 1.0, GrowthRecuperation = 1.0, TrafficWearPerDay = 0.0,
                        ConditionWeight = 5.0,
                    };
                case ZoneType.Approach:
                    return new SurfaceProfile
                    {
                        Type = type, GrassName = "Bentgrass (approach)", GridSize = 1, MowHeightIn = 0.30,
                        MoistureMin = 18.0, MoistureMax = 24.0, DensityMin = 82.0, DensityMax = 88.0,
                        RootDepthIn = 5.0, OmMin = 35.0, OmMax = 40.0, DebtMin = 8.0, DebtMax = 12.0,
                        DiseaseSusceptibility = 0.85, GrowthRecuperation = 1.0, TrafficWearPerDay = 0.10,
                        ConditionWeight = 2.0,
                    };
                case ZoneType.Fairway:
                    return new SurfaceProfile
                    {
                        Type = type, GrassName = "Bentgrass/Poa (fairway)", GridSize = 1, MowHeightIn = 0.50,
                        MoistureMin = 18.0, MoistureMax = 26.0, DensityMin = 80.0, DensityMax = 88.0,
                        RootDepthIn = 5.0, OmMin = 38.0, OmMax = 44.0, DebtMin = 8.0, DebtMax = 12.0,
                        DiseaseSusceptibility = 0.7, GrowthRecuperation = 1.0, TrafficWearPerDay = 0.15,
                        ConditionWeight = 2.5,
                    };
                case ZoneType.Tee:
                    return new SurfaceProfile
                    {
                        Type = type, GrassName = "Bentgrass (tee)", GridSize = 1, MowHeightIn = 0.40,
                        MoistureMin = 18.0, MoistureMax = 24.0, DensityMin = 80.0, DensityMax = 88.0,
                        RootDepthIn = 4.5, OmMin = 35.0, OmMax = 42.0, DebtMin = 8.0, DebtMax = 14.0,
                        DiseaseSusceptibility = 0.8, GrowthRecuperation = 1.0, TrafficWearPerDay = 0.45, // divots + foot traffic
                        ConditionWeight = 1.2,
                    };
                case ZoneType.Rough:
                    return new SurfaceProfile
                    {
                        Type = type, GrassName = "Fescue/Rye (rough)", GridSize = 1, MowHeightIn = 2.5,
                        MoistureMin = 14.0, MoistureMax = 26.0, DensityMin = 60.0, DensityMax = 75.0,
                        RootDepthIn = 4.0, OmMin = 40.0, OmMax = 50.0, DebtMin = 8.0, DebtMax = 15.0,
                        DiseaseSusceptibility = 0.5, GrowthRecuperation = 0.8, TrafficWearPerDay = 0.05, // low input
                        ConditionWeight = 1.0,
                    };
                default: // Bunker — non-turf
                    return new SurfaceProfile
                    {
                        Type = ZoneType.Bunker, GrassName = "(sand)", IsTurf = false, GridSize = 1, MowHeightIn = 0.0,
                        MoistureMin = 4.0, MoistureMax = 8.0, DensityMin = 0.0, DensityMax = 0.0,
                        RootDepthIn = 0.0, OmMin = 0.0, OmMax = 0.0, DebtMin = 0.0, DebtMax = 0.0,
                        DiseaseSusceptibility = 0.0, GrowthRecuperation = 0.0, TrafficWearPerDay = 0.0,
                        ConditionWeight = 0.8, SandQualityStart = 85.0,
                    };
            }
        }
    }
}
