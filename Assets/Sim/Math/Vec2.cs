// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System;

namespace Greenkeeper.Sim.Math
{
    /// <summary>Minimal 2D vector so ball physics stays in the engine-free Sim (no UnityEngine.Vector2).</summary>
    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }

        public static Vec2 Zero => new Vec2(0, 0);

        public double Length => System.Math.Sqrt(X * X + Y * Y);
        public double Dot(Vec2 o) => X * o.X + Y * o.Y;

        public Vec2 Normalized()
        {
            double len = Length;
            return len < 1e-9 ? new Vec2(0, 0) : new Vec2(X / len, Y / len);
        }

        /// <summary>Left-hand perpendicular.</summary>
        public Vec2 Perp() => new Vec2(-Y, X);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);

        public override string ToString() => $"({X:F2},{Y:F2})";
    }
}
