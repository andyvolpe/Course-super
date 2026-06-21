using UnityEngine;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// Phase 3.2 — first-person inspection. Walk up to a green (existing FP controller), look at it to
    /// read the shader tells, and earn deeper reads with tools: a moisture meter at the spot you're
    /// aiming at, a scout, or a soil test. All reveals go through the sim's LegibilitySystem gate;
    /// nothing here exposes hidden state on its own. Same camera that will later play golf.
    ///
    /// SETUP: put on the Player; assign the camera; give green colliders the layer in greenMask, and
    /// each green a GreenRenderer with cellRenderers so we can resolve which sub-cell you're aiming at.
    /// </summary>
    public sealed class GreenInspectionController : MonoBehaviour
    {
        public GameManager game;
        public Camera cam;
        public RoomInteractionController room; // the moisture meter is a handheld you must carry out
        public float reach = 6f;
        public LayerMask greenMask = ~0;

        [Header("Keys")]
        public KeyCode meterKey = KeyCode.E;
        public KeyCode scoutKey = KeyCode.Q;
        public KeyCode soilTestKey = KeyCode.R;

        /// <summary>The green currently aimed at, and the sub-cell index, for the HUD to display.</summary>
        public string AimedZoneId { get; private set; }
        public int AimedCellIndex { get; private set; }
        public float LastMeterReading { get; private set; } = float.NaN;
        /// <summary>A clean one-line readout of the last measurement act, for the handheld HUD badge.</summary>
        public string LastToolReadout { get; private set; } = "";

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (cam == null) cam = GetComponentInChildren<Camera>() ?? Camera.main;
        }

        private void Update()
        {
            ResolveAim();
            if (AimedZoneId == null) return;
            int day = game.Director.Clock.DayIndex;

            // USE performs whatever measurement tool is in hand (moisture / stimp / firmness).
            if (UnityEngine.Input.GetKeyDown(meterKey)) UseCarriedTool(day);
            // Scouting / soil-testing by HAND is the expert read — it clears the tech-reported flag.
            if (UnityEngine.Input.GetKeyDown(scoutKey)) { game.Legibility.Scout(AimedZoneId, day); game.MarkPlayerRead(AimedZoneId); }
            if (UnityEngine.Input.GetKeyDown(soilTestKey)) { game.Legibility.SoilTest(AimedZoneId, day); game.MarkPlayerRead(AimedZoneId); }
        }

        private void UseCarriedTool(int day)
        {
            ZoneState z = game.Course.Get(AimedZoneId);
            if (z == null) return;
            CarriedTool tool = room != null ? room.Tool : CarriedTool.None;
            switch (tool)
            {
                case CarriedTool.MoistureMeter:
                    LastMeterReading = (float)game.Legibility.MeterReading(z, AimedCellIndex, day);
                    game.MarkPlayerRead(AimedZoneId);
                    LastToolReadout = $"moisture {LastMeterReading:0.#}% · {AimedZoneId} cell {AimedCellIndex}";
                    break;
                case CarriedTool.Stimpmeter:
                    game.RecordStimp(z.Id, z.Stimp);
                    LastToolReadout = $"green speed {z.Stimp:0.0} ft · {z.Id}";
                    break;
                case CarriedTool.FirmnessMeter:
                    game.RecordFirm(z.Id, z.FirmnessPct);
                    LastToolReadout = $"firmness {z.FirmnessPct:0}/100 · {z.Id}";
                    break;
                default:
                    LastToolReadout = "no tool in hand — pick one up in the building";
                    break;
            }
        }

        private void ResolveAim()
        {
            AimedZoneId = null;
            AimedCellIndex = 0;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, reach, greenMask)) return;

            // Any surface (incl. greens) is a SurfaceRenderer. Greens map the hit point to a 3x3 sub-cell
            // so scouting/metering is still spatially resolved.
            var sr = hit.collider.GetComponent<SurfaceRenderer>();
            if (sr != null)
            {
                AimedZoneId = sr.zoneId;
                AimedCellIndex = sr.CellIndexAt(hit.point);
            }
        }
    }
}
