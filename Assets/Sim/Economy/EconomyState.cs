// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.Economy
{
    /// <summary>One day's books.</summary>
    public struct DayLedger
    {
        public int DayIndex;
        public double ConditionIndex;
        public double Reputation;
        public double Rounds;
        public double Revenue;
        public double Costs;
        public double Net;
        public double Cash;
    }

    /// <summary>Running financial state of the course.</summary>
    public sealed class EconomyState
    {
        public double Cash;
        public double Reputation;
        public readonly List<DayLedger> History = new List<DayLedger>();

        public EconomyState(EconomyConfig cfg = null)
        {
            cfg = cfg ?? EconomyConfig.Default;
            Cash = cfg.StartingCash;
            Reputation = cfg.StartingReputation;
        }

        public DayLedger Latest => History.Count > 0 ? History[History.Count - 1] : default;
    }
}
