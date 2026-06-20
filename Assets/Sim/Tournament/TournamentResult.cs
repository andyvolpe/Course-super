// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Tournament
{
    public enum TournamentGrade { Fail, Bronze, Silver, Gold }

    /// <summary>The graded outcome of a tournament — the pride payoff, recorded.</summary>
    public sealed class TournamentResult
    {
        public string Name;
        public int DayIndex;
        public double Score;          // 0..100
        public TournamentGrade Grade;
        public bool Passed;

        // What the agronomist saw on the day.
        public double MeanStimp;
        public double MeanFirmness;
        public double StimpStdev;
        public double WorstInfection;
        public double MeanDensity;

        // Awarded (applied to the economy if present).
        public double PrizeAwarded;
        public double ReputationDelta;

        public override string ToString()
            => $"{Name}: {Grade} ({Score:F0}/100){(Passed ? "" : " — missed")}";
    }
}
