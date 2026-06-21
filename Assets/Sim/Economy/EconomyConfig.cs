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
        // Condition -> demand is gated by a FLOOR (nobody plays a course below it) and a steep curve
        // above it, so poor condition visibly empties the tee sheet within weeks (GDD §8).
        public double DemandFloorCondition = 25.0; // below this, demand collapses to ~0
        public double DemandConditionExponent = 2.2; // steep convex falloff above the floor
        public double ReputationRate = 0.04;        // EMA speed of reputation toward condition

        // Weather playability multipliers on demand.
        public double WeatherFrostFactor = 0.10;
        public double WeatherStormFactor = 0.10;
        public double WeatherHeavyRainFactor = 0.5;
        public double WeatherHeatFactor = 0.8;
        public double HeavyRainMm = 8.0;

        // ---- FIXED costs (TDD §4.6): these drain EVERY day regardless of activity. Doing nothing does
        //      not stop the bills — a do-nothing quarter must trend toward loss. All tunable. ----
        public double CrewWagesPerDay = 1500.0;     // the maintenance crew is on payroll whether or not you assign them
        public double DebtServicePerDay = 1200.0;   // mortgage / lease on the property
        public double AdminClubhousePerDay = 900.0; // clubhouse + admin staff
        public double UtilitiesPerDay = 600.0;      // water + power base load
        public double EquipmentLeasePerDay = 500.0; // mowers/sprayers depreciation + lease
        public double PropertyTaxPerDay = 250.0;
        public double InsurancePerDay = 200.0;

        /// <summary>Total fixed cost that accrues every day no matter what (≈ $5,150/day at defaults).</summary>
        public double FixedDailyCost()
            => CrewWagesPerDay + DebtServicePerDay + AdminClubhousePerDay + UtilitiesPerDay
             + EquipmentLeasePerDay + PropertyTaxPerDay + InsurancePerDay;

        // ---- VARIABLE costs (materials/inputs) — only when you actually do the work ----
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
