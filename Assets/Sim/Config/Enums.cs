// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Config
{
    public enum ZoneType
    {
        Green,
        Tee,
        Fairway,
        Rough,
        Bunker
    }

    /// <summary>
    /// Rootzone construction. Drives drainage rate and field capacity: a USGA-spec sand profile
    /// drains far faster than a native push-up green at equal input (TDD §4.1, test 2.2b).
    /// </summary>
    public enum SoilType
    {
        UsgaSpec,   // engineered sand rootzone — fast drainage, low FC
        PushUp      // native soil ("push-up") green — slow drainage, high FC, holds water
    }

    public enum Season
    {
        Spring = 0,
        Summer = 1,
        Fall = 2,
        Winter = 3
    }
}
