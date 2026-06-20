using UnityEngine;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Phase 6 — the books. Shows cash, reputation, course condition, and yesterday's ledger
    /// (rounds / revenue / costs / net) so the player feels mismanagement in the wallet, not just the
    /// eye. Cash turns red in the negative.
    /// </summary>
    public sealed class EconomyHud : MonoBehaviour
    {
        public GameManager game;

        private void Awake()
        {
            if (game == null) game = FindObjectOfType<GameManager>();
        }

        private void OnGUI()
        {
            var eco = game != null ? game.Economy : null;
            if (eco == null) return;

            GUILayout.BeginArea(new Rect(Screen.width / 2 - 170, 28, 340, 92), GUI.skin.box);
            string cashColor = eco.Cash < 0 ? "#ff6060" : "#a0ffa0";
            GUILayout.Label($"<b>CASH</b> <color={cashColor}>${eco.Cash:N0}</color>    reputation {eco.Reputation:F0}/100", Rich());

            DayLedger l = eco.Latest;
            if (eco.History.Count > 0)
            {
                string netColor = l.Net < 0 ? "#ff8080" : "#80ff80";
                GUILayout.Label($"condition {l.ConditionIndex:F0}/100   rounds {l.Rounds:F0}");
                GUILayout.Label($"revenue ${l.Revenue:N0}  −  costs ${l.Costs:N0}  =  " +
                                $"<color={netColor}>${l.Net:N0}</color>", Rich());
            }
            else
            {
                GUILayout.Label("Resolve a day to see the books.");
            }
            GUILayout.EndArea();
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
