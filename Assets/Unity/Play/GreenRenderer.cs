using UnityEngine;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.Play
{
    /// <summary>
    /// Phase 3.1 — drives the green surface shader from the sim's honest tells. One material instance
    /// per 3x3 sub-cell (assign 9 cell Renderers in row-major order). Reads tells THROUGH the single
    /// LegibilitySystem gate, so it can only ever render what the player is permitted to see — and
    /// commits per sub-cell, so a green can look partly sick.
    /// </summary>
    public sealed class GreenRenderer : MonoBehaviour
    {
        public GameManager game;
        public string zoneId = "green-01";

        [Tooltip("9 sub-cell quads/meshes in row-major (3x3) order matching the sim grid.")]
        public Renderer[] cellRenderers = new Renderer[9];

        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int Lesions = Shader.PropertyToID("_Lesions");
        private static readonly int Thinning = Shader.PropertyToID("_Thinning");
        private static readonly int WetSheen = Shader.PropertyToID("_WetSheen");
        private static readonly int WiltTint = Shader.PropertyToID("_WiltTint");

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            _mpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (game == null || game.Legibility == null || game.Course == null) return;
            ZoneState zone = game.Course.Get(zoneId);
            if (zone == null) return;

            ObservableZone obs = game.Legibility.Observe(zone, game.Director.Clock.DayIndex);
            int count = Mathf.Min(cellRenderers.Length, obs.Tells.Length);
            for (int i = 0; i < count; i++)
            {
                var r = cellRenderers[i];
                if (r == null) continue;
                TellAppearance t = obs.Tells[i];
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColor, new Color((float)t.BaseColor.R, (float)t.BaseColor.G, (float)t.BaseColor.B, 1f));
                _mpb.SetFloat(Lesions, (float)t.Lesions);
                _mpb.SetFloat(Thinning, (float)t.Thinning);
                _mpb.SetFloat(WetSheen, (float)t.WetSheen);
                _mpb.SetFloat(WiltTint, (float)t.WiltTint);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
