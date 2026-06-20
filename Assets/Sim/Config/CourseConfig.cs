// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.Config
{
    /// <summary>
    /// Authored description of an entire course (TDD §2). Produced by the Unity ScriptableObject
    /// wrapper and consumed by CourseFactory. Pure C#.
    /// </summary>
    public sealed class CourseConfig
    {
        public string Name = "MVP Cash-Cow Course";
        public int Holes = 18;
        public GrassProfile Grass = GrassProfile.Mvp();
        public List<ZoneSpec> Zones = new List<ZoneSpec>();
        public AgronomyTuning Tuning = AgronomyTuning.Default;

        /// <summary>
        /// Builds the canonical MVP course layout from the §5 tuning table: 18 greens (3x3 sub-cell
        /// USGA-spec), 18 tees, 18 fairways, rough blocks, and 40 bunkers. Initial *state* values
        /// (healthy start) are applied by CourseFactory, not here.
        /// </summary>
        public static CourseConfig Mvp()
        {
            var cfg = new CourseConfig();
            int bunkersRemaining = 40;
            for (int hole = 1; hole <= cfg.Holes; hole++)
            {
                cfg.Zones.Add(new ZoneSpec($"green-{hole:00}", ZoneType.Green, SoilType.UsgaSpec, hole, 3));
                cfg.Zones.Add(new ZoneSpec($"tee-{hole:00}", ZoneType.Tee, SoilType.PushUp, hole, 1));
                cfg.Zones.Add(new ZoneSpec($"fairway-{hole:00}", ZoneType.Fairway, SoilType.PushUp, hole, 1));
                cfg.Zones.Add(new ZoneSpec($"rough-{hole:00}", ZoneType.Rough, SoilType.PushUp, hole, 1));

                // Distribute 40 bunkers across 18 holes (a couple of holes get a third).
                int bunkersThisHole = bunkersRemaining > 0 ? 2 : 0;
                if (hole <= (40 - 18 * 2) && bunkersRemaining > 2) bunkersThisHole = 3;
                for (int b = 0; b < bunkersThisHole && bunkersRemaining > 0; b++)
                {
                    cfg.Zones.Add(new ZoneSpec($"bunker-{hole:00}-{b + 1}", ZoneType.Bunker, SoilType.UsgaSpec, hole, 1));
                    bunkersRemaining--;
                }
            }
            return cfg;
        }

        /// <summary>
        /// A greens-only layout (18 USGA-spec 3x3 greens by default). Used by behavioural tests that
        /// only exercise the disease/debt systems on greens — keeps multi-hundred-seed runs fast.
        /// </summary>
        public static CourseConfig GreensOnly(int greens = 18)
        {
            var cfg = new CourseConfig { Name = "Greens-Only Test Course", Holes = greens };
            for (int hole = 1; hole <= greens; hole++)
                cfg.Zones.Add(new ZoneSpec($"green-{hole:00}", ZoneType.Green, SoilType.UsgaSpec, hole, 3));
            return cfg;
        }
    }
}
