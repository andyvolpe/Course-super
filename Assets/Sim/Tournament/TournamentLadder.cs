// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Tournament
{
    /// <summary>
    /// The goal to climb toward: a sequence of rungs of rising demand. Each is graded on its day; a good
    /// grade pays out (cash + reputation) and is the pride payoff. A flubbed event dings your standing.
    /// </summary>
    public sealed class TournamentLadder
    {
        public readonly List<TournamentSpec> Rungs;
        public int CurrentRung;
        public readonly List<TournamentResult> Results = new List<TournamentResult>();

        public TournamentLadder(List<TournamentSpec> rungs) { Rungs = rungs ?? new List<TournamentSpec>(); }

        public static TournamentLadder Mvp() => new TournamentLadder(TournamentSpec.MvpLadder());

        public TournamentSpec Current => CurrentRung >= 0 && CurrentRung < Rungs.Count ? Rungs[CurrentRung] : null;
        public bool HasUpcoming => Current != null;
        public int DaysUntilNext(int dayIndex) => Current != null ? Current.DayIndex - dayIndex : -1;

        /// <summary>
        /// If today is the current rung's tournament day, grade it, apply the payout to the economy
        /// (if present), advance the ladder, and return the result; otherwise null.
        /// </summary>
        public TournamentResult ProcessDay(int dayIndex, CourseState course, EconomyState eco)
        {
            var spec = Current;
            if (spec == null || dayIndex != spec.DayIndex) return null;

            var result = TournamentSystem.Evaluate(course, spec);

            double prizeMult, repMult;
            switch (result.Grade)
            {
                case TournamentGrade.Gold: prizeMult = 1.0; repMult = 1.0; break;
                case TournamentGrade.Silver: prizeMult = 0.6; repMult = 0.6; break;
                case TournamentGrade.Bronze: prizeMult = 0.3; repMult = 0.3; break;
                default: prizeMult = 0.0; repMult = -0.5; break; // a flubbed event hurts standing
            }
            result.PrizeAwarded = spec.PrizeMoney * prizeMult;
            result.ReputationDelta = spec.ReputationGain * repMult;

            if (eco != null)
            {
                eco.Cash += result.PrizeAwarded;
                eco.Reputation = Mathx.Clamp(eco.Reputation + result.ReputationDelta, 0, 100);
            }

            Results.Add(result);
            CurrentRung++;
            return result;
        }
    }
}
