// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System;

namespace Greenkeeper.Sim.Math
{
    /// <summary>
    /// Deterministic RNG wrapper. The sim injects this everywhere randomness is needed so that
    /// (seed, plan) fully determines the run — NO UnityEngine.Random, NO DateTime.Now (TDD §1/§3).
    /// Backed by a small splitmix64-style generator so behaviour is identical across runtimes
    /// (System.Random is not guaranteed stable across .NET versions / Mono / IL2CPP).
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(int seed) : this(unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 1UL)) { }

        public Rng(ulong state) { _state = state == 0 ? 0x1UL : state; }

        private ulong NextULong()
        {
            // splitmix64
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform double in [0,1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0); // 53-bit mantissa

        /// <summary>Uniform double in [min,max).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Uniform int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Deterministically fork a child generator (e.g. one per zone) from a stream id.</summary>
        public Rng Fork(ulong streamId) => new Rng(_state ^ (streamId * 0xD1342543DE82EF95UL + 0x2545F4914F6CDD1DUL));

        /// <summary>Snapshot/restore for deterministic save-replay.</summary>
        public ulong State => _state;
        public static Rng FromState(ulong state) => new Rng(state);
    }
}
