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
        public int holesToRender = 3;     // full holes laid out tee -> fairway -> approach -> green
        public float cellSizeM = 2.0f;

        private GameManager _game;
        private FirstPersonController _fp;
        private GreenInspectionController _inspect;
        private PuttingController _putt;
        private bool _planMode = true; // start in Plan mode so the HUD is usable immediately
        private string _error;

        /// <summary>True when the cursor is free for planning (vs Course mode walking/putting).</summary>
        public bool PlanMode => _planMode;

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
                var greens = new System.Collections.Generic.List<GreenRenderer>();
                int holes = Mathf.Max(1, holesToRender);
                for (int i = 0; i < holes; i++)
                {
                    int hole = i + 1;
                    if (_game.Course.Get($"green-{hole:00}") == null) continue;
                    var gr = BuildHole(hole, new Vector3(i * 22f, 0f, 0f), greenMat);
                    if (gr != null) greens.Add(gr);
                }

                var (player, cam) = BuildPlayer();
                BuildBallAndCup(player, cam, greens.Count > 0 ? greens[0] : null);
                BuildHuds(player);

                SetPlanMode(true);
                Debug.Log($"[Bootstrap] scene built: {_game.Course.Zones.Count} zones, {greens.Count} holes rendered " +
                          "(tee/fairway/approach/green/rough/bunkers). TAB to walk; hold LMB to putt.");
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
            // Big enough to sit under every rendered hole (holes march along +X, ~54m deep in +Z).
            float centerX = (Mathf.Max(1, holesToRender) - 1) * 22f * 0.5f;
            ground.transform.position = new Vector3(centerX, -0.05f, 25f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f); // 300m x 300m
            var mat = SolidMaterial(new Color(0.24f, 0.32f, 0.16f)); // native-rough backdrop
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

        // ---- holes (all surfaces) ----

        /// <summary>
        /// Lays out one whole hole in the world — rough pad, tee, fairway, approach, bunkers, and the
        /// 3x3 green at the far end — each quad driven by its sim zone via SurfaceRenderer/GreenRenderer.
        /// </summary>
        private GreenRenderer BuildHole(int hole, Vector3 origin, Material greenMat)
        {
            string h = hole.ToString("00");

            // Native rough pad under the whole hole (the miss-penalty surface you maintain).
            BuildSurfaceQuad($"rough-{h}", origin + new Vector3(0f, 0f, 18f), 16f, 42f, -0.03f, new Color(0.22f, 0.34f, 0.14f));

            // Playing corridor: tee -> fairway -> approach.
            if (_game.Course.Get($"tee-{h}") != null)
                BuildSurfaceQuad($"tee-{h}", origin + new Vector3(0f, 0f, 2f), 4f, 4f, 0f, new Color(0.18f, 0.44f, 0.18f));
            if (_game.Course.Get($"fairway-{h}") != null)
                BuildSurfaceQuad($"fairway-{h}", origin + new Vector3(0f, 0f, 16f), 8f, 22f, 0f, new Color(0.20f, 0.42f, 0.18f));
            if (_game.Course.Get($"approach-{h}") != null)
                BuildSurfaceQuad($"approach-{h}", origin + new Vector3(0f, 0f, 29f), 7f, 4f, 0f, new Color(0.17f, 0.43f, 0.17f));

            // Whatever bunkers this hole has.
            BuildBunkerIfExists($"bunker-{h}-1", origin + new Vector3(5f, 0f, 31f));
            BuildBunkerIfExists($"bunker-{h}-2", origin + new Vector3(-5f, 0f, 30f));
            BuildBunkerIfExists($"bunker-{h}-3", origin + new Vector3(6f, 0f, 18f));

            // The green at the far end (existing 3x3 sub-cell renderer; ball/cup live here for hole 1).
            var gr = BuildGreen($"green-{h}", origin + new Vector3(0f, 0f, 33f), 3f + hole, greenMat);
            gr.game = _game;
            return gr;
        }

        private void BuildBunkerIfExists(string zoneId, Vector3 center)
        {
            if (_game.Course.Get(zoneId) == null) return;
            BuildSurfaceQuad(zoneId, center, 3.2f, 3.2f, -0.02f, new Color(0.82f, 0.74f, 0.55f));
        }

        private GameObject BuildSurfaceQuad(string zoneId, Vector3 center, float sizeX, float sizeZ, float y, Color baseColor)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane); // faces +Y, has MeshCollider (walkable)
            go.name = $"Surf_{zoneId}";
            go.transform.position = center + Vector3.up * y;
            go.transform.localScale = new Vector3(sizeX / 10f, 1f, sizeZ / 10f); // Plane is 10x10 at scale 1
            var mr = go.GetComponent<Renderer>();
            var mat = SolidMaterial(baseColor);
            if (mat != null) mr.material = mat;
            var sr = go.AddComponent<SurfaceRenderer>();
            sr.game = _game;
            sr.zoneId = zoneId;
            return go;
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
            var hud = ui.AddComponent<GreenkeeperHud>();
            hud.game = _game;
            hud.inspection = _inspect;
            hud.putt = _putt;
            hud.bootstrap = this;
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
                GUI.Label(new Rect(12, 40, 600, 40), "GameBootstrap: scene not built (check the Console).");
            // The unified GreenkeeperHud draws the rest (top bar, panels, crosshair, putt meter).
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
