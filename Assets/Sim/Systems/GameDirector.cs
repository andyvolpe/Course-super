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

        /// <summary>
        /// Whether extreme-weather events (heat spike / storm / frost / flash drought) fire interrupts
        /// and apply their effects. On in normal play; tests of the bare clock/skip/disease mechanics
        /// turn it off to isolate from the weather.
        /// </summary>
        public bool WeatherInterruptsEnabled = true;

        /// <summary>
        /// Optional economy: when set, each resolved day settles the books (condition -> demand ->
        /// revenue, minus the day's costs). Opt-in so headless determinism/agronomy tests are unaffected.
        /// </summary>
        public Greenkeeper.Sim.Economy.EconomyState Economy;
        public Greenkeeper.Sim.Economy.EconomyConfig EconomyConfig;

        /// <summary>Optional tournament ladder: graded on its day, payout applied to the economy. Opt-in.</summary>
        public Greenkeeper.Sim.Tournament.TournamentLadder Tournament;

        /// <summary>Raised by an interrupt source to stop a skip loop.</summary>
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

            // Frost: mowing or rolling frozen turf doesn't take cleanly AND damages it (bruised crowns,
            // shattered blades, tracking). A competent op waits for the frost to lift.
            bool frost = WeatherInterruptsEnabled && result.Weather.TminF < _tuning.FrostThresholdF;
            int frostDamaged = 0;

            for (int i = 0; i < Course.Zones.Count; i++)
            {
                var z = Course.Zones[i];
                var action = plan.For(z.Id);
                bool frostMow = frost && z.Type != ZoneType.Bunker && (action.Mow || action.Roll);
                if (frost) { action.Mow = false; action.Roll = false; } // the cut/roll doesn't take on frozen turf
                ctx.Rng = dayRng;
                var zoneRng = dayRng.Fork((ulong)(i + 1));

                MaintenanceSystem.ApplyInputs(z, action, _tuning);                  // 2 inputs
                var water = WaterBalance.Apply(z, result.Weather, action.IrrigationMm, _tuning); // 3 water
                double gdd = SoilThermal.Apply(z, result.Weather, _tuning);          // 4 soil temp + GDD
                Growth.Apply(z, result.Weather, gdd, water.DrainageVwc, _tuning, _grass); // 5 growth
                OrganicMatter.Apply(z, _tuning, _grass);                             // 6 OM + grain
                DiseaseSystem.Apply(z, action, ctx, zoneRng);                        // 7 disease
                MaintenanceSystem.ApplyMechanical(z, action, _tuning);              // 8 maintenance
                MaintenanceSystem.ApplyBunkerSand(z, action, result.Weather, _tuning); // 8b bunker sand (non-turf)
                TurfDebtSystem.Apply(z, action, ctx);                               // 9 turf debt

                // Frost-mowing damage (applied before derived surfaces so speed/firmness reflect it).
                if (frostMow)
                {
                    z.DensityPct = Mathx.Clamp(z.DensityPct - _tuning.FrostMowDensityLoss, 0.0, 100.0);
                    z.TurfDebtPct = Mathx.Clamp(z.TurfDebtPct + _tuning.FrostMowDebt, 0.0, 100.0);
                    frostDamaged++;
                }

                DerivedSurfaces.Recompute(z, _tuning);                              // 10 derived surfaces
                MaintenanceSystem.TickCounters(z, action);                         // 11 counters
            }
            if (frostDamaged > 0)
            {
                result.Interrupts.Add($"Frost damage — mowed/rolled {frostDamaged} frozen zone(s); turf bruised");
                RaiseInterrupt();
            }

            result.Step($"resolved {Course.Zones.Count} zones; expressions={result.Expressions.Count}");

            // Step 11 — interrupts. Crises always surface to the player, overriding any delegation/skip.
            DetectInterrupts(result);

            // Step 12 — economy (opt-in): condition -> demand -> revenue, minus the day's costs.
            if (Economy != null)
                Greenkeeper.Sim.Economy.EconomySystem.Settle(
                    Economy, Course, result.Weather, plan, Clock.DayIndex, Clock.Season,
                    EconomyConfig ?? Greenkeeper.Sim.Economy.EconomyConfig.Default, _tuning);

            // Step 13 — tournament (opt-in): grade on the day, pay out, and pull the player in.
            if (Tournament != null)
            {
                var tr = Tournament.ProcessDay(Clock.DayIndex, Course, Economy);
                if (tr != null)
                {
                    result.Tournament = tr;
                    result.Interrupts.Add($"Tournament — {tr}");
                    RaiseInterrupt();
                }
            }

            Clock.AdvanceDay();
            return result;
        }

        private readonly System.Collections.Generic.HashSet<string> _diseaseFlagged = new System.Collections.Generic.HashSet<string>();
        private int _hotDryRun; // consecutive hot, rainless days (for flash drought)

        private void DetectInterrupts(DayResult result)
        {
            var w = result.Weather;

            if (WeatherInterruptsEnabled)
            {
                // Heat spike — a punishing day demands attention regardless of what's automated.
                if (WeatherEvents.IsHeatSpike(w, _tuning))
                    result.Interrupts.Add($"Heat spike: {w.TmaxF:F0}F");

                // Storm — a downpour washes out the bunkers (cleanup pressure) and floods greens.
                if (WeatherEvents.IsStorm(w, _tuning))
                {
                    int washed = 0;
                    foreach (var z in Course.Zones)
                        if (z.Type == Greenkeeper.Sim.Config.ZoneType.Bunker) { z.WashedOut = true; washed++; }
                    result.Interrupts.Add($"Storm: {w.RainMm:F0}mm — {washed} bunkers washed out");
                }

                // Frost — don't mow/roll frozen turf (it bruises), and play is sparse.
                if (WeatherEvents.IsFrost(w, _tuning))
                    result.Interrupts.Add($"Frost: {w.TminF:F0}F — hold off mowing the frozen turf");

                // Flash drought — a run of hot, rainless days.
                if (WeatherEvents.IsHotDry(w, _tuning)) _hotDryRun++; else _hotDryRun = 0;
                if (_hotDryRun == _tuning.FlashDroughtDays)
                    result.Interrupts.Add($"Flash drought: {_hotDryRun} hot, dry days");
            }

            // Fresh disease break — a green crossing the threshold (armed once, until it recovers).
            double clear = _tuning.InterruptInfectionThreshold * _tuning.InterruptClearFraction;
            foreach (var z in Course.Greens)
            {
                double inf = z.MaxInfection;
                if (inf >= _tuning.InterruptInfectionThreshold && !_diseaseFlagged.Contains(z.Id))
                {
                    _diseaseFlagged.Add(z.Id);
                    result.Interrupts.Add($"Disease break on {z.Id} (infection {inf:F0})");
                }
                else if (inf < clear)
                {
                    _diseaseFlagged.Remove(z.Id);
                }
            }

            if (result.IsInterruptDay) RaiseInterrupt();
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
