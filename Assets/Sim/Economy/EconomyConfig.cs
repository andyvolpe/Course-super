// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
namespace Greenkeeper.Sim.Economy
{
    /// <summary>
    /// Economy tuning (the MVP "course as a business"). Separate from AgronomyTuning because it is a
    /// different domain. Condition drives demand drives revenue; maintenance costs money — so neglect
    /// hurts the books, not just the looks.
    /// </summary>
    public sealed class EconomyConfig
    {
        public static readonly EconomyConfig Default = new EconomyConfig();

        // ---- Bank ----
        public double StartingCash = 50000.0;
        public double StartingReputation = 60.0; // 0..100, slow-moving

        // ---- Demand -> revenue ----
        public double GreenFee = 55.0;
        public double BaseRoundsCapacity = 220.0;  // max rounds/day a full, perfect course in peak season draws
        public double DemandConditionExponent = 1.6; // poor condition sheds golfers fast (convex)
        public double ReputationRate = 0.04;        // EMA speed of reputation toward condition

        // Weather playability multipliers on demand.
        public double WeatherFrostFactor = 0.10;
        public double WeatherStormFactor = 0.10;
        public double WeatherHeavyRainFactor = 0.5;
        public double WeatherHeatFactor = 0.8;
        public double HeavyRainMm = 8.0;

        // ---- Costs ----
        public double DailyOverhead = 750.0;   // crew wages + fixed daily costs
        public double SprayCost = 60.0;        // per zone sprayed (fungicide)
        public double FertCostPerN = 4.0;      // per unit of nitrogen applied
        public double WaterCostPerMm = 1.5;    // per mm irrigation per zone
        public double MowCost = 5.0;           // per zone mown
        public double RollCost = 4.0;          // per zone rolled
        public double AerateCost = 40.0;       // per zone aerated

        // ---- Condition weights (per green, sum to 1.0) ----
        public double WDensity = 0.35;
        public double WHealth = 0.30;   // 1 - infection
        public double WDebt = 0.15;     // 1 - turf debt
        public double WSpeed = 0.10;    // Stimp near target band
        public double WFirm = 0.10;
        public double StimpTarget = 11.0;
        public double StimpTolerance = 4.0;
    }
}
