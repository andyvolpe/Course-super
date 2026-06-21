using UnityEngine;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Phase 5.4 — the forecast strip. Shows the next few days with error bands that visibly WIDEN
    /// further out, so the player can act on imperfect information: pre-water ahead of a forecast heat
    /// spike, hold a fungicide when humidity is uncertain, double-cut early before forecast rain. The
    /// forecast is NOT the truth — the band is the gamble. Read it; decide; live with the miss.
    ///
    /// SETUP: add next to GameManager.
    /// </summary>
    public sealed class ForecastHud : MonoBehaviour
    {
        public GameManager game;

        private void Awake()
        {
            if (game == null) game = FindFirstObjectByType<GameManager>();
        }

        private void OnGUI()
        {
            if (game == null || game.Forecast == null || game.Director == null) return;

            ForecastDay[] days = game.Forecast.Upcoming(game.Director.Clock.DayIndex);

            GUILayout.BeginArea(new Rect(10, Screen.height - 170, Screen.width - 500, 160), GUI.skin.box);
            GUILayout.Label("<b>FORECAST</b>  (bands widen further out — the gamble)", Rich());

            GUILayout.BeginHorizontal();
            foreach (var d in days)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(150));
                GUILayout.Label($"<b>+{d.DaysOut}d</b>  conf {d.Confidence:0%}", Rich());
                GUILayout.Label($"hi {d.Predicted.TmaxF:0}±{d.TempBandF:0}F");
                GUILayout.Label($"lo {d.Predicted.TminF:0}±{d.TempBandF:0}F");
                GUILayout.Label($"rain {d.Predicted.RainMm:0}±{d.RainBandMm:0}mm");
                GUILayout.Label($"hum {d.Predicted.Humidity:0%}");
                string warn = "";
                if (d.PredictedHeatSpike) warn += "<color=#ff8060>HEAT</color> ";
                if (d.PredictedStorm) warn += "<color=#80a0ff>STORM</color> ";
                if (d.PredictedFrost) warn += "<color=#a0e0ff>FROST</color> ";
                GUILayout.Label(string.IsNullOrEmpty(warn) ? "—" : warn, Rich());
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
