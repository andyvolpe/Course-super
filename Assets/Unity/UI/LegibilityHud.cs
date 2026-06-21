using UnityEngine;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Unity.Managers;
using Greenkeeper.Unity.Play;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Phase 3.2/3.3 — the real legibility readout (replaces the throwaway debug panel for play). Shows
    /// ONLY what the LegibilitySystem permits for the green you're looking at: always the qualitative
    /// tells; exact infection/moisture/nutrients only once earned; the turf-debt bar and threat
    /// telegraph ONLY when assists are on. A button toggles assists (surfacing only — never the sim).
    /// </summary>
    public sealed class LegibilityHud : MonoBehaviour
    {
        public GameManager game;
        public GreenInspectionController inspection;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
            if (inspection == null) inspection = FindFirstObjectByType<GreenInspectionController>();
        }

        private void OnGUI()
        {
            if (game == null || game.Legibility == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 360, 360), GUI.skin.box);
            bool assists = game.assistsEnabled;
            GUILayout.Label($"<b>Legibility</b>  —  difficulty: {(assists ? "ASSISTED" : "FULL")}", Rich());
            if (GUILayout.Button(assists ? "Switch to FULL difficulty" : "Turn ASSISTS on"))
                game.SetAssists(!assists);

            string zoneId = inspection != null ? inspection.AimedZoneId : null;
            if (string.IsNullOrEmpty(zoneId)) { GUILayout.Label("Look at a green to read it."); GUILayout.EndArea(); return; }

            ZoneState z = game.Course.Get(zoneId);
            if (z == null) { GUILayout.EndArea(); return; }

            ObservableZone obs = game.Legibility.Observe(z, game.Director.Clock.DayIndex);
            int cell = inspection.AimedCellIndex;
            TellAppearance t = obs.Tells[Mathf.Clamp(cell, 0, obs.Tells.Length - 1)];

            GUILayout.Space(4);
            GUILayout.Label($"<b>{zoneId}</b>  (sub-cell {cell})", Rich());
            GUILayout.Label("Visual tells (free):");
            GUILayout.Label($"  colour depth {Depth(t):0%}   lesions {t.Lesions:0%}   thinning {t.Thinning:0%}");
            GUILayout.Label($"  wet sheen {t.WetSheen:0%}   wilt {t.WiltTint:0%}");

            GUILayout.Space(4);
            GUILayout.Label("Earned reads:");
            GUILayout.Label(obs.InfectionRevealed
                ? $"  [scouted] infection {obs.RevealedMaxInfection:0.#}  symptom {obs.RevealedMeanExpression:0.#}"
                : "  infection: unknown (Q to scout)");
            GUILayout.Label(obs.MoistureMetered
                ? $"  [meter] VWC {obs.MeteredMoisturePct:0.#}%"
                : "  moisture: unknown (E to meter)");
            GUILayout.Label(obs.SoilTested
                ? $"  [soil test] N {obs.RevealedNitrogenPct:0.#}  OM {obs.RevealedOrganicMatterPct:0.#}%"
                : "  nutrients: unknown (R to soil test)");

            GUILayout.Space(4);
            if (obs.TurfDebtShown)
                GUILayout.Label($"<color=#ffd0d0>[assist] turf debt {obs.TurfDebtPct:0}/100</color>", Rich());
            if (obs.ThreatTelegraphed)
                GUILayout.Label($"<color=#ffe0a0>[assist] {obs.ThreatNote}</color>", Rich());
            if (!assists)
                GUILayout.Label("<i>turf debt is never shown on full difficulty.</i>", Rich());

            GUILayout.EndArea();
        }

        private static float Depth(TellAppearance t)
        {
            // Rough "how dark/green" indicator for the readout (1 - paleness).
            return Mathf.Clamp01(1f - (float)t.BaseColor.R / 0.62f);
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
