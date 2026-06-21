using UnityEngine;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// Drives a single-zone surface (fairway / tee / approach / rough / bunker) from the sim's honest
    /// state — the off-green analogue of <see cref="GreenRenderer"/>. Turf surfaces read the legibility
    /// tell (colour depth, thinning, wilt, wet sheen); bunkers read sand consistency (washed = dark).
    /// One component per rendered quad; several quads may point at the same zone id.
    /// </summary>
    public sealed class SurfaceRenderer : MonoBehaviour
    {
        public GameManager game;
        public string zoneId;

        /// <summary>Set for green meshes: enables worst-cell colouring + hit→sub-cell mapping for scouting.</summary>
        public bool spatialGreen;
        public Vector2 greenHalf = Vector2.one; // local XZ half-extents of the green mesh

        private Renderer _r;
        private MaterialPropertyBlock _mpb;
        private int _lastDay = -1; // colours only change when the sim day advances — recompute then only
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"); // URP lit
        private static readonly int ColorProp = Shader.PropertyToID("_Color");     // Built-in standard

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            _r = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>Map a world point on this green to a 3x3 sub-cell index (for scouting/metering).</summary>
        public int CellIndexAt(Vector3 worldPoint)
        {
            if (!spatialGreen) return 0;
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            int col = Mathf.Clamp(Mathf.FloorToInt((lp.x / Mathf.Max(0.01f, greenHalf.x) * 0.5f + 0.5f) * 3f), 0, 2);
            int row = Mathf.Clamp(Mathf.FloorToInt((lp.z / Mathf.Max(0.01f, greenHalf.y) * 0.5f + 0.5f) * 3f), 0, 2);
            return row * 3 + col;
        }

        private void LateUpdate()
        {
            if (_r == null || game == null || game.Legibility == null || game.Course == null) return;
            int day = game.Director.Clock.DayIndex;
            if (day == _lastDay) return; // skip unchanged frames (keeps 18 holes cheap)
            _lastDay = day;
            ZoneState zone = game.Course.Get(zoneId);
            if (zone == null) return;

            Color c;
            if (zone.Type == ZoneType.Bunker)
            {
                // Clean firm sand = pale tan; washed-out / settled sand = darker, muddier.
                float q = Mathf.Clamp01((float)(zone.SandQualityPct / 100.0));
                if (zone.WashedOut) q *= 0.5f;
                c = Color.Lerp(new Color(0.45f, 0.38f, 0.28f), new Color(0.86f, 0.78f, 0.58f), q);
            }
            else
            {
                ObservableZone obs = game.Legibility.Observe(zone, day);
                // For a green, show the WORST sub-cell so a partly-sick green still reads as sick.
                int idx = 0;
                if (spatialGreen && obs.Tells.Length > 1)
                {
                    double worst = -1;
                    for (int i = 0; i < obs.Tells.Length; i++)
                    {
                        double sev = obs.Tells[i].Lesions + obs.Tells[i].Thinning;
                        if (sev > worst) { worst = sev; idx = i; }
                    }
                }
                TellAppearance t = obs.Tells.Length > 0 ? obs.Tells[idx] : default;

                // The MPB colour MULTIPLIES the (already grass-coloured) turf texture, so HEALTHY turf must
                // tint near-bright or the mesh comes out far darker than the untinted terrain (the dark
                // patchwork). Start bright; only genuine problems pull it dark / off-colour.
                bool green = zone.Type == ZoneType.Green;
                float healthy01 = Mathf.InverseLerp(0.70f, 0.34f, (float)t.BaseColor.G); // 1 = deep/fed, 0 = starved
                c = Color.Lerp(new Color(0.86f, 0.84f, 0.52f),   // starved: pale yellow-green
                               new Color(0.80f, 0.94f, 0.62f),   // fed/healthy: bright green
                               healthy01);

                float thinW = green ? 0.65f : 0.30f;
                float overW = green ? 0.45f : 0.30f;
                c = Color.Lerp(c, new Color(0.66f, 0.70f, 0.64f), (float)t.WiltTint * 0.5f);    // dry wilt: grey-green
                c = Color.Lerp(c, c * 0.78f, (float)t.WetSheen * 0.7f);                          // wet: a touch darker
                c = Color.Lerp(c, new Color(0.82f, 0.72f, 0.42f), (float)t.Lesions * 0.85f);     // disease straw
                c = Color.Lerp(c, new Color(0.46f, 0.37f, 0.25f), (float)t.Thinning * thinW);    // bare soil

                // Un-mown LENGTH reads shaggier: deepen toward a soft olive (never near-black).
                double cut = zone.Surface != null ? zone.Surface.MowHeightIn : zone.MowHeightIn;
                double range = zone.Type == ZoneType.Rough ? 4.0 : 1.5;
                float over = Mathf.Clamp01((float)((zone.GrassHeightIn - cut) / range));
                if (over > 0.01f)
                    c = Color.Lerp(c, new Color(0.42f, 0.52f, 0.28f), over * overW);
            }

            _r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorProp, c);
            _r.SetPropertyBlock(_mpb);
        }
    }
}
