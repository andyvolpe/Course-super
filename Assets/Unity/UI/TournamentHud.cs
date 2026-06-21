using UnityEngine;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Tournament;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Phase 7 — the agronomist's brief. Shows the next tournament's setup spec, the countdown, and how
    /// the course CURRENTLY measures against the targets, so the player can steer the greens to spec
    /// under the clock. After an event, shows the latest grade (the pride payoff).
    /// </summary>
    public sealed class TournamentHud : MonoBehaviour
    {
        public GameManager game;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
        }

        private void OnGUI()
        {
            var ladder = game != null ? game.Tournament : null;
            if (ladder == null) return;

            GUILayout.BeginArea(new Rect(Screen.width - 470, Screen.height - 200, 250, 190), GUI.skin.box);

            var spec = ladder.Current;
            if (spec != null)
            {
                int days = ladder.DaysUntilNext(game.Director.Clock.DayIndex);
                GUILayout.Label($"<b>NEXT EVENT</b>  {spec.Name}", Rich());
                GUILayout.Label($"in <b>{Mathf.Max(0, days)}</b> days  (day {spec.DayIndex})", Rich());
                GUILayout.Label("Agronomist's spec:");
                GUILayout.Label($"  Stimp {spec.StimpMin:0.0}–{spec.StimpMax:0.0}   firm {spec.FirmMin:0}–{spec.FirmMax:0}");
                GUILayout.Label($"  disease <{spec.MaxInfection:0}   density >{spec.MinDensity:0}");

                // Live "you are here" projection (what you'd score today).
                if (game.Course != null)
                {
                    var dry = TournamentSystem.Evaluate(game.Course, spec);
                    string col = dry.Passed ? "#80ff80" : "#ffb060";
                    GUILayout.Label($"<b>today you'd score</b> <color={col}>{dry.Score:0}/100 ({dry.Grade})</color>", Rich());
                    GUILayout.Label($"  now: Stimp {dry.MeanStimp:0.0} (±{dry.StimpStdev:0.0})  firm {dry.MeanFirmness:0}");
                }
            }
            else
            {
                GUILayout.Label("<b>Ladder complete.</b>", Rich());
            }

            if (ladder.Results.Count > 0)
            {
                var last = ladder.Results[ladder.Results.Count - 1];
                GUILayout.Space(4);
                GUILayout.Label($"last: {last.Name} — <b>{last.Grade}</b> ({last.Score:0})", Rich());
            }

            GUILayout.EndArea();
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
