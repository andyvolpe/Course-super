using UnityEngine;
using UnityEngine.Rendering;
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
        private string _error;

        /// <summary>
        /// Auto-boot: when you enter Play mode in ANY scene, if there's no GameBootstrap already, spawn
        /// one. This means you can just press Play on an empty/default scene with zero setup — no
        /// GameObject, no Add Component. (Runs only in play mode, not during EditMode tests.)
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (FindFirstObjectByType<GameBootstrap>() != null) return; // already placed by hand
            var go = new GameObject("GameBootstrap (auto)");
            go.AddComponent<GameBootstrap>();
            Debug.Log("[Bootstrap] auto-booted — no GameBootstrap was in the scene, so one was created.");
        }

        private void Awake()
        {
            try
            {
                Debug.Log("[Bootstrap] building scene…");
                BuildLighting();
                BuildGround();
                _game = BuildGameManager();
                if (_game == null || _game.Course == null) { _error = "GameManager/course failed to build."; return; }

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
                Debug.Log($"[Bootstrap] scene built: {_game.Course.Zones.Count} zones, {renderers.Count} greens rendered. " +
                          "TAB to walk; hold LMB to putt.");
            }
            catch (System.Exception e)
            {
                _error = e.ToString();
                Debug.LogError("[Bootstrap] build FAILED: " + e);
            }
        }

        // ---- world ----

        private void BuildLighting()
        {
            if (FindFirstObjectByType<Light>() != null) return;
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
            var mat = SolidMaterial(new Color(0.30f, 0.40f, 0.22f)); // rough/fairway green
            if (mat != null) ground.GetComponent<Renderer>().material = mat;
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
                    if (greenMat != null) mr.material = greenMat; // per-renderer MPB set by GreenRenderer
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

            // Reuse the scene's existing camera if there is one (avoids two cameras fighting); else make one.
            Camera cam = Camera.main;
            GameObject camGo = cam != null ? cam.gameObject : new GameObject("Camera");
            if (cam == null) { cam = camGo.AddComponent<Camera>(); camGo.AddComponent<AudioListener>(); try { camGo.tag = "MainCamera"; } catch { } }
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camGo.transform.localRotation = Quaternion.identity;

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
            var ballMat = SolidMaterial(Color.white); if (ballMat != null) ball.GetComponent<Renderer>().material = ballMat;

            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            cup.transform.localScale = new Vector3(0.10f, 0.02f, 0.10f);
            cup.transform.position = greenCenter + up * 0.02f + firstGreen.transform.forward * 1.5f;
            Destroy(cup.GetComponent<Collider>());
            var cupMat = SolidMaterial(Color.black); if (cupMat != null) cup.GetComponent<Renderer>().material = cupMat;

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
            ScaleGuiFonts(); // runs first (exec order -50) so every HUD this frame gets readable text

            if (_error != null)
            {
                GUI.color = Color.red;
                GUI.Label(new Rect(12, 40, Screen.width - 24, Screen.height - 80), "GameBootstrap failed to build the scene:\n\n" + _error);
                GUI.color = Color.white;
                return;
            }
            if (_game == null)
            {
                GUI.Label(new Rect(12, 40, 600, 40), "GameBootstrap: scene not built (check the Console).");
                return;
            }
            GUI.Label(new Rect(Screen.width / 2 - 220, 6, 460, 22),
                _planMode ? "PLAN mode — click the morning window. TAB to walk the course."
                          : "COURSE mode — WASD walk, mouse look. LMB putt / RMB approach, E meter, Q scout. TAB to plan.");
            // crosshair in course mode
            if (!_planMode)
                GUI.Label(new Rect(Screen.width / 2 - 4, Screen.height / 2 - 8, 12, 16), "+");
        }

        private static void ScaleGuiFonts()
        {
            int fs = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 55f), 13, 30);
            GUI.skin.label.fontSize = fs;
            GUI.skin.button.fontSize = fs;
            GUI.skin.toggle.fontSize = fs;
            GUI.skin.box.fontSize = fs;
            GUI.skin.textField.fontSize = fs;
        }

        // ---- materials ----

        // Pick a lit shader that actually renders in the PROJECT'S active pipeline (avoids magenta).
        private static Shader LitShader()
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader sh = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            return sh ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        }

        private static Material MakeGreenMaterial() => SolidMaterial(new Color(0.16f, 0.42f, 0.16f));

        private static Material SolidMaterial(Color c)
        {
            Shader sh = LitShader();
            if (sh == null) return null;
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); // URP
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);         // Built-in
            m.color = c;
            return m;
        }
    }
}
