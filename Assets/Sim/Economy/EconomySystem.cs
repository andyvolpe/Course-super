// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Sim.Economy
{
    /// <summary>
    /// Settles one day's books (deterministic — no RNG). Condition -> demand -> revenue; the day's
    /// maintenance plan -> costs. This is what makes mismanagement HURT: let the course slide and
    /// rounds (and reputation, and cash) follow it down, while costs keep coming.
    /// </summary>
    public static class EconomySystem
    {
        public static DayLedger Settle(EconomyState eco, CourseState course, WeatherDay w, DayPlan plan,
                                       int dayIndex, Season season, EconomyConfig cfg, AgronomyTuning agro)
        {
            cfg = cfg ?? EconomyConfig.Default;
            agro = agro ?? AgronomyTuning.Default;

            double condition = ConditionSystem.CourseCondition(course, cfg);

            // Reputation is a slow EMA of condition — a course earns (and loses) its standing over time.
            eco.Reputation = Mathx.Clamp(eco.Reputation + cfg.ReputationRate * (condition - eco.Reputation), 0, 100);

            double weatherFactor = WeatherDemandFactor(w, cfg, agro);
            double seasonFactor = SeasonDemandFactor(season);
            // Demand collapses below the floor and falls steeply above it: (cond-floor)/(100-floor) ^ exp.
            double above = Mathx.Clamp01((condition - cfg.DemandFloorCondition)
                                         / System.Math.Max(1.0, 100.0 - cfg.DemandFloorCondition));
            double conditionFactor = System.Math.Pow(above, cfg.DemandConditionExponent);
            double repFactor = Mathx.Clamp01(eco.Reputation / 100.0);

            double rounds = cfg.BaseRoundsCapacity * conditionFactor * repFactor * weatherFactor * seasonFactor;
            double revenue = rounds * cfg.GreenFee;
            // Fixed costs accrue regardless of activity; materials only when you actually work.
            double costs = cfg.FixedDailyCost() + MaterialCost(course, plan, cfg);
            double net = revenue - costs;
            eco.Cash += net;

            var ledger = new DayLedger
            {
                DayIndex = dayIndex,
                ConditionIndex = condition,
                Reputation = eco.Reputation,
                Rounds = rounds,
                Revenue = revenue,
                Costs = costs,
                Net = net,
                Cash = eco.Cash,
            };
            eco.History.Add(ledger);
            return ledger;
        }

        public static double MaterialCost(CourseState course, DayPlan plan, EconomyConfig cfg)
        {
            double cost = 0;
            foreach (var z in course.Zones)
            {
                var a = plan.For(z.Id);
                if (a.Mow) cost += cfg.MowCost;
                if (a.Roll) cost += cfg.RollCost;
                if (a.Spray) cost += cfg.SprayCost;
                if (a.Aerate) cost += cfg.AerateCost;
                if (a.FertilizerN > 0) cost += a.FertilizerN * cfg.FertCostPerN;
                if (a.IrrigationMm > 0) cost += a.IrrigationMm * cfg.WaterCostPerMm;
            }
            return cost;
        }

        private static double WeatherDemandFactor(WeatherDay w, EconomyConfig cfg, AgronomyTuning agro)
        {
            if (WeatherEvents.IsFrost(w, agro)) return cfg.WeatherFrostFactor;
            if (WeatherEvents.IsStorm(w, agro)) return cfg.WeatherStormFactor;
            if (w.RainMm > cfg.HeavyRainMm) return cfg.WeatherHeavyRainFactor;
            if (WeatherEvents.IsHeatSpike(w, agro)) return cfg.WeatherHeatFactor;
            return 1.0;
        }

        private static double SeasonDemandFactor(Season season)
        {
            switch (season)
            {
                case Season.Summer: return 1.0;
                case Season.Spring: return 0.85;
                case Season.Fall: return 0.85;
                default: return 0.40; // winter — quiet, but the bills still come
            }
        }
    }
}
