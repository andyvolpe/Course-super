#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Greenkeeper.Unity.EditorTools
{
    /// <summary>
    /// Editor importer for Poly Haven (CC0) assets. Paste a slug, pick a resolution and the target
    /// surface key, and it downloads the maps from Poly Haven's CDN, imports them, builds a lit
    /// Material (or a Skybox material for an HDRI), and saves it where the play test auto-loads it:
    /// Assets/Resources/PolyHaven/&lt;Key&gt;.mat. See docs/polyhaven-assets.md.
    ///
    /// Window > Greenkeeper > Poly Haven Importer.
    /// </summary>
    public sealed class PolyHavenImporter : EditorWindow
    {
        private const string CdnBase = "https://dl.polyhaven.org/file/ph-assets";
        private static readonly string[] SurfaceKeys =
            { "Green", "Approach", "Fairway", "Tee", "Rough", "Bunker", "Ground" };
        private static readonly string[] Resolutions = { "1k", "2k", "4k" };

        private enum Kind { Texture, HDRI, Model }

        private string _slug = "aerial_grass_rock";
        private int _res = 1;        // index into Resolutions (2k)
        private Kind _kind = Kind.Texture;
        private int _keyIndex = 2;   // Fairway
        private string _modelKey = "Tree";
        private string _status = "";

        [MenuItem("Window/Greenkeeper/Poly Haven Importer")]
        public static void Open() => GetWindow<PolyHavenImporter>("Poly Haven Importer");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Poly Haven asset importer (CC0)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Paste a slug from polyhaven.com (the URL tail, e.g. 'aerial_grass_rock', " +
                "'hausdorf_clear_sky', or a model like 'grass_medium_01'). Texture -> material, " +
                "HDRI -> skybox, Model -> FBX prefab — saved into Resources/PolyHaven/.",
                MessageType.Info);

            _slug = EditorGUILayout.TextField("Slug", _slug).Trim();
            _kind = (Kind)EditorGUILayout.EnumPopup("Kind", _kind);
            _res = EditorGUILayout.Popup("Resolution", _res, Resolutions);

            if (_kind == Kind.Texture)
                _keyIndex = EditorGUILayout.Popup("Surface key", _keyIndex, SurfaceKeys);
            else if (_kind == Kind.HDRI)
                EditorGUILayout.LabelField("Saves as", "Resources/PolyHaven/Skybox.mat");
            else
                _modelKey = EditorGUILayout.TextField("Prefab key", _modelKey);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_slug)))
                if (GUILayout.Button("Import", GUILayout.Height(30)))
                {
                    if (_kind == Kind.Texture) ImportTexture(_slug, Resolutions[_res], SurfaceKeys[_keyIndex]);
                    else if (_kind == Kind.HDRI) ImportHdri(_slug, Resolutions[_res]);
                    else ImportModel(_slug, Resolutions[_res], string.IsNullOrEmpty(_modelKey) ? "Tree" : _modelKey.Trim());
                }

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.None);
        }

        // ---- texture -> lit material ---------------------------------------------------

        private void ImportTexture(string slug, string res, string key)
        {
            // Poly Haven CDN naming: Textures/jpg/<res>/<slug>/<slug>_<map>_<res>.jpg
            string assetDir = $"Assets/Art/PolyHaven/{slug}";
            EnsureDir(assetDir);
            try
            {
                string diff = Fetch(slug, res, "diff", assetDir, required: true);
                string nor = Fetch(slug, res, "nor_gl", assetDir, required: false);
                string ao = Fetch(slug, res, "ao", assetDir, required: false);
                string rough = Fetch(slug, res, "rough", assetDir, required: false);
                AssetDatabase.Refresh();

                if (diff == null) { Fail("Could not download the diffuse map — check the slug."); return; }
                if (nor != null) SetNormalMap(nor);

                var mat = NewLitMaterial();
                AssignTex(mat, "_BaseMap", "_MainTex", Load<Texture2D>(diff));
                mat.mainTexture = Load<Texture2D>(diff);
                if (nor != null) { mat.SetTexture("_BumpMap", Load<Texture2D>(nor)); mat.EnableKeyword("_NORMALMAP"); }
                if (ao != null && mat.HasProperty("_OcclusionMap"))
                {
                    mat.SetTexture("_OcclusionMap", Load<Texture2D>(ao));
                    if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", 1f);
                    mat.EnableKeyword("_OCCLUSIONMAP");
                }
                // Turf/sand aren't glossy.
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.12f);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.12f);

                string outPath = SaveMaterial(mat, key);
                Done($"Imported '{slug}' ({res}) → {outPath}\nmaps: diff{(nor != null ? ", nor_gl" : "")}{(ao != null ? ", ao" : "")}{(rough != null ? ", rough(downloaded)" : "")}");
            }
            catch (System.Exception e) { Fail("Import failed: " + e.Message); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ---- HDRI -> skybox material ---------------------------------------------------

        private void ImportHdri(string slug, string res)
        {
            string assetDir = "Assets/Art/PolyHaven/HDRI";
            EnsureDir(assetDir);
            try
            {
                // Poly Haven CDN naming: HDRIs/hdr/<res>/<slug>_<res>.hdr
                string url = $"{CdnBase}/HDRIs/hdr/{res}/{slug}_{res}.hdr";
                string assetPath = $"{assetDir}/{slug}_{res}.hdr";
                if (!Download(url, assetPath)) { Fail("Could not download the HDRI — check the slug."); return; }
                AssetDatabase.Refresh();

                var sky = new Material(Shader.Find("Skybox/Panoramic"));
                var tex = Load<Texture>(assetPath);
                sky.SetTexture("_MainTex", tex);
                if (sky.HasProperty("_Mapping")) sky.SetFloat("_Mapping", 1);   // latitude-longitude layout
                if (sky.HasProperty("_ImageType")) sky.SetFloat("_ImageType", 0); // 360 degrees

                string outPath = SaveMaterial(sky, "Skybox");
                Done($"Imported HDRI '{slug}' ({res}) → {outPath}\nIt becomes the sky + ambient light on Play.");
            }
            catch (System.Exception e) { Fail("Import failed: " + e.Message); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ---- model -> prefab -----------------------------------------------------------

        private void ImportModel(string slug, string res, string key)
        {
            string assetDir = $"Assets/Art/PolyHaven/{slug}";
            EnsureDir(assetDir);
            try
            {
                // The model's file list (FBX url + texture includes) comes from the Poly Haven API.
                EditorUtility.DisplayProgressBar("Poly Haven", "Fetching file list…", 0.15f);
                string json = FetchText($"https://api.polyhaven.com/files/{slug}");
                if (json == null) { Fail("Couldn't reach the Poly Haven API for the file list."); return; }

                string fbxUrl = PickUrl(json, @"https://[^""]+\.fbx", res);
                if (fbxUrl == null) { Fail("No FBX found for this slug — is it actually a model?"); return; }

                string fbxAsset = $"{assetDir}/{Path.GetFileName(fbxUrl)}";
                EditorUtility.DisplayProgressBar("Poly Haven", "Downloading FBX…", 0.4f);
                if (!Download(fbxUrl, fbxAsset)) { Fail("FBX download failed."); return; }

                // Textures at this resolution — Unity remaps the FBX's materials to these by name.
                var imgs = AllUrls(json, @"https://[^""]+\.(?:png|jpg|jpeg)", res);
                int i = 0;
                foreach (var img in imgs)
                {
                    EditorUtility.DisplayProgressBar("Poly Haven", $"Downloading textures ({++i}/{imgs.Count})…", 0.5f + 0.4f * i / Mathf.Max(1, imgs.Count));
                    Download(img, $"{assetDir}/{Path.GetFileName(img)}");
                }
                AssetDatabase.Refresh();

                var fbx = Load<GameObject>(fbxAsset);
                if (fbx == null) { Fail("FBX imported but couldn't be loaded."); return; }

                EnsureDir("Assets/Resources"); EnsureDir("Assets/Resources/PolyHaven");
                string prefabPath = $"Assets/Resources/PolyHaven/{key}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) AssetDatabase.DeleteAsset(prefabPath);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                Object.DestroyImmediate(inst);
                AssetDatabase.SaveAssets();

                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                Done($"Imported model '{slug}' ({res}) → {prefabPath}\n{imgs.Count} texture(s). It scatters off the playing corridors on Play.");
            }
            catch (System.Exception e) { Fail("Import failed: " + e.Message); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // ---- helpers -------------------------------------------------------------------

        private static string FetchText(string url)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "GreenkeeperEditor/1.0 (+polyhaven import)");
                    return wc.DownloadString(url);
                }
            }
            catch (WebException) { return null; }
        }

        /// <summary>All distinct URLs in the JSON matching the pattern, preferring the chosen resolution.</summary>
        private static List<string> AllUrls(string json, string pattern, string res)
        {
            var all = new List<string>();
            foreach (Match m in Regex.Matches(json, pattern))
                if (!all.Contains(m.Value)) all.Add(m.Value);
            var atRes = all.FindAll(u => u.Contains($"/{res}/") || u.Contains($"_{res}"));
            return atRes.Count > 0 ? atRes : all;
        }

        private static string PickUrl(string json, string pattern, string res)
        {
            var all = AllUrls(json, pattern, res);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>Download one texture map; returns the asset path or null if it 404s (optional maps).</summary>
        private string Fetch(string slug, string res, string map, string assetDir, bool required)
        {
            string file = $"{slug}_{map}_{res}.jpg";
            string url = $"{CdnBase}/Textures/jpg/{res}/{slug}/{file}";
            string assetPath = $"{assetDir}/{file}";
            EditorUtility.DisplayProgressBar("Poly Haven", $"Downloading {map}…", 0.5f);
            return Download(url, assetPath) ? assetPath : null;
        }

        private static bool Download(string url, string assetPath)
        {
            string fsPath = ToFileSystemPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fsPath));
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "GreenkeeperEditor/1.0 (+polyhaven import)");
                    wc.DownloadFile(url, fsPath);
                }
                return File.Exists(fsPath) && new FileInfo(fsPath).Length > 0;
            }
            catch (WebException) { return false; } // 404 for an optional map is fine
        }

        private static void SetNormalMap(string assetPath)
        {
            var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null) return;
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }

        private static Material NewLitMaterial()
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            Shader sh = (urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard"))
                        ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            return new Material(sh);
        }

        private static void AssignTex(Material m, string urpProp, string builtinProp, Texture2D tex)
        {
            if (tex == null) return;
            if (m.HasProperty(urpProp)) m.SetTexture(urpProp, tex);
            if (m.HasProperty(builtinProp)) m.SetTexture(builtinProp, tex);
        }

        private static string SaveMaterial(Material mat, string key)
        {
            EnsureDir("Assets/Resources");
            EnsureDir("Assets/Resources/PolyHaven");
            string path = $"Assets/Resources/PolyHaven/{key}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(mat);
            Selection.activeObject = mat;
            return path;
        }

        private static T Load<T>(string assetPath) where T : Object => AssetDatabase.LoadAssetAtPath<T>(assetPath);

        private static void EnsureDir(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;
            string parent = Path.GetDirectoryName(assetDir).Replace('\\', '/');
            string leaf = Path.GetFileName(assetDir);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static string ToFileSystemPath(string assetPath)
            => Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        private void Done(string msg) { _status = msg; Debug.Log("[PolyHaven] " + msg); Repaint(); }
        private void Fail(string msg) { _status = msg; Debug.LogError("[PolyHaven] " + msg); Repaint(); }
    }
}
#endif
