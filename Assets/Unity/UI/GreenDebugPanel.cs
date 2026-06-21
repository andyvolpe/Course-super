using System.Collections.Generic;
using UnityEngine;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Unity.Managers;

namespace Greenkeeper.Unity.UI
{
    /// <summary>
    /// Phase 3.0 — throwaway feel-test scaffolding. An IMGUI panel for ONE selected green that dumps
    /// live state (including the normally-hidden turfDebt — DEBUG ONLY), applies mismanagement/good
    /// actions, resolves 1 or 7 days, and plots the last ~21 days of dollar-spot pressure + infection
    /// so you can see the CURVE. Kept isolated so Phase 3's real legibility UI can replace it.
    ///
    /// SETUP: add next to a GameManager in the Sandbox scene.
    /// </summary>
    public sealed class GreenDebugPanel : MonoBehaviour
    {
        public GameManager game;
        public int historyLength = 21;

        // Per-green toggles applied as that green's plan each resolve.
        private bool _skipWater, _starveN, _skipSpray, _mowLow;   // mismanagement
        private bool _water, _feedN, _spray, _aerate, _mowSafe;   // good counters

        private int _selected;
        private readonly List<float> _pressure = new List<float>();
        private readonly List<float> _infection = new List<float>();
        private Vector2 _scroll;

        private void Awake()
        {
            if (game == null) game = GetComponent<GameManager>() ?? FindFirstObjectByType<GameManager>();
        }

        private List<ZoneState> Greens()
        {
            var list = new List<ZoneState>();
            if (game?.Course != null) foreach (var g in game.Course.Greens) list.Add(g);
            return list;
        }

        private ZoneState Selected()
        {
            var greens = Greens();
            if (greens.Count == 0) return null;
            _selected = Mathf.Clamp(_selected, 0, greens.Count - 1);
            return greens[_selected];
        }

        private ZoneAction BuildAction()
        {
            // Default safe baseline; toggles push it toward neglect or recovery.
            var a = ZoneAction.None;
            a.Mow = _mowSafe || _mowLow;
            a.MowHeightIn = _mowLow ? 0.08 : 0.125;
            a.IrrigationMm = _skipWater ? 0.0 : (_water ? 14.0 : 6.0);
            a.FertilizerN = _starveN ? 0.0 : (_feedN ? 12.0 : 0.0);
            a.Spray = _spray && !_skipSpray;
            a.Aerate = _aerate;
            return a;
        }

        private void ResolveDays(int n)
        {
            var sel = Selected();
            if (sel == null) return;
            for (int i = 0; i < n; i++)
            {
                var plan = new DayPlan();
                plan.Set(sel.Id, BuildAction()); // only the selected green is driven; others idle
                game.Director.ResolveDay(plan);
                Record(sel);
            }
        }

        private void Record(ZoneState g)
        {
            _pressure.Add((float)g.MeanPressure);
            _infection.Add((float)g.MaxInfection);
            while (_pressure.Count > historyLength) _pressure.RemoveAt(0);
            while (_infection.Count > historyLength) _infection.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (game == null || game.Director == null) return;
            var g = Selected();
            if (g == null) return;

            GUILayout.BeginArea(new Rect(Screen.width - 470, 10, 460, Screen.height - 20), GUI.skin.box);
            GUILayout.Label($"<b>GREEN DEBUG (feel-test)</b>  —  day {game.Director.Clock.DayIndex} / {game.Director.Clock.Season}", Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< prev")) _selected--;
            GUILayout.Label($"  {g.Id}  ", GUILayout.Width(110));
            if (GUILayout.Button("next >")) _selected++;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>Live state</b>", Rich());
            GUILayout.Label($"moisture {g.SoilMoisturePct,5:F1}%   soilTemp {g.SoilTempF,5:F1}F   N {g.NitrogenPct,5:F1}");
            GUILayout.Label($"density  {g.DensityPct,5:F1}%   OM {g.OrganicMatterPct,5:F1}%   carb {g.CarbReservesPct,5:F1}");
            GUILayout.Label($"pressure {g.MeanPressure,5:F1}   infection {g.MaxInfection,5:F1}   expr {g.MaxExpression,5:F1}");
            GUILayout.Label($"<color=#ff8080>turfDebt {g.TurfDebtPct,5:F1}  (DEBUG-ONLY; hidden in real play)</color>", Rich());

            GUILayout.Space(4);
            GUILayout.Label("<b>Mismanage</b>", Rich());
            GUILayout.BeginHorizontal();
            _skipWater = GUILayout.Toggle(_skipWater, "skip water");
            _starveN = GUILayout.Toggle(_starveN, "starve N");
            _skipSpray = GUILayout.Toggle(_skipSpray, "skip spray");
            _mowLow = GUILayout.Toggle(_mowLow, "mow low");
            GUILayout.EndHorizontal();

            GUILayout.Label("<b>Counters (good)</b>", Rich());
            GUILayout.BeginHorizontal();
            _water = GUILayout.Toggle(_water, "water");
            _feedN = GUILayout.Toggle(_feedN, "feed N");
            _spray = GUILayout.Toggle(_spray, "spray");
            _aerate = GUILayout.Toggle(_aerate, "aerate");
            _mowSafe = GUILayout.Toggle(_mowSafe, "mow safe");
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Resolve 1 day")) ResolveDays(1);
            if (GUILayout.Button("Resolve 7 days")) ResolveDays(7);
            if (GUILayout.Button("Reset game")) { game.NewGame(); _pressure.Clear(); _infection.Clear(); }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"<b>Last {historyLength} days</b>  (P=pressure 0-100, I=infection 0-100)", Rich());
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = _pressure.Count - 1; i >= 0; i--)
                GUILayout.Label($"P {Bar(_pressure[i])} {_pressure[i],5:F1}   I {Bar(_infection[i])} {_infection[i],5:F1}");
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private static string Bar(float v01to100)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(v01to100 / 5f), 0, 20);
            return new string('#', n).PadRight(20, '.');
        }

        private static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true };
    }
}
