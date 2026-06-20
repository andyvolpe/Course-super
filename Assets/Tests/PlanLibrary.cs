// Greenkeeper.Tests — pure NUnit-compatible (no UnityEngine).
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Tests
{
    /// <summary>Scripted morning-plan generators used by the behavioural and fuzz tests.</summary>
    public static class PlanLibrary
    {
        /// <summary>
        /// Sound superintendent practice on every green: mow tight but safe, keep moisture just under
        /// field capacity, feed nitrogen before it runs low, and stay on a 14-day fungicide interval.
        /// </summary>
        public static DayPlan Good(int dayIndex, CourseState course)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                double target = 16.0; // just under USGA FC -> firm, dry, low disease pressure
                double irrigationMm = Mathx.Max0((target - z.SoilMoisturePct) / 0.9);
                plan.Set(z.Id, new ZoneAction
                {
                    Mow = true,
                    MowHeightIn = 0.125,                 // safe height (>= MinSafeMowHeight)
                    Roll = (dayIndex % 3 == 0),
                    Spray = (dayIndex % 14 == 0),        // preventive fungicide interval
                    IrrigationMm = irrigationMm,
                    FertilizerN = z.NitrogenPct < 35.0 ? 12.0 : 0.0,
                    Aerate = false,
                });
            }
            return plan;
        }

        /// <summary>
        /// Textbook mismanagement: keep the greens soaked, never feed nitrogen, never spray, and scalp
        /// them below safe height. This should drive dollar spot hard.
        /// </summary>
        public static DayPlan Bad(int dayIndex, CourseState course)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                plan.Set(z.Id, new ZoneAction
                {
                    Mow = true,
                    MowHeightIn = 0.08,   // below MinSafeMowHeight -> scalp
                    Roll = false,
                    Spray = false,
                    IrrigationMm = 20.0,  // chronic over-watering -> wet canopy
                    FertilizerN = 0.0,    // nitrogen starvation
                    Aerate = false,
                });
            }
            return plan;
        }

        /// <summary>A stateful generator of random-but-legal plans for the invariant fuzz (T2).</summary>
        public sealed class RandomPlanner
        {
            private readonly Rng _rng;
            public RandomPlanner(int seed) { _rng = new Rng(seed ^ 0x5151); }

            public DayPlan Plan(int dayIndex, CourseState course)
            {
                var plan = new DayPlan();
                foreach (var z in course.Zones)
                {
                    plan.Set(z.Id, new ZoneAction
                    {
                        Mow = _rng.Chance(0.6),
                        MowHeightIn = _rng.Range(0.08, 0.50),
                        Roll = _rng.Chance(0.2),
                        Spray = _rng.Chance(0.1),
                        IrrigationMm = _rng.Chance(0.7) ? _rng.Range(0.0, 30.0) : 0.0,
                        FertilizerN = _rng.Chance(0.15) ? _rng.Range(0.0, 20.0) : 0.0,
                        Aerate = _rng.Chance(0.02),
                    });
                }
                return plan;
            }
        }
    }
}
