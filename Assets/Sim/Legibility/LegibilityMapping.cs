// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Legibility
{
    /// <summary>
    /// Pure function: zone/cell state -> the honest visual tell (TDD §6). Lives in the Sim so the
    /// fairness contract (tells NARROW, never pinpoint) is unit-testable headless, and so the Unity
    /// shader is a dumb consumer.
    ///
    /// Fairness rules encoded here:
    ///  • colour DEPTH saturates at adequate nitrogen, so healthy-vigorous and N-pushed look identical
    ///    (dark green = "fed enough OR pushed" — context disambiguates later); only STARVATION reads
    ///    as pale. The tell narrows, it does not answer.
    ///  • turf debt is NOT an input — it shows only THROUGH the thinning it causes.
    /// </summary>
    public static class LegibilityMapping
    {
        public static TellAppearance ForCell(ZoneState z, int cellIndex, AgronomyTuning t)
        {
            double wilt = t.WiltPoint(z.Soil);
            double fc = t.FieldCapacity(z.Soil, z.OrganicMatterPct);

            // --- Colour depth: nitrogen sufficiency SATURATES, so vigour == N-push (narrowing) ---
            // Iron greens up like N FOR COLOUR ONLY — it adds to the same saturating curve, so a lean
            // (in-band) green can be made to LOOK fed without the growth/disease cost of more N. Above
            // optimum the curve is still flat, so fed and pushed remain indistinguishable (fairness).
            double nSufficiency = Mathx.Clamp01((z.NitrogenPct + z.IronPct) / t.NitrogenOptimum);
            double densityNorm = Mathx.Clamp01(z.DensityPct / 100.0);
            // Depth driven by whichever is limiting; both vigorous (N=opt) and pushed (N>opt) hit 1.0.
            double depth = Mathx.Clamp01(0.35 + 0.65 * nSufficiency) * Mathx.Clamp01(0.4 + 0.6 * densityNorm);

            // Pale starved yellow-green -> deep green as depth rises (hue stays "green", only depth moves).
            Rgb baseColor = new Rgb(
                Mathx.Lerp(0.62, 0.10, depth),   // R: pale has more red/yellow
                Mathx.Lerp(0.70, 0.32, depth),   // G: always green-dominant
                Mathx.Lerp(0.30, 0.12, depth));  // B: low

            // --- Moisture tells: wilt (dry) vs wet sheen (saturated) ---
            double wiltTint = 1.0 - Mathx.InverseLerp(wilt, wilt + 6.0, z.SoilMoisturePct); // strong below wilt+6
            wiltTint = Mathx.Clamp01(wiltTint);
            double wetSheen = Mathx.InverseLerp(fc, t.SaturationPct, z.SoilMoisturePct);     // grows above FC

            // --- Infection -> lesions on THIS cell (spatially resolved) ---
            double infection = cellIndex >= 0 && cellIndex < z.Cells.Length ? z.Cells[cellIndex].Infection : z.MeanInfection;
            double expression = cellIndex >= 0 && cellIndex < z.Cells.Length ? z.Cells[cellIndex].ExpressionSeverity : z.MeanExpression;
            // Only EXPRESSED symptoms are visible (latent infection without expression stays hidden).
            double lesions = Mathx.Clamp01(expression / 100.0);

            // --- Thinning from low density (turf debt shows ONLY through this, never directly) ---
            double thinning = 1.0 - densityNorm;

            return new TellAppearance
            {
                BaseColor = baseColor,
                Lesions = lesions,
                Thinning = Mathx.Clamp01(thinning),
                WetSheen = Mathx.Clamp01(wetSheen),
                WiltTint = wiltTint,
            };
        }
    }
}
