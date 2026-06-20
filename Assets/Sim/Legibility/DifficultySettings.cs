// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Legibility
{
    public enum Difficulty
    {
        FullDifficulty, // earn every read from tells + tools; no debt bar, no telegraphs, no auto-scout
        Assisted        // surfacing aids ON; the SAME simulation underneath
    }

    /// <summary>
    /// The assist layer (GDD §3.1 rule 3 / §0.2). These flags change ONLY what the LegibilitySystem
    /// surfaces — never the simulation. turf debt is never surfaced as a number on full difficulty.
    /// </summary>
    public sealed class DifficultySettings
    {
        public Difficulty Mode = Difficulty.FullDifficulty;

        public bool ShowTurfDebtBar;     // assists: render the hidden debt accumulator as a bar
        public bool TelegraphThreats;    // assists: warn about incoming disease/heat before it expresses
        public bool AutoScout;           // assists: keep scouting reveals fresh without manual action
        public bool ShowExactOnScout = true; // show exact numbers (vs ranges) once a read is earned

        public static DifficultySettings Full() => new DifficultySettings
        {
            Mode = Difficulty.FullDifficulty,
            ShowTurfDebtBar = false,
            TelegraphThreats = false,
            AutoScout = false,
        };

        public static DifficultySettings WithAssists() => new DifficultySettings
        {
            Mode = Difficulty.Assisted,
            ShowTurfDebtBar = true,
            TelegraphThreats = true,
            AutoScout = true,
        };
    }
}
