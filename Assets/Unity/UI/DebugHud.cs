using UnityEngine;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Ugly-but-functional debug HUD (Phase 1.2) — the morning-plan → resolve → skip loop shell from
    /// GDD §1. Drawn with IMGUI so it needs no scene wiring beyond adding this component next to a
    /// <see cref="GameManager"/>. Shows the day/season, drives Resolve / Skip, and tails the step log.
    ///
    /// SETUP: add this component to the same GameObject as GameManager (or assign the reference).
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        public GameManager game;

        private Vector2 _scroll;

        private void Awake()
        {
            if (game == null) game = GetComponent<GameManager>() ?? FindObjectOfType<GameManager>();
        }

        private void OnGUI()
        {
            if (game == null || game.Director == null) return;

            const float w = 460f;
            GUILayout.BeginArea(new Rect(10, 10, w, Screen.height - 20), GUI.skin.box);

            var clock = game.Director.Clock;
            GUILayout.Label($"<b>GREENKEEPER — debug</b>", Rich());
            GUILayout.Label($"Day {clock.DayIndex}  |  {clock.Season}  |  season-day {clock.DayOfSeason + 1}/90");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resolve Day")) game.ResolveDay();
            if (GUILayout.Button("Skip to next interrupt")) game.SkipToNextInterrupt();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New Game")) game.NewGame();
            if (GUILayout.Button("Save")) game.SaveGame();
            GUILayout.EndHorizontal();

            // A quick read of green #1 so you can see the sim moving.
            var g1 = game.Course?.Get("green-01");
            if (g1 != null)
            {
                GUILayout.Space(6);
                GUILayout.Label($"green-01  moisture {g1.SoilMoisturePct:F1}%  density {g1.DensityPct:F0}%  " +
                                $"N {g1.NitrogenPct:F0}  debt {g1.TurfDebtPct:F0}");
                GUILayout.Label($"          stimp {g1.Stimp:F1}  firmness {g1.FirmnessPct:F0}  " +
                                $"pressure {g1.MeanPressure:F1}  infection {g1.MaxInfection:F1}");
            }

            GUILayout.Space(6);
            GUILayout.Label("Resolve log:");
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = game.RecentLog.Count - 1; i >= 0; i--)
                GUILayout.Label(game.RecentLog[i]);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private static GUIStyle Rich()
        {
            var s = new GUIStyle(GUI.skin.label) { richText = true };
            return s;
        }
    }
}
