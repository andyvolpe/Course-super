// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.Crew
{
    /// <summary>
    /// A member of the maintenance crew (TDD §2). Skill/knowledge drive delegation quality (§4.8),
    /// speed scales how many hours a task costs them, reliability is their chance of executing as
    /// planned. All 0..1.
    /// </summary>
    public sealed class CrewMember
    {
        public string Id = "";
        public string Name = "";
        public double Skill = 0.5;       // craftsmanship -> delegation quality
        public double Speed = 1.0;       // >1 faster (task costs fewer hours), <1 slower
        public double Reliability = 0.9; // chance of doing the job as ordered
        public double Knowledge = 0.5;   // agronomic judgement -> delegation quality
        public double AvailableHours = 6.0; // hours this crew member contributes to a window

        public CrewMember() { }

        public CrewMember(string id, string name, double skill, double speed, double reliability, double knowledge, double availableHours = 6.0)
        {
            Id = id; Name = name; Skill = skill; Speed = speed; Reliability = reliability; Knowledge = knowledge; AvailableHours = availableHours;
        }

        /// <summary>The MVP default crew: 5 hands, ~6 hrs each = ~30 crew-hours/window (TDD §5).</summary>
        public static List<CrewMember> DefaultCrew()
        {
            return new List<CrewMember>
            {
                new CrewMember("crew-1", "Foreman",   skill: 0.80, speed: 1.10, reliability: 0.97, knowledge: 0.80),
                new CrewMember("crew-2", "Spray Tech", skill: 0.70, speed: 1.00, reliability: 0.95, knowledge: 0.70),
                new CrewMember("crew-3", "Mower A",    skill: 0.55, speed: 1.05, reliability: 0.92, knowledge: 0.45),
                new CrewMember("crew-4", "Mower B",    skill: 0.50, speed: 1.00, reliability: 0.90, knowledge: 0.40),
                new CrewMember("crew-5", "Seasonal",   skill: 0.35, speed: 0.90, reliability: 0.85, knowledge: 0.25),
            };
        }
    }
}
