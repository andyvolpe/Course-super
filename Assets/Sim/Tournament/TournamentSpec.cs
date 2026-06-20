// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.Tournament
{
    /// <summary>
    /// The agronomist's setup brief for an event: hit these targets BY the deadline day. "Under the
    /// clock" is real because agronomy lags (you can't snap Stimp up) and crew-hours are scarce, so the
    /// greens must be STEERED to spec over the preceding days.
    /// </summary>
    public sealed class TournamentSpec
    {
        public string Name = "Event";
        public int DayIndex;                 // the tournament day (absolute)

        public double StimpMin = 11.0, StimpMax = 12.5;
        public double FirmMin = 60.0, FirmMax = 85.0;
        public double MaxInfection = 5.0;    // disease above this is penalized hard
        public double MinDensity = 80.0;
        public double ConsistencyToleranceStimp = 0.5; // greens should roll alike; stdev over this is penalized

        public double PassScore = 60.0;      // Bronze threshold
        public double PrizeMoney = 25000.0;
        public double ReputationGain = 10.0;

        /// <summary>The MVP ladder: three rungs of rising demand across a summer.</summary>
        public static List<TournamentSpec> MvpLadder()
        {
            return new List<TournamentSpec>
            {
                new TournamentSpec {
                    Name = "Member-Guest", DayIndex = 120,
                    StimpMin = 10.0, StimpMax = 11.5, FirmMin = 50, FirmMax = 75,
                    MaxInfection = 8, MinDensity = 78, ConsistencyToleranceStimp = 0.7,
                    PassScore = 60, PrizeMoney = 20000, ReputationGain = 8,
                },
                new TournamentSpec {
                    Name = "Club Championship", DayIndex = 150,
                    StimpMin = 11.0, StimpMax = 12.5, FirmMin = 60, FirmMax = 85,
                    MaxInfection = 5, MinDensity = 82, ConsistencyToleranceStimp = 0.5,
                    PassScore = 70, PrizeMoney = 40000, ReputationGain = 12,
                },
                new TournamentSpec {
                    Name = "Regional Qualifier", DayIndex = 180,
                    StimpMin = 12.0, StimpMax = 13.5, FirmMin = 70, FirmMax = 92,
                    MaxInfection = 3, MinDensity = 85, ConsistencyToleranceStimp = 0.4,
                    PassScore = 80, PrizeMoney = 75000, ReputationGain = 18,
                },
            };
        }
    }
}
