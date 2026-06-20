// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Config
{
    /// <summary>
    /// Authored description of one zone to build (TDD §2). The CourseFactory turns these into
    /// live ZoneState, applying the §5 "healthy start" initial values.
    /// </summary>
    public sealed class ZoneSpec
    {
        public string Id = "";
        public ZoneType Type = ZoneType.Fairway;
        public SoilType Soil = SoilType.PushUp;
        public int HoleNumber = 1;

        /// <summary>Greens are sub-zoned into a grid (3x3 = 9 cells in the MVP).</summary>
        public int GridSize = 1;

        public ZoneSpec() { }

        public ZoneSpec(string id, ZoneType type, SoilType soil, int holeNumber, int gridSize)
        {
            Id = id; Type = type; Soil = soil; HoleNumber = holeNumber; GridSize = gridSize;
        }
    }
}
