// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>A single visible disease-symptom event, logged for the fairness audit (T4).</summary>
    public struct ExpressionEvent
    {
        public int DayIndex;
        public string ZoneId;
        public int CellIndex;
        public double Severity;
    }

    /// <summary>
    /// What one resolved day did: the ordered step log, any symptom expressions, and the
    /// per-zone "readable tell" snapshot the fairness gate used (TDD §3 / Test Spec).
    /// </summary>
    public sealed class DayResult
    {
        public int DayIndex;
        public Season Season;
        public WeatherDay Weather;
        public readonly List<string> Log = new List<string>();
        public readonly List<ExpressionEvent> Expressions = new List<ExpressionEvent>();
        public readonly Dictionary<string, bool> TellByZone = new Dictionary<string, bool>();

        public void Step(string message) => Log.Add(message);
    }
}
