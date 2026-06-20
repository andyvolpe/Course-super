// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>
    /// Dollar spot only (TDD §4.2 / §4.3). Favorability -> per-cell pressure accrual -> diffusion
    /// across the green's grid -> tell-gated infection -> FAIRNESS-GATED expression.
    ///
    /// The fairness contract (GDD §3.1): a visible symptom may fire ONLY when a readable tell is
    /// present (pressure over threshold, active infection, moisture below wilt, or density below the
    /// thin threshold). With no tell, P(express) = 0. This gate is mandatory and test-locked (T4).
    /// </summary>
    public static class DiseaseSystem
    {
        public static void Apply(ZoneState z, ZoneAction action, ResolveContext ctx, Rng rng)
        {
            var t = ctx.Tuning;
            var w = ctx.Weather;

            if (z.Type == ZoneType.Bunker)
            {
                ctx.Result.TellByZone[z.Id] = false;
                return;
            }

            // --- Favorability (each factor guarded max(0,...), multiplied together) ---
            double fc = t.FieldCapacity(z.Soil, z.OrganicMatterPct);
            double tempFactor = Mathx.Bell(w.TmeanF, t.DiseaseTempCenterF, t.DiseaseTempHalfWidthF);
            double effectiveWetness = w.LeafWetnessHrs
                + action.IrrigationMm * t.IrrigationWetnessHrsPerMm
                + Mathx.Max0(z.SoilMoisturePct - fc) * t.SaturatedExcessWetnessHrs;
            double wetnessFactor = Mathx.Clamp01(effectiveWetness / t.LeafWetnessOptimumHrs);
            // Baseline susceptibility + starvation amplification, guarded and capped.
            double nitrogenFactor = Mathx.Clamp(
                t.DiseaseNitrogenBase + (1.0 - z.NitrogenPct / t.NitrogenOptimum),
                0.0, t.DiseaseNitrogenMax);

            double favorability = Mathx.Max0(tempFactor) * Mathx.Max0(wetnessFactor)
                                  * nitrogenFactor * ctx.Grass.DiseaseSusceptibility;
            if (z.SprayResidualDaysLeft > 0) favorability *= t.SprayResidualFavorabilityMult;

            // --- Per-cell pressure accrual (small jitter so a hot spot can lead) ---
            for (int i = 0; i < z.Cells.Length; i++)
            {
                var cell = z.Cells[i];
                double jitter = 0.85 + 0.30 * rng.NextDouble();
                cell.Pressure = Mathx.Clamp(
                    cell.Pressure + favorability * t.PressureGain * jitter - t.PressureDecay * cell.Pressure,
                    0.0, 100.0);
            }

            // --- Spread: diffuse pressure across the grid toward neighbour mean ---
            if (z.GridSize > 1) Diffuse(z, t.SpreadDiffusion);

            // --- Infection (tell-gated growth) + expression (fairness-gated) ---
            bool zoneEnvTell = z.SoilMoisturePct < t.WiltPoint(z.Soil) || z.DensityPct < t.ThinDensityThreshold;
            bool anyTell = zoneEnvTell;

            for (int i = 0; i < z.Cells.Length; i++)
            {
                var cell = z.Cells[i];

                // Infection can only advance once pressure crosses the tell threshold, so a readable
                // tell always precedes infection (keeps the cause chain legible).
                double grow = cell.Pressure > t.TellPressureThreshold
                    ? t.InfectionGain * (cell.Pressure - t.TellPressureThreshold)
                    : 0.0;
                double heal = t.InfectionRecovery * (1.0 - cell.Pressure / 100.0);
                cell.Infection = Mathx.Clamp(cell.Infection + grow - heal, 0.0, 100.0);

                bool cellTell = cell.Pressure > t.TellPressureThreshold || cell.Infection > 0.0;
                bool tell = cellTell || zoneEnvTell;
                anyTell = anyTell || tell;

                bool gateOpen = tell || ctx.BypassFairnessGate;
                if (gateOpen)
                {
                    double basis = System.Math.Max(cell.Pressure, cell.Infection);
                    double pExpress = Mathx.Clamp01(basis / t.ExpressionPressureScale) * t.ExpressionChanceMax;
                    if (ctx.BypassFairnessGate) pExpress = System.Math.Max(pExpress, 0.2); // teeth: fire even with no tell

                    if (pExpress > 0.0 && rng.Chance(pExpress))
                    {
                        cell.ExpressionSeverity = Mathx.Clamp(cell.ExpressionSeverity + t.ExpressionSeverityStep, 0.0, 100.0);
                        ctx.Result.Expressions.Add(new ExpressionEvent
                        {
                            DayIndex = ctx.DayIndex,
                            ZoneId = z.Id,
                            CellIndex = i,
                            Severity = cell.ExpressionSeverity
                        });
                    }
                }

                // Symptoms fade once the infection clears.
                if (cell.Infection <= 0.01)
                    cell.ExpressionSeverity = Mathx.Max0(cell.ExpressionSeverity - 1.0);
            }

            // Active dollar spot scars the canopy: infection thins density directly. This is what makes
            // a sick green visibly thin (and lets the symptom show THROUGH density, per the legibility
            // contract) — distinct from, and on top of, turf-debt bleed.
            double meanInfection = z.MeanInfection;
            if (meanInfection > 0.0)
                z.DensityPct = Mathx.Clamp(z.DensityPct - (meanInfection / 100.0) * t.InfectionDensityLoss, 0.0, 100.0);

            ctx.Result.TellByZone[z.Id] = anyTell;
        }

        private static void Diffuse(ZoneState z, double rate)
        {
            int n = z.GridSize;
            var next = new double[z.Cells.Length];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++)
                {
                    int idx = r * n + c;
                    double sum = 0; int count = 0;
                    if (r > 0) { sum += z.Cells[(r - 1) * n + c].Pressure; count++; }
                    if (r < n - 1) { sum += z.Cells[(r + 1) * n + c].Pressure; count++; }
                    if (c > 0) { sum += z.Cells[r * n + (c - 1)].Pressure; count++; }
                    if (c < n - 1) { sum += z.Cells[r * n + (c + 1)].Pressure; count++; }
                    double neighbourMean = count > 0 ? sum / count : z.Cells[idx].Pressure;
                    next[idx] = z.Cells[idx].Pressure + rate * (neighbourMean - z.Cells[idx].Pressure);
                }
            }
            for (int i = 0; i < z.Cells.Length; i++)
                z.Cells[i].Pressure = Mathx.Clamp(next[i], 0.0, 100.0);
        }
    }
}
