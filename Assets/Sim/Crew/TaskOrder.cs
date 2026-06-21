// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Crew
{
    /// <summary>The MVP maintenance task types (GDD §4, TDD §5).</summary>
    public enum TaskType
    {
        WalkMowGreens,   // per green, premium cut
        TriplexMowGreens,// course-wide, faster but coarser than a walk-mow
        RollGreens,      // per green, speed/smoothness
        Spray,           // per zone, fungicide
        Water,           // per zone, irrigation
        Fertilize,       // per zone, nitrogen (legacy simple feed)
        FertilizeProgram,// per zone, the full fertility PROGRAM (source/method/N:K/Fe)
        Aerate,          // per zone, relieves OM/debt
        MowFairways,     // per fairway
        RakeBunkers,     // course-wide
        ChangeCups       // course-wide, cosmetic/wear (no agronomy delta)
    }

    /// <summary>
    /// One ordered job for a window (TDD §2). It costs hours against the crew budget; it may be
    /// delegated to staff or kept hands-on; and (Phase 4.2) its delegation quality scales the
    /// beneficial part of its effect.
    /// </summary>
    public sealed class TaskOrder
    {
        public TaskType Type;
        public string ZoneId;        // null/"" => course-wide / all relevant zones
        public double HoursCost;     // hours consumed from the window budget
        public double Amount;        // optional payload (irrigation mm, fertilizer N) — 0 => catalog default
        public FertApplication Program; // payload for TaskType.FertilizeProgram (source/method/N:K/Fe)

        public bool Delegated;       // true => staff handle it; false => the player does it
        public string AssignedCrewId; // which crew member (for delegated tasks / speed)

        public TaskOrder() { }

        public TaskOrder(TaskType type, string zoneId, double hoursCost, double amount = 0.0)
        {
            Type = type; ZoneId = zoneId; HoursCost = hoursCost; Amount = amount;
        }

        public bool IsCourseWide => string.IsNullOrEmpty(ZoneId);
    }
}
