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
                ObservableZone obs = game.Legibility.Observe(zone, game.Director.Clock.DayIndex);
                TellAppearance t = obs.Tells.Length > 0 ? obs.Tells[0] : default;
                c = new Color((float)t.BaseColor.R, (float)t.BaseColor.G, (float)t.BaseColor.B, 1f);
                c = Color.Lerp(c, new Color(0.55f, 0.60f, 0.58f), (float)t.WiltTint * 0.6f);   // dry wilt
                c = Color.Lerp(c, c * 0.6f, (float)t.WetSheen);                                 // wet sheen
                c = Color.Lerp(c, new Color(0.72f, 0.64f, 0.40f), (float)t.Lesions * 0.85f);    // disease straw
                c = Color.Lerp(c, new Color(0.34f, 0.26f, 0.18f), (float)t.Thinning * 0.7f);    // bare soil
            }

            _r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColor, c);
            _mpb.SetColor(ColorProp, c);
            _r.SetPropertyBlock(_mpb);
        }
    }
}
