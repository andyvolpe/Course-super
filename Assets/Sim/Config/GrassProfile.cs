// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Config
{
    /// <summary>
    /// Authored static data describing a turfgrass cultivar (TDD §2). Plain C# so the sim can
    /// consume it headless; the Unity layer authors it via a ScriptableObject wrapper.
    /// </summary>
    public sealed class GrassProfile
    {
        public string Name = "Creeping Bentgrass (MVP)";

        /// <summary>Lowest safe mowing height (inches). Mowing below this scalps (turf debt).</summary>
        public double MinSafeMowHeightIn = 0.10;

        /// <summary>Temperature optimum for growth (degF).</summary>
        public double GrowthOptimumF = 65.0;

        /// <summary>Optimal nitrogen band (nOptBand). Below = dollar-spot/weakness; above = brown-patch/
        /// Pythium, thatch, weak roots, burn. Deviation either way is punished.</summary>
        public double NOptMin = 40.0;
        public double NOptMax = 60.0;
        public double NOptMid => 0.5 * (NOptMin + NOptMax);

        /// <summary>Multiplier on disease susceptibility (1.0 = baseline dollar-spot susceptibility).</summary>
        public double DiseaseSusceptibility = 1.0;

        /// <summary>Multiplier on recuperative capacity (density gain). Higher = recovers faster.</summary>
        public double RecuperativeRate = 1.0;

        public static GrassProfile Mvp() => new GrassProfile();
    }
}
