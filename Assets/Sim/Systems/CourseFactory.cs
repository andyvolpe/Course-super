// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Builds a live CourseState from a CourseConfig, initialised to the §5 "healthy start" values.
    /// Deterministic: all per-zone jitter comes from the injected seed.
    /// </summary>
    public static class CourseFactory
    {
        // §5 healthy-start ranges (the test 2.1 contract).
        public const double HealthyMoistureMin = 18.0, HealthyMoistureMax = 22.0;
        public const double HealthyDensityMin = 88.0, HealthyDensityMax = 92.0;
        public const double HealthyRootDepthIn = 6.0;
        public const double HealthyOmMin = 33.0, HealthyOmMax = 37.0;
        public const double HealthyDebtMin = 8.0, HealthyDebtMax = 12.0;

        public static CourseState Build(CourseConfig cfg, int seed)
        {
            var t = cfg.Tuning;
            var rngRoot = new Rng(seed);
            var course = new CourseState();
            ulong stream = 1;

            foreach (var spec in cfg.Zones)
            {
                var rng = rngRoot.Fork(stream++);
                int cellCount = spec.GridSize * spec.GridSize;
                var cells = new SubCell[cellCount];
                for (int i = 0; i < cellCount; i++) cells[i] = new SubCell();

                // Each surface is initialised to ITS profile's healthy ranges (the §5 per-surface table).
                var profile = SurfaceProfile.For(spec.Type);

                var z = new ZoneState
                {
                    Id = spec.Id,
                    Type = spec.Type,
                    Soil = spec.Soil,
                    HoleNumber = spec.HoleNumber,
                    GridSize = spec.GridSize,
                    Cells = cells,
                    Surface = profile,

                    SoilMoisturePct = rng.Range(profile.MoistureMin, profile.MoistureMax),
                    RootDepthIn = profile.RootDepthIn,
                    DensityPct = rng.Range(profile.DensityMin, profile.DensityMax),
                    OrganicMatterPct = rng.Range(profile.OmMin, profile.OmMax),
                    TurfDebtPct = rng.Range(profile.DebtMin, profile.DebtMax),
                    CarbReservesPct = t.CarbStartPct,
                    NitrogenPct = t.NitrogenStart,
                    PotassiumPct = t.PotassiumStart,
                    IronPct = t.IronStart,
                    SoilTempF = 58.0,
                    GddAccum = 0.0,
                    GrainPct = t.GrainStartPct,
                    MowHeightIn = profile.MowHeightIn,
                    SprayResidualDaysLeft = 0,
                    DaysSinceAeration = 0,
                    AerationRecoveryDaysLeft = 0,
                    RollBonus = 0.0,
                };

                // Bunkers are sand: no living-turf agronomy, but they DO carry a sand-consistency state.
                if (!profile.IsTurf)
                {
                    z.DensityPct = 0;
                    z.OrganicMatterPct = 0;
                    z.GrainPct = 0;
                    z.CarbReservesPct = 0;
                    z.NitrogenPct = 0;
                    z.PotassiumPct = 0;
                    z.RootDepthIn = 0;
                    z.SandQualityPct = profile.SandQualityStart;
                }

                // Seed sensible derived surfaces so day-0 reads are meaningful.
                DerivedSurfaces.Recompute(z, t);
                course.Add(z);
            }

            return course;
        }
    }
}
