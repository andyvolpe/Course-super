// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Tournament
{
    /// <summary>
    /// Grades the course against a setup spec (deterministic). Rewards hitting the Stimp/firmness bands
    /// across ALL greens consistently, and punishes disease and thin turf — exactly the things the
    /// agronomy core makes you earn.
    /// </summary>
    public static class TournamentSystem
    {
        public static TournamentResult Evaluate(CourseState course, TournamentSpec spec)
        {
            var stimps = new List<double>();
            double sumScore = 0, sumStimp = 0, sumFirm = 0, sumDensity = 0, worstInf = 0;
            int n = 0;

            foreach (var g in course.Greens)
            {
                double sStimp = BandScore(g.Stimp, spec.StimpMin, spec.StimpMax, 2.0);
                double sFirm = BandScore(g.FirmnessPct, spec.FirmMin, spec.FirmMax, 20.0);
                double sHealth = g.MaxInfection <= spec.MaxInfection ? 1.0
                              : Mathx.Clamp01(1.0 - (g.MaxInfection - spec.MaxInfection) / 30.0);
                double sDensity = g.DensityPct >= spec.MinDensity ? 1.0
                              : Mathx.Clamp01(g.DensityPct / System.Math.Max(1.0, spec.MinDensity));

                double greenScore = 0.40 * sStimp + 0.25 * sFirm + 0.20 * sHealth + 0.15 * sDensity;
                sumScore += greenScore;

                stimps.Add(g.Stimp);
                sumStimp += g.Stimp; sumFirm += g.FirmnessPct; sumDensity += g.DensityPct;
                if (g.MaxInfection > worstInf) worstInf = g.MaxInfection;
                n++;
            }

            if (n == 0) return new TournamentResult { Name = spec.Name, DayIndex = spec.DayIndex, Grade = TournamentGrade.Fail };

            double baseScore = sumScore / n;

            // Consistency: greens should roll alike. Penalize Stimp spread beyond the tolerance.
            double meanStimp = sumStimp / n;
            double stdev = Stdev(stimps, meanStimp);
            double consistencyPenalty = Mathx.Clamp01((stdev - spec.ConsistencyToleranceStimp)
                                                      / System.Math.Max(1e-6, spec.ConsistencyToleranceStimp)) * 0.15;

            double score = Mathx.Clamp(baseScore - consistencyPenalty, 0, 1) * 100.0;
            var grade = GradeFor(score, spec);

            return new TournamentResult
            {
                Name = spec.Name,
                DayIndex = spec.DayIndex,
                Score = score,
                Grade = grade,
                Passed = score >= spec.PassScore,
                MeanStimp = meanStimp,
                MeanFirmness = sumFirm / n,
                StimpStdev = stdev,
                WorstInfection = worstInf,
                MeanDensity = sumDensity / n,
            };
        }

        private static TournamentGrade GradeFor(double score, TournamentSpec spec)
        {
            if (score >= 90) return TournamentGrade.Gold;
            if (score >= System.Math.Max(75, spec.PassScore)) return TournamentGrade.Silver;
            if (score >= spec.PassScore) return TournamentGrade.Bronze;
            return TournamentGrade.Fail;
        }

        /// <summary>1 inside [min,max]; falls off linearly over <paramref name="falloff"/> outside it.</summary>
        public static double BandScore(double v, double min, double max, double falloff)
        {
            if (v >= min && v <= max) return 1.0;
            double dist = v < min ? min - v : v - max;
            return Mathx.Clamp01(1.0 - dist / falloff);
        }

        private static double Stdev(List<double> xs, double mean)
        {
            if (xs.Count == 0) return 0;
            double s = 0; foreach (var x in xs) s += (x - mean) * (x - mean);
            return System.Math.Sqrt(s / xs.Count);
        }
    }
}
