// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine, DateTime.Now, or frame time.
using System;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Owns the clock and the daily resolve pipeline (TDD §3). Fully deterministic: construct with a
    /// seed and the outcome is a pure function of (seed, plans). The pipeline runs its steps IN ORDER;
    /// each step is a real system call, logged by name into the DayResult.
    ///
    /// Pipeline order (per zone, zones independent except intra-green diffusion):
    ///   1 Weather (course-wide, once)        5 Growth / clip / carb reserves
    ///   2 Inputs (fertilizer)                6 Organic matter + grain
    ///   3 Water balance                      7 Disease (favorability/pressure/spread/expression)
    ///   4 Soil temp + GDD                    8 Maintenance (mow/roll/spray/aerate)
    ///                                        9 Turf debt   10 Derived surfaces   11 Tick counters
    /// </summary>
    public sealed class GameDirector
    {
        public GameClock Clock { get; } = new GameClock();
        public CourseState Course { get; }

        private readonly WeatherSystem _weather;
        private readonly AgronomyTuning _tuning;
        private readonly GrassProfile _grass;
        private readonly int _seed;

        /// <summary>TEETH SWITCH for the fairness keystone (T4). Always false in normal play.</summary>
        public bool BypassFairnessGate;

        /// <summary>Raised by an interrupt source to stop a skip loop (stub: stays false by default).</summary>
        public bool InterruptRaised { get; private set; }
        public void RaiseInterrupt() => InterruptRaised = true;
        public void ClearInterrupt() => InterruptRaised = false;

        public GameDirector(CourseState course, int seed,
                            AgronomyTuning tuning = null, GrassProfile grass = null)
        {
            Course = course;
            _seed = seed;
            _weather = new WeatherSystem(seed);
            _tuning = tuning ?? AgronomyTuning.Default;
            _grass = grass ?? GrassProfile.Mvp();
        }

        /// <summary>Resolve one day with a no-op plan.</summary>
        public DayResult ResolveDay() => ResolveDay(new DayPlan());

        /// <summary>Resolve a day from a maintenance window (its accepted tasks become the morning plan).</summary>
        public DayResult ResolveWindow(Crew.MaintenanceWindow window, System.Func<Crew.TaskOrder, double> qualityResolver = null)
            => ResolveDay(window.ToDayPlan(Course, qualityResolver));

        /// <summary>Resolve one day with the supplied morning plan, then advance the clock.</summary>
        public DayResult ResolveDay(DayPlan plan)
        {
            var result = new DayResult
            {
                DayIndex = Clock.DayIndex,
                Season = Clock.Season,
            };

            // Step 1 — Weather (deterministic from seed + day).
            result.Weather = _weather.Generate(Clock.DayIndex);
            result.Step($"weather: Tmin={result.Weather.TminF:F1} Tmax={result.Weather.TmaxF:F1} " +
                        $"rain={result.Weather.RainMm:F1}mm wet={result.Weather.LeafWetnessHrs:F1}h");

            var ctx = new ResolveContext
            {
                DayIndex = Clock.DayIndex,
                Weather = result.Weather,
                Tuning = _tuning,
                Grass = _grass,
                Result = result,
                BypassFairnessGate = BypassFairnessGate,
            };

            // Per-day RNG stream, forked deterministically per zone.
            var dayRng = new Rng((ulong)unchecked((long)_seed * 2654435761L + Clock.DayIndex + 1));

            for (int i = 0; i < Course.Zones.Count; i++)
            {
                var z = Course.Zones[i];
                var action = plan.For(z.Id);
                ctx.Rng = dayRng;
                var zoneRng = dayRng.Fork((ulong)(i + 1));

                MaintenanceSystem.ApplyInputs(z, action, _tuning);                  // 2 inputs
                var water = WaterBalance.Apply(z, result.Weather, action.IrrigationMm, _tuning); // 3 water
                double gdd = SoilThermal.Apply(z, result.Weather, _tuning);          // 4 soil temp + GDD
                Growth.Apply(z, result.Weather, gdd, water.DrainageVwc, _tuning, _grass); // 5 growth
                OrganicMatter.Apply(z, _tuning);                                     // 6 OM + grain
                DiseaseSystem.Apply(z, action, ctx, zoneRng);                        // 7 disease
                MaintenanceSystem.ApplyMechanical(z, action, _tuning);              // 8 maintenance
                TurfDebtSystem.Apply(z, action, ctx);                               // 9 turf debt
                DerivedSurfaces.Recompute(z, _tuning);                              // 10 derived surfaces
                MaintenanceSystem.TickCounters(z, action);                         // 11 counters
            }

            result.Step($"resolved {Course.Zones.Count} zones; expressions={result.Expressions.Count}");

            Clock.AdvanceDay();
            return result;
        }

        /// <summary>
        /// Resolve days until <paramref name="stop"/> returns true or an interrupt is raised
        /// (GDD §1 interruptible skip). Returns the number of days resolved. Guarded by maxDays.
        /// </summary>
        public int SkipUntil(Func<GameDirector, bool> stop, Func<int, DayPlan> planProvider = null, int maxDays = 100000)
        {
            int resolved = 0;
            while (resolved < maxDays)
            {
                if (stop != null && stop(this)) break;
                if (InterruptRaised) break;
                var plan = planProvider != null ? planProvider(Clock.DayIndex) : null;
                ResolveDay(plan ?? new DayPlan());
                resolved++;
                if (InterruptRaised) break;
            }
            return resolved;
        }
    }
}
