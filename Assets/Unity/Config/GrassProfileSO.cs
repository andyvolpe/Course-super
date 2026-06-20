using UnityEngine;
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Unity.Config
{
    /// <summary>
    /// ScriptableObject WRAPPER for authoring a GrassProfile in the editor. ScriptableObject is
    /// UnityEngine, so it must NOT leak into Greenkeeper.Sim — this class only holds authored values
    /// and converts them to the plain-C# <see cref="GrassProfile"/> the sim consumes (TDD §2 / §9).
    /// </summary>
    [CreateAssetMenu(menuName = "Greenkeeper/Grass Profile", fileName = "GrassProfile")]
    public sealed class GrassProfileSO : ScriptableObject
    {
        public string grassName = "Creeping Bentgrass (MVP)";
        public float minSafeMowHeightIn = 0.10f;
        public float growthOptimumF = 65f;
        public float diseaseSusceptibility = 1f;
        public float recuperativeRate = 1f;

        public GrassProfile ToConfig() => new GrassProfile
        {
            Name = grassName,
            MinSafeMowHeightIn = minSafeMowHeightIn,
            GrowthOptimumF = growthOptimumF,
            DiseaseSusceptibility = diseaseSusceptibility,
            RecuperativeRate = recuperativeRate,
        };
    }
}
