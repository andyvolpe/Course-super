// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Legibility
{
    /// <summary>
    /// THE single legibility gate (GDD §3). Everything the UI may show passes through here, so hidden
    /// state can never leak: by default the player sees only the free visual tells; infection detail,
    /// exact moisture, and nutrient trends must be EARNED with scout/meter/soil-test tools; and raw
    /// hidden state (true pressure, turf debt on full difficulty) is never exposed.
    ///
    /// Crucially this is READ-ONLY with respect to the simulation — it observes CourseState and records
    /// what the player has earned, but never mutates sim state. That is what keeps the assist layer
    /// (§3.1) from changing outcomes and keeps the headless determinism tests valid.
    /// </summary>
    public sealed class LegibilitySystem
    {
        public DifficultySettings Settings;
        private readonly AgronomyTuning _tuning;

        public int ScoutDurationDays = 3;
        public int SoilTestDurationDays = 7;
        public int MeterFreshnessDays = 1;

        private sealed class Reveal
        {
            public int ScoutUntilDay = int.MinValue;
            public int SoilTestUntilDay = int.MinValue;
            public int MeterDay = int.MinValue;
            public int MeterCell;
            public double MeterMoisture;
        }

        private readonly Dictionary<string, Reveal> _reveals = new Dictionary<string, Reveal>();

        public LegibilitySystem(AgronomyTuning tuning, DifficultySettings difficulty = null)
        {
            _tuning = tuning ?? AgronomyTuning.Default;
            Settings = difficulty ?? DifficultySettings.Full();
        }

        private Reveal Get(string zoneId)
        {
            if (!_reveals.TryGetValue(zoneId, out var r)) { r = new Reveal(); _reveals[zoneId] = r; }
            return r;
        }

        // ---- Earned-information tools (the player spends time on these) ----

        /// <summary>Scout a zone: reveals expressed-symptom detail for a few days.</summary>
        public void Scout(string zoneId, int dayIndex) => Get(zoneId).ScoutUntilDay = dayIndex + ScoutDurationDays;

        /// <summary>Soil test a zone: reveals nutrient/OM trend for a week.</summary>
        public void SoilTest(string zoneId, int dayIndex) => Get(zoneId).SoilTestUntilDay = dayIndex + SoilTestDurationDays;

        /// <summary>
        /// Take a moisture-meter reading at a spot: returns the TRUE VWC for this reading only and
        /// records it so the UI may display it briefly. Does not mutate the sim.
        /// </summary>
        public double MeterReading(ZoneState zone, int cellIndex, int dayIndex)
        {
            var r = Get(zone.Id);
            r.MeterDay = dayIndex;
            r.MeterCell = cellIndex;
            r.MeterMoisture = zone.SoilMoisturePct; // spot VWC (cells share zone moisture in the MVP)
            return r.MeterMoisture;
        }

        // ---- The gate: what may the UI show right now? ----

        public ObservableZone Observe(ZoneState zone, int dayIndex)
        {
            var r = Get(zone.Id);
            bool assists = Settings.Mode == Difficulty.Assisted;

            // Free tells are always available.
            var tells = new TellAppearance[zone.Cells.Length];
            for (int i = 0; i < zone.Cells.Length; i++)
                tells[i] = LegibilityMapping.ForCell(zone, i, _tuning);

            var obs = new ObservableZone { ZoneId = zone.Id, Tells = tells };

            // Infection detail: earned by scouting, or kept fresh by AutoScout assist.
            bool scouted = dayIndex <= r.ScoutUntilDay || (assists && Settings.AutoScout);
            if (scouted)
            {
                obs.InfectionRevealed = true;
                obs.RevealedMaxInfection = zone.MaxInfection;
                obs.RevealedMeanExpression = zone.MeanExpression;
            }

            // Exact moisture: ONLY from a fresh meter reading.
            if (dayIndex - r.MeterDay <= MeterFreshnessDays && r.MeterDay != int.MinValue)
            {
                obs.MoistureMetered = true;
                obs.MeteredCellIndex = r.MeterCell;
                obs.MeteredMoisturePct = r.MeterMoisture;
            }

            // Nutrient/OM trend: earned by soil test.
            if (dayIndex <= r.SoilTestUntilDay)
            {
                obs.SoilTested = true;
                obs.RevealedNitrogenPct = zone.NitrogenPct;
                obs.RevealedOrganicMatterPct = zone.OrganicMatterPct;
            }

            // Turf debt: assist-only bar. NEVER surfaced on full difficulty.
            if (assists && Settings.ShowTurfDebtBar)
            {
                obs.TurfDebtShown = true;
                obs.TurfDebtPct = zone.TurfDebtPct;
            }

            // Threat telegraph: assist-only early warning from otherwise-hidden pressure.
            if (assists && Settings.TelegraphThreats)
            {
                double pressure = zone.MeanPressure;
                if (pressure > _tuning.TellPressureThreshold * 0.5 && zone.MaxInfection <= 0.0)
                {
                    obs.ThreatTelegraphed = true;
                    obs.ThreatNote = "Dollar-spot pressure building.";
                }
            }

            return obs;
        }
    }
}
