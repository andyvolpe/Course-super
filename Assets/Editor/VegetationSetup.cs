#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Greenkeeper.Unity.EditorTools
{
    /// <summary>
    /// One-click vegetation wiring for the play test. Scans the imported NatureManufacture (or any)
    /// packs for tree + grass prefabs and copies a curated subset into the Resources/ folders the
    /// runtime scene loads from (Resources/Trees, Resources/Grass, Resources/TerrainLayers). No manual
    /// dragging. Window > Greenkeeper > Vegetation Setup.
    /// </summary>
    public sealed class VegetationSetup : EditorWindow
    {
        private int _maxTrees = 8;
        private int _maxGrass = 4;
        private bool _copyTerrainLayers = true;
        private Vector2 _scroll;
        private string _log = "";

        [MenuItem("Window/Greenkeeper/Vegetation Setup")]
        public static void Open() => GetWindow<VegetationSetup>("Vegetation Setup");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Copies tree + grass prefabs from your imported packs (e.g. NatureManufacture) into " +
                "Assets/Resources/{Trees,Grass,TerrainLayers}, which the play test scatters automatically. " +
                "Re-run any time; review what it copied in the log and add/remove by hand if needed.",
                MessageType.Info);

            _maxTrees = EditorGUILayout.IntSlider("Max tree prefabs", _maxTrees, 1, 20);
            _maxGrass = EditorGUILayout.IntSlider("Max grass prefabs", _maxGrass, 1, 12);
            _copyTerrainLayers = EditorGUILayout.Toggle("Copy 3 terrain layers", _copyTerrainLayers);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan & Copy to Resources", GUILayout.Height(30))) Populate();
                if (GUILayout.Button("Clear Resources vegetation", GUILayout.Height(30))) Clear();
            }

            if (!string.IsNullOrEmpty(_log))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }

        private void Populate()
        {
            var sb = new System.Text.StringBuilder();
            string[] roots = FindPackRoots();
            if (roots.Length == 0)
            {
                _log = "No vegetation packs found under Assets/ (looked for folders containing " +
                       "'NatureManufacture'/'Nature'/'Vegetation'). Import a pack first.";
                return;
            }
            sb.AppendLine("Searching: " + string.Join(", ", roots));

            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Resources/Trees");
            EnsureFolder("Assets/Resources/Grass");

            var prefabs = AssetDatabase.FindAssets("t:Prefab", roots)
                .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();

            int trees = CopySet(prefabs.Where(IsTree), "Assets/Resources/Trees", _maxTrees, sb, "tree");
            int grass = CopySet(prefabs.Where(IsGrass), "Assets/Resources/Grass", _maxGrass, sb, "grass");

            int layers = 0;
            if (_copyTerrainLayers)
            {
                EnsureFolder("Assets/Resources/TerrainLayers");
                var tls = AssetDatabase.FindAssets("t:TerrainLayer", roots)
                    .Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
                string[] grassWords = { "grass", "lawn", "meadow", "turf", "field", "green" };
                string[] dirtWords = { "dirt", "soil", "ground", "path", "mud", "gravel", "sand", "forest", "rock" };
                var grass = tls.Where(p => grassWords.Any(p.ToLower().Contains)).ToList();
                var dirt = tls.Where(p => dirtWords.Any(p.ToLower().Contains)).ToList();
                // Map grass-like layers to Rough/Fairway; a dirt-like one to Ground. Skip if none match
                // (so we never put a dirt texture down as the base "grass").
                if (grass.Count > 0) layers += CopyLayer(grass[0], "Rough", sb);
                if (grass.Count > 0) layers += CopyLayer(grass.Count > 1 ? grass[1] : grass[0], "Fairway", sb);
                if (dirt.Count > 0) layers += CopyLayer(dirt[0], "Ground", sb);
                if (grass.Count == 0) sb.AppendLine("  (no grass-named TerrainLayer found — leaving the green fallback)");
            }

            AssetDatabase.Refresh();
            sb.Insert(0, $"Copied {trees} trees, {grass} grass, {layers} terrain layers.\n\n");
            _log = sb.ToString();
            Debug.Log("[VegetationSetup] " + _log);
        }

        private int CopyLayer(string src, string name, System.Text.StringBuilder sb)
        {
            string dest = $"Assets/Resources/TerrainLayers/{name}.terrainlayer";
            if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(dest) != null) AssetDatabase.DeleteAsset(dest);
            if (AssetDatabase.CopyAsset(src, dest)) { sb.AppendLine($"  layer -> {name}  ({Path.GetFileName(src)})"); return 1; }
            return 0;
        }

        private int CopySet(IEnumerable<string> srcPaths, string destFolder, int max, System.Text.StringBuilder sb, string kind)
        {
            int n = 0;
            var seen = new HashSet<string>();
            foreach (var src in srcPaths)
            {
                if (n >= max) break;
                string file = Path.GetFileName(src);
                if (!seen.Add(file)) continue;
                string dest = $"{destFolder}/{file}";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(dest) != null) { n++; continue; } // already there
                if (AssetDatabase.CopyAsset(src, dest)) { n++; sb.AppendLine($"  {kind} -> {file}"); }
            }
            return n;
        }

        private void Clear()
        {
            foreach (var f in new[] { "Assets/Resources/Trees", "Assets/Resources/Grass", "Assets/Resources/TerrainLayers" })
                if (AssetDatabase.IsValidFolder(f)) AssetDatabase.DeleteAsset(f);
            AssetDatabase.Refresh();
            _log = "Cleared Resources/Trees, Resources/Grass, Resources/TerrainLayers.";
        }

        // ---- heuristics ----

        private static string[] FindPackRoots()
        {
            return AssetDatabase.GetSubFolders("Assets")
                .Where(f => { var l = f.ToLower(); return l.Contains("naturemanufacture") || l.Contains("nature") || l.Contains("vegetation"); })
                .ToArray();
        }

        private static readonly string[] TreeWords =
            { "/tree", "tree", "pine", "oak", "spruce", "birch", "fir", "maple", "willow", "poplar",
              "beech", "hornbeam", "conifer", "cedar", "aspen", "elm", "ash", "larch", "bush", "shrub" };
        private static readonly string[] GrassWords =
            { "grass", "meadow", "fern", "clover", "flower", "weed", "sedge", "reed" };
        private static readonly string[] Reject =
            { "lod", "billboard", "impostor", "stump", "log", "branch", "trunk", "stage", "demo", "example" };

        private static bool IsTree(string path)
        {
            string p = path.ToLower();
            if (Reject.Any(p.Contains)) return false;
            return TreeWords.Any(p.Contains);
        }

        private static bool IsGrass(string path)
        {
            string p = path.ToLower();
            if (p.Contains("lod") || p.Contains("billboard")) return false;
            return GrassWords.Any(p.Contains);
        }

        private static void EnsureFolder(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;
            string parent = Path.GetDirectoryName(assetDir).Replace('\\', '/');
            string leaf = Path.GetFileName(assetDir);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
