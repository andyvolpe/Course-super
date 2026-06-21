// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Sim.State
{
    /// <summary>
    /// Full live state for one managed zone (TDD §2 schema). Natural units; normalized inside math.
    /// Every accumulator is clamped each tick by the systems that own it.
    /// </summary>
    public sealed class ZoneState
    {
        // Identity / construction
        public string Id = "";
        public ZoneType Type;
        public SoilType Soil;
        public int HoleNumber;

        /// <summary>The surface's tuning/profile (per-SurfaceType agronomy). Set by CourseFactory; the
        /// reference is shared (immutable config) so Clone/determinism are unaffected.</summary>
        public SurfaceProfile Surface;

        // Spatial disease grid (length = GridSize*GridSize; 9 for greens, 1 otherwise)
        public int GridSize = 1;
        public SubCell[] Cells = System.Array.Empty<SubCell>();

        // ---- Soil / water (§4.1) ----
        public double SoilMoisturePct;   // volumetric water content %
        public double RootDepthIn;       // rooting depth, inches

        // ---- Turf / biology (§4.7) ----
        public double DensityPct;        // canopy density / cover, 0..100
        public double OrganicMatterPct;  // OM / thatch, 0..100
        public double TurfDebtPct;       // single mismanagement accumulator, 0..100 (§4.5)
        public double CarbReservesPct;   // carbohydrate reserves, 0..100
        public double NitrogenPct;       // available nitrogen, 0..100
        public double PotassiumPct;      // available potassium (K) — stress-tolerance pool, 0..100
        public double IronPct;           // foliar iron (Fe) — colour WITHOUT growth; decays fast, 0..100
        public double SoilTempF;         // lagged soil temperature
        public double GddAccum;          // accumulated growing degree days
        public double GrainPct;          // grain, 0..100
        public double ClipVolume;        // yesterday's clip yield (informational)

        // ---- Derived playing surface (§4.4) ----
        public double FirmnessPct;       // 0..100
        public double Stimp;             // green speed, feet (clamped 6..15)

        // ---- Bunker (non-turf) ----
        public double SandQualityPct;    // sand consistency 0..100 — clean firm vs settled/washed (lie quality)

        // ---- Maintenance bookkeeping ----
        public double MowHeightIn = 0.125; // current height of cut
        public int SprayResidualDaysLeft;
        public int DaysSinceAeration;
        public int AerationRecoveryDaysLeft;
        public double RollBonus;          // transient Stimp bonus from rolling (decays)
        public bool WashedOut;            // bunker washed out by a storm — needs raking (Phase 5.3)

        public bool IsGreen => Type == ZoneType.Green;

        // ---- Spatial aggregates (recomputed cheaply from cells) ----
        public double MeanPressure => CellMean(c => c.Pressure);
        public double MeanInfection => CellMean(c => c.Infection);
        public double MeanExpression => CellMean(c => c.ExpressionSeverity);
        public double MaxInfection => CellMax(c => c.Infection);
        public double MaxExpression => CellMax(c => c.ExpressionSeverity);

        private double CellMean(System.Func<SubCell, double> f)
        {
            if (Cells.Length == 0) return 0;
            double s = 0; for (int i = 0; i < Cells.Length; i++) s += f(Cells[i]);
            return s / Cells.Length;
        }

        private double CellMax(System.Func<SubCell, double> f)
        {
            double m = 0; for (int i = 0; i < Cells.Length; i++) { double v = f(Cells[i]); if (v > m) m = v; }
            return m;
        }

        public ZoneState Clone()
        {
            var z = (ZoneState)MemberwiseClone();
            z.Cells = new SubCell[Cells.Length];
            for (int i = 0; i < Cells.Length; i++) z.Cells[i] = Cells[i].Clone();
            return z;
        }

        /// <summary>Flattens all numeric state into a list for deterministic deep-equality (T1).</summary>
        public void CollectStateValues(List<double> into)
        {
            into.Add(SoilMoisturePct);
            into.Add(RootDepthIn);
            into.Add(DensityPct);
            into.Add(OrganicMatterPct);
            into.Add(TurfDebtPct);
            into.Add(CarbReservesPct);
            into.Add(NitrogenPct);
            into.Add(PotassiumPct);
            into.Add(IronPct);
            into.Add(SoilTempF);
            into.Add(GddAccum);
            into.Add(GrainPct);
            into.Add(ClipVolume);
            into.Add(FirmnessPct);
            into.Add(Stimp);
            into.Add(SandQualityPct);
            into.Add(MowHeightIn);
            into.Add(SprayResidualDaysLeft);
            into.Add(DaysSinceAeration);
            into.Add(AerationRecoveryDaysLeft);
            into.Add(RollBonus);
            into.Add(WashedOut ? 1.0 : 0.0);
            for (int i = 0; i < Cells.Length; i++) Cells[i].CollectStateValues(into);
        }
    }
}
