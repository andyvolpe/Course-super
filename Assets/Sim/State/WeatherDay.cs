// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.State
{
    /// <summary>One day of weather, generated deterministically from the seed (TDD §3 step 1).</summary>
    public struct WeatherDay
    {
        public double TminF;
        public double TmaxF;
        public double RainMm;
        public double LeafWetnessHrs;     // hours of canopy wetness (dew + rain)
        public double SolarRa;            // extraterrestrial radiation, mm/day equivalent (Hargreaves)
        public double Humidity;           // relative humidity 0..1 (drives dew/leaf-wetness, dollar-spot risk)

        public double TmeanF => 0.5 * (TminF + TmaxF);
    }
}
