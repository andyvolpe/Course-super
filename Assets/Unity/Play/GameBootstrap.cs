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
        public bool enableTerrainDetailGrass = false; // URP renders terrain detail grass dark — off by default
        public bool enableGrassObjects = true;        // lit grass MESHES scattered as GameObjects in the rough
        public int maxGrassObjects = 3000;            // cap so 18 holes of rough grass stays cheap

        // Real-scale layout (metres). Holes are 100–530 m long, so the grid cells are big.
        private const int Cols = 6;
        private const float LaneW = 110f;   // spacing between holes across (each hole ~30 m playing + rough/trees)
        private const float RowD = 600f;    // spacing down — must exceed the longest hole (~526 m) + green
        private const float YardM = 0.9144f;
        // 18-hole par mix: 12 par-4, 3 par-3, 3 par-5.
        private static readonly int[] Pars = { 4, 4, 3, 4, 5, 4, 4, 3, 4, 4, 5, 4, 3, 4, 4, 5, 4, 4 };
        private static int ParForHole(int hole) => Pars[(hole - 1) % Pars.Length];

        private GameManager _game;
        private Terrain _terrain;          // Unity Terrain ground (null => mesh-ground fallback)
        private float _terrMinX, _terrMinZ, _terrW = 1f, _terrL = 1f;
        private float[,] _grassMask;       // [z,x]; 1 = short maintained surface — keep tall grass OFF it
        private const int GrassMaskRes = 384;
        private GameObject[] _treePrefabs; // real tree models from Resources/Trees/* (+ Resources/PolyHaven/Tree)
        private GameObject[] _grassPrefabs; // real grass meshes from Resources/Grass/* (scattered, not terrain detail)
        private Material _trunkMat;        // shared so hundreds of trees don't spawn thousands of materials
        private Material[] _canopyMats;
        private Dictionary<string, Material> _turfMats; // generated per-surface turf materials (cached)
        private FirstPersonController _fp;
        private GreenInspectionController _inspect;
        private PuttingController _putt;
        private RoomInteractionController _interaction; // owns cursor/look gating now (diegetic UI)
        private Vector3 _roomSpawn;                      // where the player wakes up inside the building
        private Vector3 _roomCenter;                     // world centre of the maintenance building
        private Vector2 _roomHalf = new Vector2(6f, 5f); // interior half-extents (X,Z)
        private const float RoomCx = -22f, RoomCz = 4f;  // building location (kept clear of trees/grass)
        private string _error;

        /// <summary>True when a diegetic panel is open (cursor free) — kept for the course HUD's layout.</summary>
        public bool PlanMode => _interaction != null && _interaction.PanelOpen;

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

                // Real tree models: drop a CC0 pack into Assets/Resources/Trees/ and they're used.
                var trees = new List<GameObject>();
                var folder = Resources.LoadAll<GameObject>("Trees");
                if (folder != null) trees.AddRange(folder);
                var ph = Resources.Load<GameObject>("PolyHaven/Tree");
                if (ph != null) trees.Add(ph);
                _treePrefabs = trees.ToArray();

                // Real LIT grass meshes (NatureManufacture etc.) scattered as GameObjects in the rough —
                // not terrain detail, which URP renders near-black. Empty folder = scatter is skipped.
                _grassPrefabs = Resources.LoadAll<GameObject>("Grass") ?? new GameObject[0];

                BuildGround(); // Unity Terrain (heightmap + splat + terrain trees), or mesh-ground fallback
                _game = BuildGameManager();
                if (_game == null || _game.Course == null) { _error = "GameManager/course failed to build."; return; }

                var holesViz = new List<HoleViz>();
                int holes = Mathf.Max(1, holesToRender);
                // Grid routing: rows of 6 holes, all teeing toward +Z. Holes are long (up to ~526 m), so
                // rows are spaced far enough apart that holes never overlap.
                for (int i = 0; i < holes; i++)
                {
                    int hole = i + 1;
                    if (_game.Course.Get($"green-{hole:00}") == null) continue;
                    int row = i / Cols, col = i % Cols;
                    float x = col * LaneW, z = row * RowD;
                    var hv = BuildHole(hole, new Vector3(x, SurfaceGroundY(x, z), z), 0f);
                    if (hv != null) holesViz.Add(hv);
                }
                // Perimeter forest: Terrain tree instances handle it when a pack is imported; otherwise
                // (mesh ground, or terrain but no tree models) scatter our own so it's never barren.
                if (_terrain == null || _treePrefabs == null || _treePrefabs.Length == 0)
                    BuildPerimeterTrees(maxXForTrees: (Cols - 1) * LaneW, maxZForTrees: ((holes + Cols - 1) / Cols - 1) * RowD + 540f);

                // Keep the maintenance building footprint clear of scattered trees/grass.
                StampGrassMask(RoomCx, RoomCz, 10f);

                // Terrain trees (after holes so the mask is filled — they avoid the surfaces). Detail
                // grass is OFF by default: URP renders terrain detail grass (esp. imported mesh grass)
                // near-black, which shows as dark patches. Splat ground + trees carry the look.
                if (_terrain != null)
                {
                    PlaceTerrainTrees(_terrain);
                    if (enableTerrainDetailGrass) ApplyGrassDetail(_terrain);
                }

                // Lit grass mesh tufts in the rough fringe around every hole (URP-safe; terrain detail is dark).
                if (enableGrassObjects) ScatterGrassObjects();

                BuildRoom(); // the maintenance building the day starts in (diegetic interface)

                var (player, cam) = BuildPlayer();
                BuildBallAndCup(player, holesViz);
                BuildHuds(player);

                Debug.Log($"[Bootstrap] scene built: {_game.Course.Zones.Count} zones, {holesViz.Count} holes rendered. " +
                          "Day starts in the maintenance building — E to use objects, walk out the door to the course.");
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
            try { _terrain = BuildTerrain(); }
            catch (System.Exception e) { Debug.LogWarning("[Bootstrap] Terrain build failed, using mesh ground: " + e.Message); _terrain = null; }
            if (_terrain == null) BuildMeshGround();
        }

        /// <summary>Rolling site elevation (metres) at a world XZ — broad hills + a little finer relief.</summary>
        private float GroundY(float x, float z)
        {
            float broad = Mathf.PerlinNoise(x * 0.010f + 11.3f, z * 0.010f + 7.1f) - 0.5f;   // ±0.5
            float fine = Mathf.PerlinNoise(x * 0.04f + 31.7f, z * 0.04f + 19.2f) - 0.5f;     // ±0.5
            return broad * 9f + fine * 0.7f; // gentle roll (~±5 m), smooth enough for clean greens
        }

        /// <summary>Height the SURFACES drape onto — the live Terrain if present, else the GroundY function.</summary>
        private float SurfaceGroundY(float x, float z)
            => _terrain != null ? _terrain.SampleHeight(new Vector3(x, 0f, z)) + _terrain.transform.position.y : GroundY(x, z);

        // ---- Unity Terrain ----

        private void BuildBounds(out float minX, out float minZ, out float width, out float length)
        {
            int rows = Mathf.CeilToInt(Mathf.Max(1, holesToRender) / (float)Cols);
            minX = -100f; minZ = -100f;
            width = (Cols - 1) * LaneW + 200f;
            length = (rows - 1) * RowD + 560f + 200f; // + longest hole + margins
        }

        /// <summary>
        /// Build a real Unity Terrain: heightmap from GroundY, 3 splat layers blended by noise (so the
        /// grass isn't an obvious repeating tile), a TerrainCollider, and terrain trees from the imported
        /// pack massed at the edges. Returns null on any failure so BuildGround can fall back to a mesh.
        /// </summary>
        private Terrain BuildTerrain()
        {
            BuildBounds(out float minX, out float minZ, out float width, out float length);
            _terrMinX = minX; _terrMinZ = minZ; _terrW = width; _terrL = length;
            _grassMask = new float[GrassMaskRes, GrassMaskRes]; // filled while holes are built
            const float minY = -10f, sizeY = 20f;

            var data = new TerrainData { heightmapResolution = 513 }; // finer -> smoother hills
            data.size = new Vector3(width, sizeY, length);

            // Heights (heights[y,x]; y runs along Z/length, x along width).
            int hr = data.heightmapResolution;
            var heights = new float[hr, hr];
            for (int y = 0; y < hr; y++)
                for (int x = 0; x < hr; x++)
                {
                    float wx = minX + width * x / (hr - 1);
                    float wz = minZ + length * y / (hr - 1);
                    heights[y, x] = Mathf.Clamp01((GroundY(wx, wz) - minY) / sizeY);
                }
            data.SetHeights(0, 0, heights);

            // Splat layers: rough grass (base), fairway grass (patches), dirt (rare) — blended by noise.
            data.terrainLayers = new[]
            {
                Layer("Rough", new Color(0.20f, 0.31f, 0.13f), 9f),
                Layer("Fairway", new Color(0.24f, 0.44f, 0.18f), 7f),
                Layer("Ground", new Color(0.33f, 0.27f, 0.16f), 6f),
            };
            int ar = 512; data.alphamapResolution = ar; // finer + gentler blends -> soft transitions
            var alpha = new float[ar, ar, 3];
            for (int y = 0; y < ar; y++)
                for (int x = 0; x < ar; x++)
                {
                    float wx = minX + width * x / (ar - 1);
                    float wz = minZ + length * y / (ar - 1);
                    // Broad, gradual patches (low sharpening = wide feathered transitions); dirt rare.
                    float fair = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(wx * 0.018f + 4f, wz * 0.018f + 9f) - 0.5f) * 2.2f);
                    float dirt = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(wx * 0.05f + 40f, wz * 0.05f + 70f) - 0.80f) * 3.0f);
                    float rough = 0.9f;
                    float sum = rough + fair + dirt;
                    alpha[y, x, 0] = rough / sum;
                    alpha[y, x, 1] = fair / sum;
                    alpha[y, x, 2] = dirt / sum;
                }
            data.SetAlphamaps(0, 0, alpha);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(minX, minY, minZ);
            var terrain = go.GetComponent<Terrain>(); // keep the pipeline's default terrain material
            return terrain; // trees + grass are placed AFTER the holes (so they can avoid the surfaces)
        }

        private TerrainLayer Layer(string key, Color fallback, float tileSize)
        {
            // Prefer a ready-made TerrainLayer (e.g. a NatureManufacture ground) dropped in by name.
            var nm = Resources.Load<TerrainLayer>($"TerrainLayers/{key}");
            if (nm != null) return nm;
            // Else use the imported material's texture, or a generated turf/dirt (coloured, since terrain
            // layers aren't tinted at runtime).
            var mat = LoadSurfaceMaterial(key);
            Texture2D tex = mat != null ? (mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture) as Texture2D : null;
            if (tex == null)
                tex = key == "Ground"
                    ? TurfTexture(91, new Color(0.34f, 0.27f, 0.16f), 0.30f, 0, 0f)   // dirt
                    : TurfTexture(key.GetHashCode(), new Color(0.22f, 0.40f, 0.18f), 0.22f, 0, 0f); // grass
            return new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tileSize, tileSize) };
        }

        /// <summary>Terrain tree instances massed at the edges/between holes — kept OFF every maintained
        /// surface via the grass mask (with a margin), so no trunk grows on a green/fairway/tee/bunker.</summary>
        private void PlaceTerrainTrees(Terrain terrain)
        {
            if (_treePrefabs == null || _treePrefabs.Length == 0) return;
            var data = terrain.terrainData;
            var protos = new TreePrototype[_treePrefabs.Length];
            for (int i = 0; i < _treePrefabs.Length; i++) protos[i] = new TreePrototype { prefab = _treePrefabs[i] };
            data.treePrototypes = protos;

            var rnd = new System.Random(909);
            var list = new List<TreeInstance>();
            for (int i = 0; i < 3500; i++)
            {
                float nx = (float)rnd.NextDouble(), nz = (float)rnd.NextDouble();
                int mx = Mathf.Clamp(Mathf.RoundToInt(nx * (GrassMaskRes - 1)), 0, GrassMaskRes - 1);
                int mz = Mathf.Clamp(Mathf.RoundToInt(nz * (GrassMaskRes - 1)), 0, GrassMaskRes - 1);
                if (MaskedNear(mx, mz, 4)) continue;   // never on/near a maintained surface
                float edge = Mathf.Min(Mathf.Min(nx, 1f - nx), Mathf.Min(nz, 1f - nz));
                if (edge > 0.16f && rnd.NextDouble() < 0.80) continue; // dense at the edges, sparse inside
                list.Add(new TreeInstance
                {
                    position = new Vector3(nx, 0f, nz),         // normalised over the terrain
                    prototypeIndex = rnd.Next(protos.Length),
                    widthScale = 0.85f + 0.6f * (float)rnd.NextDouble(),
                    heightScale = 0.85f + 0.6f * (float)rnd.NextDouble(),
                    rotation = (float)rnd.NextDouble() * 6.283f,
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            data.SetTreeInstances(list.ToArray(), true);
        }

        /// <summary>True if any mask cell within <paramref name="r"/> cells marks a maintained surface.</summary>
        private bool MaskedNear(int cx, int cz, int r)
        {
            if (_grassMask == null) return false;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= GrassMaskRes || z >= GrassMaskRes) continue;
                    if (_grassMask[z, x] > 0.5f) return true;
                }
            return false;
        }

        // ---- lit grass MESHES scattered as GameObjects (URP-safe) ----

        /// <summary>
        /// Scatter real lit grass meshes (Resources/Grass — NatureManufacture etc.) as GameObjects through
        /// the ROUGH fringe that rings every maintained surface, clumped by noise and capped for cost. This
        /// is the URP-safe alternative to terrain detail grass (which renders near-black). Off the short
        /// surfaces via the grass mask; each tuft gets a random yaw + size so the rough never tiles.
        /// </summary>
        private void ScatterGrassObjects()
        {
            if (_grassMask == null || _grassPrefabs == null || _grassPrefabs.Length == 0) return;
            try
            {
                var parent = new GameObject("RoughGrass").transform;
                var rnd = new System.Random(4242);
                int placed = 0;
                // Walk the mask on a stride (~ every 2 cells ≈ 4 m); a cell qualifies if it's OFF a surface
                // but within the rough band (within ~8 cells of one), so grass hugs the holes, not the void.
                const int stride = 2, bandCells = 8;
                for (int z = 0; z < GrassMaskRes && placed < maxGrassObjects; z += stride)
                    for (int x = 0; x < GrassMaskRes && placed < maxGrassObjects; x += stride)
                    {
                        if (_grassMask[z, x] > 0.5f) continue;          // on a short surface
                        if (!MaskedNear(x, z, bandCells)) continue;     // not in the rough around a hole
                        float u = x / (float)(GrassMaskRes - 1), v = z / (float)(GrassMaskRes - 1);
                        float wx = _terrMinX + u * _terrW, wz = _terrMinZ + v * _terrL;
                        float clump = Mathf.PerlinNoise(wx * 0.12f + 3f, wz * 0.12f + 8f);
                        if (clump < 0.45f) continue;                    // clumpy, not a uniform carpet
                        if (rnd.NextDouble() > clump) continue;         // thinner at clump edges
                        // jitter within the cell so the grid never shows
                        float jx = (float)(rnd.NextDouble() - 0.5) * (_terrW / GrassMaskRes) * stride;
                        float jz = (float)(rnd.NextDouble() - 0.5) * (_terrL / GrassMaskRes) * stride;
                        float px = wx + jx, pz = wz + jz;
                        var prefab = _grassPrefabs[rnd.Next(_grassPrefabs.Length)];
                        var g = Instantiate(prefab, new Vector3(px, SurfaceGroundY(px, pz), pz),
                                            Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), parent);
                        float s = 0.7f + (float)rnd.NextDouble() * 0.8f; // 0.7..1.5 size variety
                        g.transform.localScale = new Vector3(s, s * (0.9f + (float)rnd.NextDouble() * 0.5f), s);
                        placed++;
                    }
                Debug.Log($"[Bootstrap] scattered {placed} lit grass tufts in the rough.");
            }
            catch (System.Exception e) { Debug.LogWarning("[Bootstrap] grass scatter skipped: " + e.Message); }
        }

        // ---- detail (geometry) grass on the Terrain ----

        /// <summary>Mark a world-space disc as a short maintained surface (no tall grass grows there).</summary>
        private void StampGrassMask(float wx, float wz, float radius)
        {
            if (_grassMask == null) return;
            float u = (wx - _terrMinX) / _terrW, v = (wz - _terrMinZ) / _terrL;
            int cx = Mathf.RoundToInt(u * (GrassMaskRes - 1)), cz = Mathf.RoundToInt(v * (GrassMaskRes - 1));
            int rx = Mathf.CeilToInt(radius / _terrW * GrassMaskRes);
            int rz = Mathf.CeilToInt(radius / _terrL * GrassMaskRes);
            for (int dz = -rz; dz <= rz; dz++)
                for (int dx = -rx; dx <= rx; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= GrassMaskRes || z >= GrassMaskRes) continue;
                    if ((dx / (float)Mathf.Max(1, rx)) * (dx / (float)Mathf.Max(1, rx)) +
                        (dz / (float)Mathf.Max(1, rz)) * (dz / (float)Mathf.Max(1, rz)) <= 1f)
                        _grassMask[z, x] = 1f;
                }
        }

        /// <summary>Paint detail (geometry) grass across the rough + surrounds, off the short surfaces.
        /// Uses real grass from Resources/Grass/ (NatureManufacture etc.) — mesh prefabs and/or billboard
        /// textures — and falls back to a generated grass billboard when that folder is empty.</summary>
        private void ApplyGrassDetail(Terrain terrain)
        {
            if (_grassMask == null) return;
            try
            {
                var data = terrain.terrainData;
                int res = 1024; // finer over the large real-scale course so grass isn't sparse
                data.SetDetailResolution(res, 32);

                var protos = new List<DetailPrototype>();
                foreach (var go in Resources.LoadAll<GameObject>("Grass"))      // real grass MESHES (NM etc.)
                    protos.Add(new DetailPrototype
                    {
                        prototype = go, usePrototypeMesh = true, renderMode = DetailRenderMode.Grass,
                        healthyColor = Color.white, dryColor = new Color(0.82f, 0.82f, 0.6f),
                        minWidth = 0.7f, maxWidth = 1.4f, minHeight = 0.5f, maxHeight = 1.2f, noiseSpread = 0.4f,
                    });
                foreach (var tex in Resources.LoadAll<Texture2D>("Grass"))      // grass BILLBOARD textures
                    protos.Add(new DetailPrototype
                    {
                        prototypeTexture = tex, renderMode = DetailRenderMode.GrassBillboard,
                        healthyColor = new Color(0.45f, 0.6f, 0.32f), dryColor = new Color(0.55f, 0.55f, 0.32f),
                        minWidth = 0.5f, maxWidth = 1.1f, minHeight = 0.3f, maxHeight = 0.8f, noiseSpread = 0.4f,
                    });
                if (protos.Count == 0) // generated fallback so it's never bare
                    protos.Add(new DetailPrototype
                    {
                        prototypeTexture = MakeGrassBillboardTex(), renderMode = DetailRenderMode.GrassBillboard,
                        healthyColor = new Color(0.42f, 0.58f, 0.30f), dryColor = new Color(0.55f, 0.55f, 0.32f),
                        minWidth = 0.5f, maxWidth = 1.1f, minHeight = 0.3f, maxHeight = 0.8f, noiseSpread = 0.4f,
                    });
                data.detailPrototypes = protos.ToArray();

                int n = protos.Count;
                var maps = new int[n][,];
                for (int i = 0; i < n; i++) maps[i] = new int[res, res];
                var rnd = new System.Random(123);
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        float u = x / (res - 1f), v = z / (res - 1f);
                        int mx = Mathf.Clamp(Mathf.RoundToInt(u * (GrassMaskRes - 1)), 0, GrassMaskRes - 1);
                        int mz = Mathf.Clamp(Mathf.RoundToInt(v * (GrassMaskRes - 1)), 0, GrassMaskRes - 1);
                        if (_grassMask[mz, mx] > 0.5f) continue; // short surface — no tall grass
                        float wx = _terrMinX + u * _terrW, wz = _terrMinZ + v * _terrL;
                        float nz = Mathf.PerlinNoise(wx * 0.09f + 5f, wz * 0.09f + 9f);
                        if (nz <= 0.42f) continue;
                        maps[rnd.Next(n)][z, x] = Mathf.RoundToInt(nz * 7f); // clumpy; spread across prototypes
                    }
                for (int i = 0; i < n; i++) data.SetDetailLayer(0, 0, i, maps[i]);

                terrain.detailObjectDistance = 180f;
                data.wavingGrassStrength = 0.35f;
                data.wavingGrassSpeed = 0.5f;
                data.wavingGrassAmount = 0.3f;
                data.wavingGrassTint = new Color(0.7f, 0.75f, 0.5f, 1f);
            }
            catch (System.Exception e) { Debug.LogWarning("[Bootstrap] detail grass skipped: " + e.Message); }
        }

        /// <summary>A small grass-tuft billboard (a few alpha'd green blades on transparent background).</summary>
        private static Texture2D MakeGrassBillboardTex()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            var rnd = new System.Random(7);
            int blades = 7;
            for (int b = 0; b < blades; b++)
            {
                int baseX = 6 + rnd.Next(S - 12);
                int top = 30 + rnd.Next(28);
                float lean = (float)(rnd.NextDouble() - 0.5) * 10f;
                for (int y = 2; y < top; y++)
                {
                    float ty = (y - 2f) / (top - 2f);
                    int w = Mathf.Max(0, Mathf.RoundToInt(1.6f * (1f - ty))); // taper to a point
                    int cx = baseX + Mathf.RoundToInt(lean * ty);
                    var col = Color.Lerp(new Color(0.13f, 0.30f, 0.10f), new Color(0.34f, 0.52f, 0.22f), ty);
                    for (int dx = -w; dx <= w; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= S) continue;
                        px[y * S + x] = col;
                    }
                }
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Fallback ground when Terrain isn't available: the rolling height mesh.</summary>
        private void BuildMeshGround()
        {
            BuildBounds(out float minX, out float minZ, out float width, out float length);
            var mesh = ProcMesh.HeightGrid(minX, minZ, minX + width, minZ + length, 4f, GroundY, 6f);
            var ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = SharedSurfaceMaterial("Ground", new Color(0.20f, 0.30f, 0.13f));
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
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

            // Real-scale length by par (yards -> metres). Par 3 plays straight; 4/5 can dogleg.
            int par = ParForHole(hole);
            float yards = par == 3 ? Mathf.Lerp(130f, 195f, R())
                        : par == 5 ? Mathf.Lerp(480f, 560f, R())
                                   : Mathf.Lerp(310f, 450f, R());
            float length = yards * YardM;
            float side = R() < 0.5f ? -1f : 1f;
            float bend = par == 3 ? 0f : Mathf.Lerp(0.05f, 0.13f, R()) * length * side; // lateral dogleg

            // Curved centreline (local XZ; +z up the hole), smoothed to a polyline.
            var ctrl = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(bend * 0.25f, length * 0.40f),
                new Vector2(bend, length * 0.75f),
                new Vector2(bend * 0.95f, length),
            };
            var center = ProcMesh.Smooth(ctrl, Mathf.Clamp(Mathf.RoundToInt(length / 12f), 10, 48));
            int m = center.Count;

            // Half-widths bulge in the middle (~28 m fairway), narrowing at the tee + green; rough flanks it.
            var fairHalf = new float[m];
            var roughHalf = new float[m];
            for (int i = 0; i < m; i++)
            {
                float t = i / (float)(m - 1);
                float w = Mathf.Max(9f, Mathf.Lerp(11f, 16f, Mathf.Sin(t * Mathf.PI)));
                fairHalf[i] = w;
                roughHalf[i] = w + 14f;
            }

            // Rough corridor then fairway on top — both draped onto the rolling terrain. Tiny lifts so
            // each surface layers cleanly on the one beneath without floating above the ground.
            BuildMesh(T, $"rough-{h}", "Rough", ProcMesh.Ribbon(center, roughHalf, 5f), 0.0f, new Color(0.20f, 0.32f, 0.13f));
            if (_game.Course.Get($"fairway-{h}") != null)
                BuildMesh(T, $"fairway-{h}", "Fairway", ProcMesh.Ribbon(center, fairHalf, 5f), 0.02f, new Color(0.22f, 0.44f, 0.18f));

            // Tee box (~7.3 x 11 m).
            Vector2 teeP = center[0];
            if (_game.Course.Get($"tee-{h}") != null)
                BuildBlob(T, $"tee-{h}", "Tee", teeP, ProcMesh.EllipseRadii(3.7f, 5.5f, 28, 0.05f, hole * 31 + 1, 0f), 0.04f, new Color(0.20f, 0.46f, 0.20f));

            // Green complex: apron, then a kidney green (~23 m across) just proud of grade.
            Vector2 greenP = center[m - 1];
            if (_game.Course.Get($"approach-{h}") != null)
            {
                Vector2 apr = Vector2.Lerp(center[m - 2], greenP, 0.3f);
                BuildBlob(T, $"approach-{h}", "Approach", apr, ProcMesh.EllipseRadii(9f, 7f, 36, 0.06f, hole * 53 + 7, 0f), 0.03f, new Color(0.20f, 0.45f, 0.19f));
                Vector3 aw = T.TransformPoint(new Vector3(apr.x, 0f, apr.y));
                StampGrassMask(aw.x, aw.z, 10f);
            }
            float gx = 10.5f + 2f * R(), gz = 11f + 2.5f * R(); // ~21–27 m diameter
            var greenObj = BuildBlob(T, $"green-{h}", "Green", greenP,
                ProcMesh.EllipseRadii(gx, gz, 72, 0.04f, hole * 71 + 3, 0.18f), 0.04f, new Color(0.16f, 0.42f, 0.16f));
            var gsr = greenObj.GetComponent<SurfaceRenderer>();
            gsr.spatialGreen = true; gsr.greenHalf = new Vector2(gx, gz);
            greenObj.transform.localRotation = Quaternion.Euler(0f, 30f * R(), 0f); // yaw only — tilt caused floating

            // Bunkers (organic, sunken blobs): two greenside, one on the inside of the dogleg.
            Vector2 perp = Perp(center[m - 1] - center[m - 2]);
            BuildBunker($"bunker-{h}-1", T, greenP + perp * (gx + 4f) + new Vector2(0f, -3f), hole * 11 + 1);
            BuildBunker($"bunker-{h}-2", T, greenP - perp * (gx + 4f) + new Vector2(0f, -5f), hole * 11 + 2);
            int ci = Mathf.Clamp(Mathf.RoundToInt(m * 0.6f), 1, m - 2);
            BuildBunker($"bunker-{h}-3", T, center[ci] - Perp(center[ci + 1] - center[ci - 1]) * (fairHalf[ci] + 4f) * side, hole * 11 + 3);

            // Pin + cup sit on the green surface.
            Vector3 greenWorld = T.TransformPoint(new Vector3(greenP.x, 0f, greenP.y));
            float greenSurfaceY = SurfaceGroundY(greenWorld.x, greenWorld.z) + 0.04f;
            Vector3 cupLocal = new Vector3(greenP.x, greenSurfaceY - T.position.y + 0.03f, greenP.y);
            BuildPin(T, cupLocal);

            ScatterTrees(T, hole, center, roughHalf);

            Vector3 teeWorld = T.TransformPoint(new Vector3(teeP.x, 0f, teeP.y));
            float teeSurfaceY = SurfaceGroundY(teeWorld.x, teeWorld.z) + 0.04f;

            // Keep tall detail grass OFF the short maintained surfaces (fairway corridor, tee, green).
            for (int i = 0; i < m; i++)
            {
                Vector3 w = T.TransformPoint(new Vector3(center[i].x, 0f, center[i].y));
                StampGrassMask(w.x, w.z, fairHalf[i] + 1f);
            }
            StampGrassMask(teeWorld.x, teeWorld.z, 7f);
            StampGrassMask(greenWorld.x, greenWorld.z, Mathf.Max(gx, gz) + 2f);

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
            float br = 4f + 4f * (float)new System.Random(seed).NextDouble(); // ~8–16 m bunkers
            BuildBlob(parent, zoneId, "Bunker", p, ProcMesh.EllipseRadii(br, br * 0.75f, 28, 0.16f, seed, 0f), -0.06f, new Color(0.82f, 0.74f, 0.55f));
            Vector3 bw = parent.TransformPoint(new Vector3(p.x, 0f, p.y));
            StampGrassMask(bw.x, bw.z, br + 1f); // no tall grass in the sand
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
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SharedSurfaceMaterial(matKey, color);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // flat ground: no self-shadow seam
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
                verts[i].y = SurfaceGroundY(world.x, world.z) + lift - rootY - localPos.y;
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
            pole.transform.localPosition = localBase + new Vector3(0f, 1.067f, 0f);
            pole.transform.localScale = new Vector3(0.02f, 1.067f, 0.02f); // 7 ft (2.13 m) flagstick
            var pm = SolidMaterial(new Color(0.92f, 0.92f, 0.92f)); if (pm != null) pole.GetComponent<Renderer>().material = pm;

            var flag = GameObject.CreatePrimitive(PrimitiveType.Quad);
            flag.name = "Flag";
            Destroy(flag.GetComponent<Collider>());
            flag.transform.SetParent(parent, false);
            flag.transform.localPosition = localBase + new Vector3(0.25f, 1.95f, 0f);
            flag.transform.localScale = new Vector3(0.5f, 0.35f, 1f);
            flag.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var fm = SolidMaterial(new Color(0.85f, 0.12f, 0.14f)); if (fm != null) flag.GetComponent<Renderer>().material = fm;
        }

        private void ScatterTrees(Transform parent, int hole, List<Vector2> center, float[] roughHalf)
        {
            // The Terrain places efficient tree instances when a model pack is imported; only scatter our
            // own GameObjects on the mesh fallback OR when no tree prefab exists (so it's never barren).
            if (_terrain != null && _treePrefabs != null && _treePrefabs.Length > 0) return;
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
                    PlaceTree(parent, new Vector3(lp.x, SurfaceGroundY(world.x, world.z) - parent.position.y, lp.y), rnd);
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
                PlaceTree(grove, new Vector3(x, SurfaceGroundY(x, z), z), rnd);
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

        // ---- the maintenance building (diegetic interface, GDD §5.3) ----

        /// <summary>
        /// Build the placeholder (cube-grey) maintenance building the day starts in: a small first-person
        /// room with a doorway onto the course and the physical objects the player uses to manage the
        /// course — the wall course-map, the wall calendar + crew board, and the desk (NOAA forecast,
        /// soil clipboard, moisture meter, fert/spray log). Art is Milestone B; this is the interaction
        /// model. Each interactable carries a <see cref="DiegeticObject"/> the controller raycasts for.
        /// </summary>
        private void BuildRoom()
        {
            // Sit the building off to the side of hole 1 (the course extends +X/+Z; this tucks into -X).
            float cx = RoomCx, cz = RoomCz;
            float floorY = SurfaceGroundY(cx, cz);
            _roomCenter = new Vector3(cx, floorY, cz);
            float hx = _roomHalf.x, hz = _roomHalf.y;     // interior half-extents
            const float wh = 3.2f, wt = 0.3f, door = 2.2f, doorH = 2.3f;

            var root = new GameObject("MaintenanceBuilding").transform;
            root.position = _roomCenter; // local space: floor at y=0, interior x∈[-hx,hx], z∈[-hz,hz]

            Color wall = new Color(0.62f, 0.62f, 0.60f);
            Color floor = new Color(0.48f, 0.48f, 0.47f);
            Color trim = new Color(0.55f, 0.55f, 0.54f);

            Box("Floor", root, new Vector3(0, -0.1f, 0), new Vector3(2 * hx, 0.2f, 2 * hz), floor);
            Box("Ceiling", root, new Vector3(0, wh, 0), new Vector3(2 * hx, 0.2f, 2 * hz), wall, collider: false);
            Box("Wall-Z+", root, new Vector3(0, wh / 2, hz), new Vector3(2 * hx, wh, wt), wall);
            Box("Wall-Z-", root, new Vector3(0, wh / 2, -hz), new Vector3(2 * hx, wh, wt), wall);
            Box("Wall-X-", root, new Vector3(-hx, wh / 2, 0), new Vector3(wt, wh, 2 * hz), wall);
            // +X wall has a centred doorway onto the course: two jambs + a lintel.
            float seg = (2 * hz - door) / 2f, segC = (door / 2f + seg / 2f);
            Box("Door-jamb+", root, new Vector3(hx, wh / 2, segC), new Vector3(wt, wh, seg), wall);
            Box("Door-jamb-", root, new Vector3(hx, wh / 2, -segC), new Vector3(wt, wh, seg), wall);
            Box("Door-lintel", root, new Vector3(hx, (doorH + wh) / 2, 0), new Vector3(wt, wh - doorH, door), wall);

            // A soft interior light so the room reads (the sun is outside).
            var lampGo = new GameObject("RoomLight");
            lampGo.transform.SetParent(root, false);
            lampGo.transform.localPosition = new Vector3(0, wh - 0.4f, 0);
            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 16f; lamp.intensity = 1.4f; lamp.color = new Color(1f, 0.97f, 0.9f);

            // The desk against the back (-X) wall.
            Box("Desk", root, new Vector3(-hx + 0.9f, 0.5f, 0), new Vector3(1.4f, 1.0f, 2.6f), trim);
            float deskTop = 1.0f, deskX = -hx + 0.9f;

            // ---- wall stations ----
            // Course map: the keystone, big on the +Z wall.
            Station("CourseMap", root, new Vector3(0, 1.7f, hz - wt / 2 - 0.03f), new Vector3(3.4f, 1.8f, 0.06f),
                    new Color(0.40f, 0.50f, 0.42f), DiegeticKind.CourseMap, "Read the course map");
            // Calendar + crew board side-by-side on the -Z wall.
            Station("Calendar", root, new Vector3(-2.4f, 1.7f, -hz + wt / 2 + 0.03f), new Vector3(2.6f, 1.6f, 0.06f),
                    new Color(0.50f, 0.46f, 0.36f), DiegeticKind.Calendar, "Open the calendar");
            Station("CrewBoard", root, new Vector3(2.4f, 1.7f, -hz + wt / 2 + 0.03f), new Vector3(2.6f, 1.6f, 0.06f),
                    new Color(0.44f, 0.44f, 0.50f), DiegeticKind.CrewBoard, "Check the crew board");

            // ---- desk items ----
            Station("Forecast", root, new Vector3(deskX, deskTop, -0.9f), new Vector3(0.45f, 0.06f, 0.35f),
                    new Color(0.80f, 0.78f, 0.70f), DiegeticKind.Forecast, "Read the NOAA forecast");
            Station("SoilClipboard", root, new Vector3(deskX, deskTop, -0.3f), new Vector3(0.4f, 0.05f, 0.32f),
                    new Color(0.72f, 0.70f, 0.62f), DiegeticKind.SoilClipboard, "Read the soil clipboard");
            Station("MoistureMeter", root, new Vector3(deskX, deskTop + 0.05f, 0.3f), new Vector3(0.18f, 0.12f, 0.3f),
                    new Color(0.30f, 0.40f, 0.30f), DiegeticKind.MoistureMeter, "the moisture meter", pickup: true);
            Station("FertLog", root, new Vector3(deskX, deskTop, 0.9f), new Vector3(0.42f, 0.07f, 0.32f),
                    new Color(0.66f, 0.62f, 0.55f), DiegeticKind.FertLog, "Read the fert / spray log");

            // Wake up standing in the middle of the room, facing the doorway (+X).
            _roomSpawn = new Vector3(cx, floorY + 1.3f, cz);
        }

        private GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!collider) { var col = go.GetComponent<Collider>(); if (col != null) Destroy(col); }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var m = SolidMaterial(color); if (m != null) go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }

        private void Station(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color,
                             DiegeticKind kind, string label, bool pickup = false)
        {
            var go = Box(name, parent, localPos, scale, color); // keeps its box collider for the interaction ray
            var d = go.AddComponent<DiegeticObject>();
            d.Kind = kind; d.Label = label; d.IsPickup = pickup;
        }

        // ---- player ----

        private (GameObject player, Camera cam) BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = _roomSpawn != Vector3.zero
                ? _roomSpawn                                          // wake up inside the maintenance building
                : new Vector3(0f, SurfaceGroundY(0f, 0f) + 1.3f, 0f); // (fallback) hole-1 tee
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0);

            // Reuse the scene's existing camera if there is one (avoids two cameras fighting); else make one.
            Camera cam = Camera.main;
            GameObject camGo = cam != null ? cam.gameObject : new GameObject("Camera");
            if (cam == null) { cam = camGo.AddComponent<Camera>(); camGo.AddComponent<AudioListener>(); try { camGo.tag = "MainCamera"; } catch { } }
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camGo.transform.localRotation = Quaternion.identity;
            cam.farClipPlane = 2500f; // see across the full-scale course

            _fp = player.AddComponent<FirstPersonController>(); // Awake finds the child camera

            _inspect = player.AddComponent<GreenInspectionController>();
            _inspect.game = _game; _inspect.cam = cam;

            _putt = player.AddComponent<PuttingController>();
            _putt.game = _game; _putt.cam = cam;

            _interaction = player.AddComponent<RoomInteractionController>();
            _interaction.game = _game; _interaction.cam = cam; _interaction.bootstrap = this;
            _interaction.fp = _fp; _interaction.inspection = _inspect; _interaction.putt = _putt;
            _interaction.roomCenter = _roomCenter; _interaction.roomHalf = _roomHalf;
            _inspect.room = _interaction; // metering on a green needs the handheld meter carried out

            return (player, cam);
        }

        private void BuildBallAndCup(GameObject player, List<HoleViz> holes)
        {
            if (holes.Count == 0) return;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Ball";
            ball.transform.localScale = Vector3.one * 0.0427f; // 1.68 in golf ball
            Destroy(ball.GetComponent<Collider>()); // ball is kinematic-animated
            var ballMat = SolidMaterial(Color.white); if (ballMat != null) ball.GetComponent<Renderer>().material = ballMat;

            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cup.name = "Cup";
            cup.transform.localScale = new Vector3(0.108f, 0.05f, 0.108f); // 4.25 in cup
            Destroy(cup.GetComponent<Collider>());
            var cupMat = SolidMaterial(Color.white); if (cupMat != null) cup.GetComponent<Renderer>().material = cupMat; // white inside

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
            hud.room = _interaction;

            var roomHud = ui.AddComponent<MaintenanceRoomHud>();
            roomHud.game = _game;
            roomHud.room = _interaction;
        }

        // ---- modes / input ----
        // Cursor / look / putt gating now lives in RoomInteractionController (the diegetic UI authority).

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

        /// <summary>Shared material for a procedural-mesh surface (UVs carry the tiling; colour set per-renderer
        /// via MPB). Greens use a dedicated fine green turf — never the coarse fairway/forest fallback.</summary>
        private Material SharedSurfaceMaterial(string key, Color color)
        {
            Material loaded = key == "Green" ? Resources.Load<Material>("PolyHaven/Green") : LoadSurfaceMaterial(key);
            return loaded != null ? loaded : TurfMat(key);
        }

        /// <summary>A cached generated turf material per surface — fine green grain (+ subtle mow stripes),
        /// near-grey so the SurfaceRenderer's health colour tints it; the fine grain hides tiling.</summary>
        private Material TurfMat(string key)
        {
            _turfMats ??= new Dictionary<string, Material>();
            if (_turfMats.TryGetValue(key, out var cached)) return cached;
            Texture2D tex;
            switch (key)
            {
                case "Green":    tex = TurfTexture(11, new Color(0.92f, 0.92f, 0.92f), 0.06f, 22, 0.05f); break; // fine, tight stripes
                case "Fairway":  tex = TurfTexture(22, new Color(0.90f, 0.90f, 0.90f), 0.11f, 40, 0.11f); break; // mow stripes
                case "Tee":      tex = TurfTexture(33, new Color(0.90f, 0.90f, 0.90f), 0.10f, 30, 0.08f); break;
                case "Approach": tex = TurfTexture(44, new Color(0.90f, 0.90f, 0.90f), 0.10f, 34, 0.08f); break;
                default:         tex = TurfTexture(55, new Color(0.88f, 0.88f, 0.88f), 0.22f, 0, 0f); break;     // rough/ground: mottled
            }
            var m = new Material(LitShader());
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.08f);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided: no black back-edge at seams
            _turfMats[key] = m;
            return m;
        }

        /// <summary>Soft mottled turf texture (Perlin) with optional mow stripes; tiles cleanly (no big features).</summary>
        private static Texture2D TurfTexture(int seed, Color baseColor, float variation, int stripePx, float stripeStr)
        {
            const int S = 256;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "turf" };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                float stripe = stripePx > 0 ? ((y / stripePx) % 2 == 0 ? 1f : 1f - stripeStr) : 1f;
                for (int x = 0; x < S; x++)
                {
                    float mott = Mathf.PerlinNoise(x * 0.06f + seed, y * 0.06f + seed);
                    float k = Mathf.Clamp01(1f + (mott - 0.5f) * variation) * stripe;
                    px[y * S + x] = new Color(baseColor.r * k, baseColor.g * k, baseColor.b * k, 1f);
                }
            }
            t.SetPixels(px); t.Apply();
            return t;
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
