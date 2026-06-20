using UnityEngine;
using Greenkeeper.Unity.Managers;
using Greenkeeper.Unity.UI;
using Greenkeeper.Unity.Input;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// One-component playable bootstrap. Drop this on an empty GameObject in an empty scene and press
    /// Play — it assembles the whole fun-core at runtime: the GameManager (sim), a couple of tilted
    /// greens rendered as 3x3 sub-cell grids that read the sim's tells, a first-person Player that can
    /// walk / inspect / putt, a ball + cup, and every HUD wired up. No hand-wiring, no prefabs.
    ///
    /// Controls (shown on screen): TAB toggles Plan mode (free cursor, click the morning window) and
    /// Course mode (mouse-look + walk + putt). WASD move; LMB hold = putt, RMB hold = approach;
    /// E meter, Q scout, R soil-test the green you're looking at.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("World")]
        public int weatherSeed = 12345;
        public bool assists = false;
        public int greensToRender = 2;
        public float cellSizeM = 2.0f;

        private GameManager _game;
        private FirstPersonController _fp;
        private GreenInspectionController _inspect;
        private PuttingController _putt;
        private bool _planMode = true; // start in Plan mode so the HUD is usable immediately

        private void Awake()
        {
            BuildLighting();
            BuildGround();
            _game = BuildGameManager();
            if (_game == null || _game.Course == null) { Debug.LogError("[Bootstrap] GameManager/course failed to build."); return; }

            Material greenMat = MakeGreenMaterial();
            var renderers = new System.Collections.Generic.List<GreenRenderer>();
            for (int i = 0; i < greensToRender; i++)
            {
                string id = $"green-{i + 1:00}";
                if (_game.Course.Get(id) == null) continue;
                var gr = BuildGreen(id, new Vector3(i * (greensToRender > 1 ? 12f : 0f), 0f, 10f), 3f + i * 2f, greenMat);
                gr.game = _game;
                renderers.Add(gr);
            }

            var (player, cam) = BuildPlayer();
            BuildBallAndCup(player, cam, renderers.Count > 0 ? renderers[0] : null);
            BuildHuds(player);

            SetPlanMode(true);
        }

        // ---- world ----

        private void BuildLighting()
        {
            if (FindObjectOfType<Light>() != null) return;
            var go = new GameObject("Sun");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.5f);
        }

        private void BuildGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100m x 100m
            var mr = ground.GetComponent<Renderer>();
            mr.material = SolidMaterial(new Color(0.30f, 0.40f, 0.22f)); // rough/fairway green
        }

        private GameManager BuildGameManager()
        {
            var go = new GameObject("Game");
            var game = go.AddComponent<GameManager>(); // Awake builds a default game
            game.weatherSeed = weatherSeed;
            game.assistsEnabled = assists;
            game.NewGame(); // rebuild with our seed/difficulty
            return game;
        }

        // ---- greens ----

        private GreenRenderer BuildGreen(string zoneId, Vector3 center, float tiltDeg, Material greenMat)
        {
            var parent = new GameObject($"Green_{zoneId}");
            parent.transform.position = center;
            parent.transform.rotation = Quaternion.Euler(tiltDeg, 17f * (zoneId.GetHashCode() % 5), 0f); // tilt -> break

            var gr = parent.AddComponent<GreenRenderer>();
            gr.zoneId = zoneId;
            gr.cellRenderers = new Renderer[9];

            float s = cellSizeM;
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    var cell = GameObject.CreatePrimitive(PrimitiveType.Plane); // faces +Y, has MeshCollider
                    cell.name = $"{zoneId}_cell{r * 3 + c}";
                    cell.transform.SetParent(parent.transform, false);
                    cell.transform.localScale = new Vector3(s / 10f, 1f, s / 10f); // Plane is 10x10 at scale 1
                    cell.transform.localPosition = new Vector3((c - 1) * s, 0f, (r - 1) * s);
                    var mr = cell.GetComponent<Renderer>();
                    mr.material = greenMat; // per-renderer MPB set by GreenRenderer
                    gr.cellRenderers[r * 3 + c] = mr;
                }
            }
            return gr;
        }

        // ---- player ----

        private (GameObject player, Camera cam) BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 1.1f, 0f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0);

            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";

            _fp = player.AddComponent<FirstPersonController>(); // Awake finds the child camera

            _inspect = player.AddComponent<GreenInspectionController>();
            _inspect.game = _game; _inspect.cam = cam;

            _putt = player.AddComponent<PuttingController>();
            _putt.game = _game; _putt.cam = cam;

            return (player, cam);
        }

        private void BuildBallAndCup(GameObject player, Camera cam, GreenRenderer firstGreen)
        {
            if (firstGreen == null) return;
            Vector3 greenCenter = firstGreen.transform.position;
            Vector3 up = firstGreen.transform.up;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.localScale = Vector3.one * 0.12f;
            ball.transform.position = greenCenter + up * 0.08f - firstGreen.transform.forward * 1.5f;
            Destroy(ball.GetComponent<Collider>()); // ball is kinematic-animated, no physics collider needed
            ball.GetComponent<Renderer>().material = SolidMaterial(Color.white);

            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            cup.transform.localScale = new Vector3(0.10f, 0.02f, 0.10f);
            cup.transform.position = greenCenter + up * 0.02f + firstGreen.transform.forward * 1.5f;
            Destroy(cup.GetComponent<Collider>());
            cup.GetComponent<Renderer>().material = SolidMaterial(Color.black);

            var putt = player.GetComponent<PuttingController>();
            putt.ball = ball.transform;
            putt.cup = cup.transform;
        }

        // ---- HUDs ----

        private void BuildHuds(GameObject player)
        {
            var ui = new GameObject("HUD");

            var debug = ui.AddComponent<GreenDebugPanel>();
            debug.game = _game; debug.enabled = false; // behind the window-HUD toggle

            var window = ui.AddComponent<MaintenanceWindowHud>();
            window.game = _game; window.debugPanel = debug;

            var forecast = ui.AddComponent<ForecastHud>();
            forecast.game = _game;

            var legibility = ui.AddComponent<LegibilityHud>();
            legibility.game = _game;
            legibility.inspection = _inspect;

            var economy = ui.AddComponent<EconomyHud>();
            economy.game = _game;

            var tournament = ui.AddComponent<TournamentHud>();
            tournament.game = _game;
        }

        // ---- modes / input ----

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab)) SetPlanMode(!_planMode);
        }

        private void SetPlanMode(bool plan)
        {
            _planMode = plan;
            if (_fp != null) _fp.enabled = !plan;          // stop mouse-look while planning
            if (_inspect != null) _inspect.enabled = !plan; // no scouting/metering while clicking HUD
            if (_putt != null) _putt.enabled = !plan;       // no accidental putt-charge on HUD clicks
            Cursor.lockState = plan ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = plan;
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(Screen.width / 2 - 220, 6, 460, 22),
                _planMode ? "PLAN mode — click the morning window. TAB to walk the course."
                          : "COURSE mode — WASD walk, mouse look. LMB putt / RMB approach, E meter, Q scout. TAB to plan.");
            // crosshair in course mode
            if (!_planMode)
                GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 8, 12, 16), "+");
        }

        // ---- materials ----

        private static Material MakeGreenMaterial()
        {
            Shader sh = Shader.Find("Greenkeeper/GreenSurface");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            m.color = new Color(0.16f, 0.42f, 0.16f);
            return m;
        }

        private static Material SolidMaterial(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            m.color = c;
            return m;
        }
    }
}
