// Greenkeeper.Tests — pure NUnit-compatible (no UnityEngine).
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Tests
{
    /// <summary>Asserts every state variable is finite and inside its legal range (T2 invariants).</summary>
    public static class StateValidator
    {
        public static string FirstViolation(CourseState course, AgronomyTuning t)
        {
            foreach (var z in course.Zones)
            {
                string v = CheckZone(z, t);
                if (v != null) return v;
            }
            return null;
        }

        private static string CheckZone(ZoneState z, AgronomyTuning t)
        {
            string r;
            if ((r = Range(z.Id, "moisture", z.SoilMoisturePct, t.ResidualMoisturePct - 1e-6, t.SaturationPct + 1e-6)) != null) return r;
            if ((r = Range(z.Id, "density", z.DensityPct, 0, 100)) != null) return r;
            if ((r = Range(z.Id, "OM", z.OrganicMatterPct, 0, 100)) != null) return r;
            if ((r = Range(z.Id, "debt", z.TurfDebtPct, 0, 100)) != null) return r;
            if ((r = Range(z.Id, "carb", z.CarbReservesPct, 0, 100)) != null) return r;
            if ((r = Range(z.Id, "nitrogen", z.NitrogenPct, 0, 100)) != null) return r;
            if ((r = Range(z.Id, "grain", z.GrainPct, 0, 100)) != null) return r;
            if ((r = Range(z.Id, "firmness", z.FirmnessPct, 0, 100)) != null) return r;
            if ((r = Finite(z.Id, "soilTemp", z.SoilTempF)) != null) return r;
            if ((r = Finite(z.Id, "gdd", z.GddAccum)) != null) return r;
            if (z.GddAccum < 0) return $"{z.Id}.gdd negative ({z.GddAccum})";
            if (z.Type != ZoneType.Bunker)
                if ((r = Range(z.Id, "stimp", z.Stimp, t.StimpMin - 1e-6, t.StimpMax + 1e-6)) != null) return r;

            foreach (var c in z.Cells)
            {
                if ((r = Range(z.Id, "pressure", c.Pressure, 0, 100)) != null) return r;
                if ((r = Range(z.Id, "infection", c.Infection, 0, 100)) != null) return r;
                if ((r = Range(z.Id, "expression", c.ExpressionSeverity, 0, 100)) != null) return r;
            }
            return null;
        }

        private static string Range(string id, string field, double v, double min, double max)
        {
            if (!Mathx.IsFinite(v)) return $"{id}.{field} not finite ({v})";
            if (v < min || v > max) return $"{id}.{field} out of range: {v} not in [{min},{max}]";
            return null;
        }

        private static string Finite(string id, string field, double v)
            => Mathx.IsFinite(v) ? null : $"{id}.{field} not finite ({v})";
    }
}
