using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Greenkeeper.Unity.Managers;
using Greenkeeper.Unity.UI;
using Greenkeeper.Unity.Input;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// One-component playable bootstrap. Drop this on an empty GameObject in an empty scene and press
    /// Play — it assembles the whole fun-core at runtime: the GameManager (sim), 18 holes built from
    /// ORGANIC procedural meshes (curved fairways/rough, kidney greens, blob bunkers, pins, trees) that
    /// read the sim's tells, a first-person Player that can walk / inspect / play, a ball + cup that
    /// move hole to hole, and every HUD wired up. No hand-wiring, no prefabs.
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
        public int holesToRender = 18;    // full holes laid out tee -> fairway -> approach -> green
        public float cellSizeM = 2.0f;

        private GameManager _game;
        private GameObject[] _treePrefabs; // real tree models from Resources/Trees/* (+ Resources/PolyHaven/Tree)
        private Material _trunkMat;        // shared so hundreds of trees don't spawn thousands of materials
        private Material[] _canopyMats;
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

                // Real tree models: drop a CC0 pack into Assets/Resources/Trees/ and they're scattered.
                var trees = new List<GameObject>();
                var folder = Resources.LoadAll<GameObject>("Trees");
                if (folder != null) trees.AddRange(folder);
                var ph = Resources.Load<GameObject>("PolyHaven/Tree");
                if (ph != null) trees.Add(ph);
                _treePrefabs = trees.ToArray();

                var holesViz = new List<HoleViz>();
                int holes = Mathf.Max(1, holesToRender);
                // Snaking grid routing: rows of 6 holes, alternate rows face back the other way (like a
                // real out-and-back nine), so the course reads as a field of holes you can walk between.
                const int cols = 6;
                const float laneW = 30f, rowD = 64f;
                for (int i = 0; i < holes; i++)
                {
                    int hole = i + 1;
                    if (_game.Course.Get($"green-{hole:00}") == null) continue;
                    int row = i / cols, col = i % cols;
                    bool back = (row % 2) == 1;            // alternate direction each row
                    float x = (back ? (cols - 1 - col) : col) * laneW;
                    float z = row * rowD;
                    float yaw = back ? 180f : 0f;          // back rows point -Z
                    var hv = BuildHole(hole, new Vector3(x, GroundY(x, z), z), yaw);
                    if (hv != null) holesViz.Add(hv);
                }
                BuildPerimeterTrees(maxXForTrees: (6 - 1) * 30f, maxZForTrees: ((holes + 5) / 6 - 1) * 64f + 50f);

                var (player, cam) = BuildPlayer();
                BuildBallAndCup(player, holesViz);
                BuildHuds(player);

                SetPlanMode(true);
                Debug.Log($"[Bootstrap] scene built: {_game.Course.Zones.Count} zones, {holesViz.Count} holes rendered " +
                          "(organic tee/fairway/approach/green/rough/bunkers + pins). TAB to walk; hold LMB to play.");
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
            // Optional Poly Haven HDRI sky (Assets/Resources/PolyHaven/Skybox.mat). Lights the scene too.
            var sky = Resources.Load<Material>("PolyHaven/Skybox");
            if (sky != null)
            {
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                RenderSettings.ambientLight = new Color(0.45f, 0.5f, 0.5f);
            }

            if (FindFirstObjectByType<Light>() != null) return;
            var go = new GameObject("Sun");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        private void BuildGround()
        {
            int rows = Mathf.CeilToInt(Mathf.Max(1, holesToRender) / 6f);
            float maxX = (6 - 1) * 30f, maxZ = (rows - 1) * 64f + 50f;
            var mesh = ProcMesh.HeightGrid(-40f, -40f, maxX + 40f, maxZ + 40f, 4f, GroundY, 6f); // UV tiles ~6 m
            var ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            // Mesh UVs carry the tiling, so use the shared material directly (no extra scale).
            ground.AddComponent<MeshRenderer>().sharedMaterial = SharedSurfaceMaterial("Ground", new Color(0.20f, 0.30f, 0.13f));
            ground.AddComponent<MeshCollider>().sharedMesh = mesh; // walkable rolling terrain
        }

        /// <summary>Rolling site elevation (metres) at a world XZ — broad hills + a little finer relief.</summary>
        private float GroundY(float x, float z)
        {
            float broad = Mathf.PerlinNoise(x * 0.012f + 11.3f, z * 0.012f + 7.1f) - 0.5f;   // ±0.5
            float fine = Mathf.PerlinNoise(x * 0.05f + 31.7f, z * 0.05f + 19.2f) - 0.5f;     // ±0.5
            return broad * 11f + fine * 1.6f; // ~±6 m of roll
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

        // ---- holes (organic, all surfaces) ----

        private sealed class HoleViz { public Transform Green; public Vector3 TeeWorld; public Vector3 CupWorld; }

        /// <summary>
        /// Lays out one whole hole from ORGANIC procedural meshes: a curved rough corridor and fairway
        /// (a dogleg spline), a rounded tee, an apron + kidney green, blob bunkers, a pin, and trees —
        /// each surface driven by its sim zone via SurfaceRenderer.
        /// </summary>
        private HoleViz BuildHole(int hole, Vector3 rootPos, float yawDeg)
        {
            string h = hole.ToString("00");
            var root = new GameObject($"Hole_{h}");
            root.transform.position = rootPos;
            root.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
            var T = root.transform;

            var rnd = new System.Random(hole * 9176 + 13);
            float R() => (float)rnd.NextDouble();

            float length = 30f + 16f * R();
            float side = R() < 0.5f ? -1f : 1f;
            float bend = (3f + 7f * R()) * side;   // dogleg

            // Curved centreline (local XZ; +z up the hole), smoothed to a polyline.
            var ctrl = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(bend * 0.25f, length * 0.35f),
                new Vector2(bend, length * 0.72f),
                new Vector2(bend * 0.95f, length),
            };
            var center = ProcMesh.Smooth(ctrl, 6);
            int m = center.Count;

            // Half-widths bulge in the middle (narrow tee + green).
            var fairHalf = new float[m];
            var roughHalf = new float[m];
            for (int i = 0; i < m; i++)
            {
                float t = i / (float)(m - 1);
                float w = Mathf.Max(2.6f, Mathf.Lerp(3.0f, 5.2f, Mathf.Sin(t * Mathf.PI)));
                fairHalf[i] = w;
                roughHalf[i] = w + 6.5f;
            }

            // Rough corridor then fairway on top — both draped onto the rolling terrain. Small lifts so
            // each surface layers cleanly on the one beneath WITHOUT floating above the ground.
            BuildMesh(T, $"rough-{h}", "Rough", ProcMesh.Ribbon(center, roughHalf, 5f), 0.0f, new Color(0.20f, 0.32f, 0.13f));
            if (_game.Course.Get($"fairway-{h}") != null)
                BuildMesh(T, $"fairway-{h}", "Fairway", ProcMesh.Ribbon(center, fairHalf, 5f), 0.03f, new Color(0.22f, 0.44f, 0.18f));

            // Tee box (barely proud of grade).
            Vector2 teeP = center[0];
            if (_game.Course.Get($"tee-{h}") != null)
                BuildBlob(T, $"tee-{h}", "Tee", teeP, ProcMesh.EllipseRadii(2.4f, 3.0f, 18, 0.10f, hole * 31 + 1, 0f), 0.05f, new Color(0.20f, 0.46f, 0.20f));

            // Green complex: apron, then a kidney green just proud of grade (no floating disc).
            Vector2 greenP = center[m - 1];
            if (_game.Course.Get($"approach-{h}") != null)
            {
                Vector2 apr = Vector2.Lerp(center[m - 2], greenP, 0.35f);
                BuildBlob(T, $"approach-{h}", "Approach", apr, ProcMesh.EllipseRadii(4.5f, 3.5f, 20, 0.10f, hole * 53 + 7, 0f), 0.04f, new Color(0.20f, 0.45f, 0.19f));
            }
            float gx = 4.2f + 1.2f * R(), gz = 4.8f + 1.4f * R();
            var greenObj = BuildBlob(T, $"green-{h}", "Green", greenP,
                ProcMesh.EllipseRadii(gx, gz, 26, 0.08f, hole * 71 + 3, 0.22f), 0.06f, new Color(0.16f, 0.42f, 0.16f));
            var gsr = greenObj.GetComponent<SurfaceRenderer>();
            gsr.spatialGreen = true; gsr.greenHalf = new Vector2(gx, gz);
            greenObj.transform.localRotation = Quaternion.Euler(2.5f + 3.0f * R(), 30f * R(), 0f); // break

            // Bunkers (organic, sunken blobs): two greenside, one on the inside of the dogleg.
            Vector2 perp = Perp(center[m - 1] - center[m - 2]);
            BuildBunker($"bunker-{h}-1", T, greenP + perp * (gx + 2.5f) + new Vector2(0f, -1.5f), hole * 11 + 1);
            BuildBunker($"bunker-{h}-2", T, greenP - perp * (gx + 2.5f) + new Vector2(0f, -2.5f), hole * 11 + 2);
            int ci = Mathf.Clamp(Mathf.RoundToInt(m * 0.6f), 1, m - 2);
            BuildBunker($"bunker-{h}-3", T, center[ci] - Perp(center[ci + 1] - center[ci - 1]) * (fairHalf[ci] + 2f) * side, hole * 11 + 3);

            // Pin + cup sit on the green surface.
            Vector3 greenWorld = T.TransformPoint(new Vector3(greenP.x, 0f, greenP.y));
            float greenSurfaceY = GroundY(greenWorld.x, greenWorld.z) + 0.06f;
            Vector3 cupLocal = new Vector3(greenP.x, greenSurfaceY - T.position.y + 0.03f, greenP.y);
            BuildPin(T, cupLocal);

            ScatterTrees(T, hole, center, roughHalf);

            Vector3 teeWorld = T.TransformPoint(new Vector3(teeP.x, 0f, teeP.y));
            float teeSurfaceY = GroundY(teeWorld.x, teeWorld.z) + 0.05f;
            return new HoleViz
            {
                Green = greenObj.transform,
                TeeWorld = new Vector3(teeWorld.x, teeSurfaceY, teeWorld.z),
                CupWorld = new Vector3(greenWorld.x, greenSurfaceY + 0.03f, greenWorld.z),
            };
        }

        private static Vector2 Perp(Vector2 d)
        {
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
            return new Vector2(-d.y, d.x);
        }

        private void BuildBunker(string zoneId, Transform parent, Vector2 p, int seed)
        {
            if (_game.Course.Get(zoneId) == null) return;
            float br = 1.4f + 0.8f * (float)new System.Random(seed).NextDouble();
            BuildBlob(parent, zoneId, "Bunker", p, ProcMesh.EllipseRadii(br, br * 0.8f, 16, 0.18f, seed, 0f), -0.05f, new Color(0.82f, 0.74f, 0.55f));
        }

        /// <summary>A rounded blob surface (green/tee/approach/bunker) centred at a local XZ point.</summary>
        private GameObject BuildBlob(Transform parent, string zoneId, string matKey, Vector2 localCenter, float[] radii, float lift, Color color)
            => BuildMeshAt(parent, zoneId, matKey, ProcMesh.Blob(radii, 4f), new Vector3(localCenter.x, 0f, localCenter.y), lift, color);

        /// <summary>A ribbon surface (fairway/rough) whose verts already live in hole-local space.</summary>
        private GameObject BuildMesh(Transform parent, string zoneId, string matKey, Mesh mesh, float lift, Color color)
            => BuildMeshAt(parent, zoneId, matKey, mesh, Vector3.zero, lift, color);

        private GameObject BuildMeshAt(Transform parent, string zoneId, string matKey, Mesh mesh, Vector3 localPos, float lift, Color color)
        {
            var go = new GameObject($"Surf_{zoneId}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            DrapeOntoTerrain(mesh, parent, localPos, lift); // follow the rolling ground (+lift)
            JitterUV(mesh, zoneId.GetHashCode());           // rotate/offset UVs so neighbours don't match
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = SharedSurfaceMaterial(matKey, color);
            go.AddComponent<MeshCollider>().sharedMesh = mesh; // walkable + ball/look raycast target
            var sr = go.AddComponent<SurfaceRenderer>();
            sr.game = _game;
            sr.zoneId = zoneId;
            return go;
        }

        /// <summary>Set each vertex's local Y so the surface sits on the terrain (GroundY) plus a lift.</summary>
        private void DrapeOntoTerrain(Mesh mesh, Transform root, Vector3 localPos, float lift)
        {
            var verts = mesh.vertices;
            float rootY = root.position.y;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 world = root.TransformPoint(localPos + new Vector3(verts[i].x, 0f, verts[i].z));
                verts[i].y = GroundY(world.x, world.z) + lift - rootY - localPos.y;
            }
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>Rotate + offset a mesh's UVs by a deterministic per-surface amount, so two adjacent
        /// surfaces using the same texture don't line up into an obvious repeating grid.</summary>
        private static void JitterUV(Mesh mesh, int seed)
        {
            var rnd = new System.Random(seed);
            float ang = (float)rnd.NextDouble() * Mathf.PI * 2f, c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            var off = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
            var uv = mesh.uv;
            for (int i = 0; i < uv.Length; i++)
            {
                var u = uv[i];
                uv[i] = new Vector2(u.x * c - u.y * s, u.x * s + u.y * c) + off;
            }
            mesh.uv = uv;
        }

        private void BuildPin(Transform parent, Vector3 localBase)
        {
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pin";
            Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(parent, false);
            pole.transform.localPosition = localBase + new Vector3(0f, 0.9f, 0f);
            pole.transform.localScale = new Vector3(0.03f, 0.9f, 0.03f); // ~1.8 m flagstick
            var pm = SolidMaterial(new Color(0.92f, 0.92f, 0.92f)); if (pm != null) pole.GetComponent<Renderer>().material = pm;

            var flag = GameObject.CreatePrimitive(PrimitiveType.Quad);
            flag.name = "Flag";
            Destroy(flag.GetComponent<Collider>());
            flag.transform.SetParent(parent, false);
            flag.transform.localPosition = localBase + new Vector3(0.28f, 1.62f, 0f);
            flag.transform.localScale = new Vector3(0.55f, 0.35f, 1f);
            flag.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var fm = SolidMaterial(new Color(0.85f, 0.12f, 0.14f)); if (fm != null) flag.GetComponent<Renderer>().material = fm;
        }

        private void ScatterTrees(Transform parent, int hole, List<Vector2> center, float[] roughHalf)
        {
            var rnd = new System.Random(hole * 2237 + 5);
            int m = center.Count;
            for (int i = 1; i < m; i += 2)
            {
                Vector2 perp = Perp(center[Mathf.Min(m - 1, i + 1)] - center[Mathf.Max(0, i - 1)]);
                for (int s = -1; s <= 1; s += 2)
                {
                    if (rnd.NextDouble() < 0.4) continue;
                    float off = roughHalf[i] + 1.5f + 4f * (float)rnd.NextDouble();
                    Vector2 lp = center[i] + perp * off * s;
                    Vector3 world = parent.TransformPoint(new Vector3(lp.x, 0f, lp.y));
                    PlaceTree(parent, new Vector3(lp.x, GroundY(world.x, world.z) - parent.position.y, lp.y), rnd);
                }
            }
        }

        /// <summary>A ring/clusters of trees around the whole course (the forest surrounds in the reference).</summary>
        private void BuildPerimeterTrees(float maxXForTrees, float maxZForTrees)
        {
            var grove = new GameObject("Trees").transform;
            var rnd = new System.Random(909);
            for (int i = 0; i < 600; i++)
            {
                float x = Mathf.Lerp(-34f, maxXForTrees + 34f, (float)rnd.NextDouble());
                float z = Mathf.Lerp(-34f, maxZForTrees + 34f, (float)rnd.NextDouble());
                // Keep the broad interior clear-ish; trees mass toward the edges and between lanes.
                float edge = Mathf.Min(Mathf.Min(x + 34f, maxXForTrees + 34f - x), Mathf.Min(z + 34f, maxZForTrees + 34f - z));
                bool nearEdge = edge < 28f;
                if (!nearEdge && rnd.NextDouble() < 0.78) continue; // sparse inside, dense at edges
                PlaceTree(grove, new Vector3(x, GroundY(x, z), z), rnd);
            }
        }

        private void PlaceTree(Transform parent, Vector3 localOrWorldPos, System.Random rnd)
        {
            GameObject tree = (_treePrefabs != null && _treePrefabs.Length > 0)
                ? Instantiate(_treePrefabs[rnd.Next(_treePrefabs.Length)], parent)
                : MakeProceduralTree(parent, rnd);
            tree.transform.localPosition = localOrWorldPos;
            tree.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * (0.85f + 0.7f * (float)rnd.NextDouble());
        }

        /// <summary>A simple low-poly tree from primitives (used when no Poly Haven Tree prefab is imported).</summary>
        private GameObject MakeProceduralTree(Transform parent, System.Random rnd)
        {
            if (_trunkMat == null)
            {
                _trunkMat = SolidMaterial(new Color(0.32f, 0.22f, 0.12f));
                _canopyMats = new[]
                {
                    SolidMaterial(new Color(0.10f, 0.34f, 0.13f)),
                    SolidMaterial(new Color(0.13f, 0.40f, 0.15f)),
                    SolidMaterial(new Color(0.09f, 0.29f, 0.12f)),
                };
            }

            var root = new GameObject("Tree");
            root.transform.SetParent(parent, false);

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(trunk.GetComponent<Collider>());
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            trunk.transform.localScale = new Vector3(0.25f, 1.1f, 0.25f);
            if (_trunkMat != null) trunk.GetComponent<Renderer>().sharedMaterial = _trunkMat;

            var canopyMat = _canopyMats[rnd.Next(_canopyMats.Length)];
            int blobs = 1 + rnd.Next(0, 2);
            for (int i = 0; i < blobs; i++)
            {
                var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(canopy.GetComponent<Collider>());
                canopy.transform.SetParent(root.transform, false);
                float r = 1.6f + 0.6f * (float)rnd.NextDouble();
                canopy.transform.localPosition = new Vector3((float)(rnd.NextDouble() - 0.5) * 0.8f, 2.4f + i * 0.9f, (float)(rnd.NextDouble() - 0.5) * 0.8f);
                canopy.transform.localScale = new Vector3(r, r * 1.2f, r);
                if (canopyMat != null) canopy.GetComponent<Renderer>().sharedMaterial = canopyMat;
            }
            return root;
        }

        // ---- player ----

        private (GameObject player, Camera cam) BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, GroundY(0f, 0f) + 1.3f, 0f); // stand on hole-1 tee
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

        private void BuildBallAndCup(GameObject player, List<HoleViz> holes)
        {
            if (holes.Count == 0) return;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.localScale = Vector3.one * 0.12f;
            Destroy(ball.GetComponent<Collider>()); // ball is kinematic-animated
            var ballMat = SolidMaterial(Color.white); if (ballMat != null) ball.GetComponent<Renderer>().material = ballMat;

            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            cup.transform.localScale = new Vector3(0.11f, 0.02f, 0.11f);
            Destroy(cup.GetComponent<Collider>());
            var cupMat = SolidMaterial(Color.black); if (cupMat != null) cup.GetComponent<Renderer>().material = cupMat;

            var tees = new Vector3[holes.Count];
            var cups = new Vector3[holes.Count];
            for (int i = 0; i < holes.Count; i++) { tees[i] = holes[i].TeeWorld; cups[i] = holes[i].CupWorld; }

            var putt = player.GetComponent<PuttingController>();
            putt.ball = ball.transform;
            putt.cup = cup.transform;
            putt.teePositions = tees;
            putt.cupPositions = cups;
            putt.SetHole(0); // tee up on hole 1
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

        // ---- Poly Haven (optional) ----
        // Drop CC0 materials into Assets/Resources/PolyHaven/<key>.mat and they're used automatically;
        // missing ones fall back to the flat colour. SurfaceRenderer still tints them by turf health.

        /// <summary>A per-quad material: the textured Poly Haven material if present (tiled to size), else a flat colour.</summary>
        private static Material SurfaceMat(string key, Color fallback, float sizeX, float sizeZ)
        {
            var loaded = LoadSurfaceMaterial(key);
            if (loaded == null) return SolidMaterial(fallback);
            var m = new Material(loaded);                 // per-quad instance so tiling can match its size
            const float tileMetres = 2.0f;               // one texture repeat ≈ every 2 m
            var scale = new Vector2(Mathf.Max(1f, sizeX / tileMetres), Mathf.Max(1f, sizeZ / tileMetres));
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", scale); // URP lit
            m.mainTextureScale = scale;                  // Built-in / fallback
            return m;
        }

        /// <summary>Shared material for a procedural-mesh surface (UVs carry the tiling; colour set per-renderer via MPB).</summary>
        private static Material SharedSurfaceMaterial(string key, Color color)
        {
            var loaded = LoadSurfaceMaterial(key);
            return loaded != null ? loaded : SolidMaterial(color);
        }

        /// <summary>
        /// Load a surface's material, falling back through closely-related surfaces so you don't have to
        /// import the same grass for every key — e.g. a single "Fairway" import also dresses tees,
        /// approaches and greens. Import a key explicitly to override the inheritance.
        /// </summary>
        private static Material LoadSurfaceMaterial(string key)
        {
            foreach (var k in MaterialFallbacks(key))
            {
                var m = Resources.Load<Material>($"PolyHaven/{k}");
                if (m != null) return m;
            }
            return null;
        }

        private static string[] MaterialFallbacks(string key)
        {
            switch (key)
            {
                case "Tee":      return new[] { "Tee", "Fairway", "Approach" };
                case "Approach": return new[] { "Approach", "Fairway" };
                case "Green":    return new[] { "Green", "Approach", "Fairway" };
                case "Fairway":  return new[] { "Fairway" };
                case "Rough":    return new[] { "Rough", "Ground" };
                case "Ground":   return new[] { "Ground", "Rough" };
                default:         return new[] { key }; // Bunker, etc.
            }
        }

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
