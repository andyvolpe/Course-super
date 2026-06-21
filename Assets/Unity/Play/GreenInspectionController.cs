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

            if (UnityEngine.Input.GetKeyDown(meterKey) && (room == null || room.CarryingMeter))
            {
                ZoneState z = game.Course.Get(AimedZoneId);
                if (z != null) LastMeterReading = (float)game.Legibility.MeterReading(z, AimedCellIndex, day);
            }
            if (UnityEngine.Input.GetKeyDown(scoutKey)) game.Legibility.Scout(AimedZoneId, day);
            if (UnityEngine.Input.GetKeyDown(soilTestKey)) game.Legibility.SoilTest(AimedZoneId, day);
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
