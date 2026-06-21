using UnityEngine;
using Greenkeeper.Unity.Input;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// DIEGETIC-PRIMARY interaction (GDD §3.2). The player is a person in the maintenance building: walk
    /// to a physical object, press USE, and it opens a clean readable panel (drawn by
    /// <see cref="UI.MaintenanceRoomHud"/>). Walking out the doorway puts you on the course. This
    /// component owns the camera ray, the focus prompt, which station is open, and the carried moisture
    /// meter — and it gates the FP / inspection / putt controllers so a panel never fights the world.
    ///
    /// Friction scales with the depth dial (§0.4): a DELEGATED routine day can be run from the crew board
    /// with one glance; a RECLAIMED day routes through the full calendar ritual.
    /// </summary>
    public sealed class RoomInteractionController : MonoBehaviour
    {
        public GameManager game;
        public Camera cam;
        public GameBootstrap bootstrap;
        public FirstPersonController fp;
        public GreenInspectionController inspection;
        public PuttingController putt;

        [Header("Reach / keys")]
        public float reach = 3.2f;
        public KeyCode useKey = KeyCode.E;
        public KeyCode closeKey = KeyCode.Escape;

        [Header("Room footprint (world XZ) — set by the bootstrap")]
        public Vector3 roomCenter;
        public Vector2 roomHalf = new Vector2(6f, 5f);

        /// <summary>The object currently under the crosshair within reach (for the prompt), or null.</summary>
        public DiegeticObject Focused { get; private set; }
        /// <summary>The station whose clean panel is open, or null when walking.</summary>
        public DiegeticObject Open { get; private set; }
        public bool PanelOpen => Open != null;

        /// <summary>The moisture meter is a HANDHELD: metering on a green only works while you carry it.</summary>
        public bool CarryingMeter { get; private set; }

        /// <summary>Depth dial (§0.4): true => routine work is delegated (glance + run); false => you reclaim it.</summary>
        public bool RoutineDelegated = true;

        /// <summary>The last day on which the forecast was actually read (so a skip can catch you out).</summary>
        public int ForecastReadDay { get; private set; } = int.MinValue;
        public void MarkForecastRead() => ForecastReadDay = game != null ? game.Director.Clock.DayIndex : 0;
        public bool ForecastReadToday => game != null && ForecastReadDay == game.Director.Clock.DayIndex;

        /// <summary>True while the player is physically inside the maintenance building.</summary>
        public bool InRoom { get; private set; } = true;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (cam == null) cam = GetComponentInChildren<Camera>() ?? Camera.main;
        }

        private void Update()
        {
            InRoom = Mathf.Abs(transform.position.x - roomCenter.x) <= roomHalf.x
                  && Mathf.Abs(transform.position.z - roomCenter.z) <= roomHalf.y;

            if (PanelOpen)
            {
                // A panel is up: world frozen, cursor free. Close on USE or ESC (HUD also has a Close button).
                if (UnityEngine.Input.GetKeyDown(useKey) || UnityEngine.Input.GetKeyDown(closeKey)) Close();
                return;
            }

            // Walking: golf + green-metering only make sense out on the course, never inside the building.
            if (putt != null) putt.enabled = !InRoom;
            if (inspection != null) inspection.enabled = !InRoom;

            Focused = RaycastObject();
            if (Focused != null && UnityEngine.Input.GetKeyDown(useKey)) Use(Focused);
        }

        private DiegeticObject RaycastObject()
        {
            if (cam == null) return null;
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out RaycastHit hit, reach)) return null;
            return hit.collider.GetComponentInParent<DiegeticObject>();
        }

        private void Use(DiegeticObject obj)
        {
            if (obj.Kind == DiegeticKind.MoistureMeter)
            {
                // Pure pickup ritual: grab it / set it back down. No panel — you read it out on the green.
                CarryingMeter = !CarryingMeter;
                return;
            }
            if (obj.Kind == DiegeticKind.Forecast) MarkForecastRead(); // reading is a deliberate act (§7)
            Open = obj;
            EnterUi(true);
        }

        /// <summary>Close the open panel and return to walking the room.</summary>
        public void Close()
        {
            Open = null;
            EnterUi(false);
        }

        private void EnterUi(bool ui)
        {
            if (fp != null) fp.enabled = !ui;          // stop mouse-look while reading a panel
            if (inspection != null) inspection.enabled = !ui;
            if (putt != null) putt.enabled = false;     // never charge a putt against a panel
            Cursor.lockState = ui ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = ui;
        }
    }
}
