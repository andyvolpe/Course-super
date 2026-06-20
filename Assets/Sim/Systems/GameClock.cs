// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine, DateTime.Now, or frame time.
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// The game clock (GDD §1). Deterministic: day index drives season; nothing reads wall-clock time.
    /// A "year" is 360 days, four 90-day seasons starting in spring.
    /// </summary>
    public sealed class GameClock
    {
        public const int DaysPerSeason = 90;
        public const int DaysPerYear = DaysPerSeason * 4;

        public int DayIndex { get; private set; }

        public int DayOfYear => DayIndex % DaysPerYear;
        public int Year => DayIndex / DaysPerYear;

        public Season Season => (Season)(DayOfYear / DaysPerSeason);

        /// <summary>Day within the current season, 0..89.</summary>
        public int DayOfSeason => DayOfYear % DaysPerSeason;

        public void AdvanceDay() => DayIndex++;

        public void Reset() => DayIndex = 0;

        /// <summary>Jump the clock to an absolute day (used to start a run in a chosen season).</summary>
        public void JumpTo(int day) => DayIndex = day < 0 ? 0 : day;
    }
}
