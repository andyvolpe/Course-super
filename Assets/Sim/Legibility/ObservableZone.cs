// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Legibility
{
    /// <summary>
    /// Exactly what the UI is permitted to show for a zone right now — the output of the single
    /// legibility gate. The free visual tells are always present; everything else is null unless it
    /// has been EARNED (scout/meter/soil test) or surfaced by an assist. Raw hidden state
    /// (true pressure, exact moisture without a meter, turf debt on full difficulty) is never here.
    /// </summary>
    public sealed class ObservableZone
    {
        public string ZoneId;

        // Always available: the honest shader tells, per sub-cell.
        public TellAppearance[] Tells;

        // Earned via Scout (or AutoScout): expressed-symptom detail.
        public bool InfectionRevealed;
        public double RevealedMaxInfection;
        public double RevealedMeanExpression;

        // Earned via the moisture meter: true VWC for the metered cell only.
        public bool MoistureMetered;
        public int MeteredCellIndex;
        public double MeteredMoisturePct;

        // Earned via a soil test: nutrient / OM trend.
        public bool SoilTested;
        public double RevealedNitrogenPct;
        public double RevealedOrganicMatterPct;

        // Assist-only surfacing (never on full difficulty).
        public bool TurfDebtShown;
        public double TurfDebtPct;
        public bool ThreatTelegraphed;
        public string ThreatNote;
    }
}
