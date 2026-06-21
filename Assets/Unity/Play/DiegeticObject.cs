using UnityEngine;

namespace Greenkeeper.Unity.Play
{
    /// <summary>The physical things in the maintenance building the player interacts with (GDD §5.3).</summary>
    public enum DiegeticKind
    {
        CourseMap,      // wall-mounted macro health board (the "where to look" view)
        Calendar,       // wall — schedule tasks + per-task delegation (§4.2)
        CrewBoard,      // wall — the depth dial + the delegated-routine "run the day" glance (§0.4)
        Forecast,       // desk — the NOAA sheet you must pick up to read (§7)
        SoilClipboard,  // desk — soil-test results (earned reads), legible
        MoistureMeter,  // desk — handheld you carry onto greens to meter VWC
        Stimpmeter,     // desk — handheld: roll balls on a green to read green speed (§10)
        FirmnessMeter,  // desk — handheld: drop on a green to read receptivity (§10)
        FertLog,        // desk — the fertiliser / spray record
    }

    /// <summary>The measurement tool currently in hand (one at a time); USE on a green performs its act.</summary>
    public enum CarriedTool { None, MoistureMeter, Stimpmeter, FirmnessMeter }

    /// <summary>
    /// Marks a placeholder (cube-grey) object in the maintenance room as interactable. The
    /// <see cref="RoomInteractionController"/> raycasts for these; using one opens a CLEAN, readable
    /// interface (drawn by <see cref="UI.MaintenanceRoomHud"/>) — ritual and place first, legible data
    /// second. Art is Milestone B; this is purely the interaction model.
    /// </summary>
    public sealed class DiegeticObject : MonoBehaviour
    {
        public DiegeticKind Kind;
        public string Label = "";   // the verb-first prompt, e.g. "Read the forecast"
        public bool IsPickup;       // carried items (the moisture meter) vs wall/desk stations
    }
}
