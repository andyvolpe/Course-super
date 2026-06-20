// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;

namespace Greenkeeper.Sim.State
{
    /// <summary>
    /// Spatially-resolved disease state for one cell of a green's grid (greens are 3x3 = 9 cells;
    /// other zones use a single cell). Disease pressure diffuses between neighbouring cells, so a
    /// hot spot can appear before the whole green is affected (TDD §4.2 spread).
    /// </summary>
    public sealed class SubCell
    {
        public double Pressure;          // 0..100 latent disease pressure
        public double Infection;         // 0..100 active infection level
        public double ExpressionSeverity; // 0..100 visible symptom severity

        public SubCell Clone() => new SubCell
        {
            Pressure = Pressure,
            Infection = Infection,
            ExpressionSeverity = ExpressionSeverity
        };

        public void CollectStateValues(List<double> into)
        {
            into.Add(Pressure);
            into.Add(Infection);
            into.Add(ExpressionSeverity);
        }
    }
}
