// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System;

namespace Greenkeeper.Sim.Math
{
    /// <summary>
    /// Small, allocation-free math helpers used throughout the sim. Kept here so the
    /// simulation never needs UnityEngine.Mathf (which would break the assembly wall).
    /// </summary>
    public static class Mathx
    {
        public const double Epsilon = 1e-9;

        public static double Clamp(double v, double min, double max)
            => v < min ? min : (v > max ? max : v);

        /// <summary>Clamp to [0,1].</summary>
        public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);

        public static double Lerp(double a, double b, double t) => a + (b - a) * Clamp01(t);

        /// <summary>Inverse lerp, clamped to [0,1]. Returns 0 when the range is degenerate.</summary>
        public static double InverseLerp(double a, double b, double v)
        {
            double denom = b - a;
            if (System.Math.Abs(denom) < Epsilon) return 0.0;
            return Clamp01((v - a) / denom);
        }

        public static double Max0(double v) => v < 0.0 ? 0.0 : v;

        /// <summary>sqrt with a non-negative guard — sqrt(max(0, v)). Mandatory safety guard (TDD §4.1).</summary>
        public static double SafeSqrt(double v) => System.Math.Sqrt(Max0(v));

        /// <summary>
        /// A symmetric bell response in [0,1], peaking at <paramref name="center"/> and reaching
        /// 0 at +/- <paramref name="halfWidth"/>. Used for temperature-optimum responses.
        /// </summary>
        public static double Bell(double x, double center, double halfWidth)
        {
            if (halfWidth <= Epsilon) return 0.0;
            double d = (x - center) / halfWidth;
            return Max0(1.0 - d * d);
        }

        public static bool IsFinite(double v) => !(double.IsNaN(v) || double.IsInfinity(v));
    }
}
