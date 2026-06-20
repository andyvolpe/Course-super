// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Crew
{
    /// <summary>
    /// Hour costs and effect mapping for tasks (TDD §5). Costs are crew-hours per task instance; the
    /// effect mapping reuses the Phase 2.3 action deltas (mow/roll/spray/water/fertilize/aerate).
    /// </summary>
    public static class TaskCatalog
    {
        public const double DefaultWaterMm = 8.0;
        public const double DefaultFertilizerN = 12.0;

        public static double BaseHours(TaskType type)
        {
            switch (type)
            {
                case TaskType.WalkMowGreens:    return 0.6;  // per green
                case TaskType.TriplexMowGreens: return 3.0;  // all greens, one pass
                case TaskType.RollGreens:       return 0.4;  // per green
                case TaskType.Spray:            return 0.5;  // per zone
                case TaskType.Water:            return 0.3;  // per zone
                case TaskType.Fertilize:        return 0.4;  // per zone
                case TaskType.Aerate:           return 1.5;  // per zone (heavy)
                case TaskType.MowFairways:      return 0.8;  // per fairway
                case TaskType.RakeBunkers:      return 4.0;  // all bunkers
                case TaskType.ChangeCups:       return 1.5;  // all greens
                default:                        return 0.5;
            }
        }

        // ---- Create helpers (hours pre-filled from the catalog) ----
        public static TaskOrder WalkMow(string greenId) => new TaskOrder(TaskType.WalkMowGreens, greenId, BaseHours(TaskType.WalkMowGreens));
        public static TaskOrder Triplex() => new TaskOrder(TaskType.TriplexMowGreens, null, BaseHours(TaskType.TriplexMowGreens));
        public static TaskOrder Roll(string greenId) => new TaskOrder(TaskType.RollGreens, greenId, BaseHours(TaskType.RollGreens));
        public static TaskOrder Spray(string zoneId) => new TaskOrder(TaskType.Spray, zoneId, BaseHours(TaskType.Spray));
        public static TaskOrder Water(string zoneId, double mm = DefaultWaterMm) => new TaskOrder(TaskType.Water, zoneId, BaseHours(TaskType.Water), mm);
        public static TaskOrder Fertilize(string zoneId, double n = DefaultFertilizerN) => new TaskOrder(TaskType.Fertilize, zoneId, BaseHours(TaskType.Fertilize), n);
        public static TaskOrder Aerate(string zoneId) => new TaskOrder(TaskType.Aerate, zoneId, BaseHours(TaskType.Aerate));
        public static TaskOrder MowFairway(string fairwayId) => new TaskOrder(TaskType.MowFairways, fairwayId, BaseHours(TaskType.MowFairways));
        public static TaskOrder RakeBunkers() => new TaskOrder(TaskType.RakeBunkers, null, BaseHours(TaskType.RakeBunkers));
        public static TaskOrder ChangeCups() => new TaskOrder(TaskType.ChangeCups, null, BaseHours(TaskType.ChangeCups));

        /// <summary>Folds a task's effect into a zone's action, scaling beneficial deltas by quality (§4.8).</summary>
        public static ZoneAction Fold(TaskOrder task, ZoneAction a, double quality)
        {
            a.Quality = a.Quality <= 0 ? quality : System.Math.Min(a.Quality, quality); // worst link on the zone
            switch (task.Type)
            {
                case TaskType.WalkMowGreens:
                    a.Mow = true; a.MowHeightIn = 0.125; break;
                case TaskType.TriplexMowGreens:
                    a.Mow = true; a.MowHeightIn = 0.130; break; // marginally higher/coarser than a walk-mow
                case TaskType.RollGreens:
                    a.Roll = true; break;
                case TaskType.Spray:
                    a.Spray = true; break;
                case TaskType.Water:
                    a.IrrigationMm += task.Amount > 0 ? task.Amount : DefaultWaterMm; break;
                case TaskType.Fertilize:
                    a.FertilizerN += task.Amount > 0 ? task.Amount : DefaultFertilizerN; break;
                case TaskType.Aerate:
                    a.Aerate = true; break;
                case TaskType.MowFairways:
                    a.Mow = true; a.MowHeightIn = 0.5; break;
                case TaskType.RakeBunkers:
                    a.Rake = true; break; // clears storm washout
                case TaskType.ChangeCups:
                    break; // no agronomy delta (cosmetic)
            }
            return a;
        }
    }
}
