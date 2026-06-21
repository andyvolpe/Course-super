// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Sim.Config
{
    /// <summary>
    /// Every coefficient the sim math uses, in one place (TDD §4 / §5 tuning table).
    /// All named, all tunable. The defaults are the MVP "cash-cow course" tuning.
    ///
    /// CONVENTIONS (TDD §4 header): state stored in natural units (%, inches, degF, Stimp-feet)
    /// is normalized to 0..1 inside the math; every accumulator is clamped each tick.
    /// </summary>
    public sealed class AgronomyTuning
    {
        public static readonly AgronomyTuning Default = new AgronomyTuning();

        // ---- Water balance (§4.1) -------------------------------------------------
        // soilMoisture is volumetric water content in % (0..SaturationPct).
        public double SaturationPct = 45.0;   // pore space full
        public double ResidualMoisturePct = 2.0; // air-dry floor; ET cannot pull below this

        // Field capacity by soil type (% VWC). Push-up holds much more water.
        public double FieldCapacityUsga = 18.0;
        public double FieldCapacityPushUp = 30.0;
        // Organic matter raises field capacity (holds water). Added per OM% over baseline.
        public double FcPerOmPct = 0.15;

        // Wilt point by soil type (% VWC).
        public double WiltPointUsga = 6.0;
        public double WiltPointPushUp = 12.0;

        // One-directional drainage: drains only the excess above FC, max(0, moisture - FC).
        public double DrainFractionUsga = 0.85;   // fast sand rootzone
        public double DrainFractionPushUp = 0.30; // slow native soil

        // Hargreaves reference ET: ET0 = HargreavesC * Ra * (Tmean + HargreavesOffset) * sqrt(max(0,Tmax-Tmin))
        // (mm/day). Ra is extraterrestrial radiation supplied by the weather day.
        public double HargreavesC = 0.0023;
        public double HargreavesOffset = 17.8;
        // One conversion for water depth -> volumetric water content over the managed rootzone:
        // rain, irrigation, AND ET all convert mm of water to VWC% through this single factor, so
        // inputs and losses are on the same physical scale (1 mm over a ~6" rootzone ≈ 0.5 VWC%).
        public double MmToVwcPct = 0.5;
        // Crop coefficient by zone (greens mown tight transpire less than lush rough).
        public double KcGreen = 1.0;
        public double KcTee = 1.0;
        public double KcFairway = 0.9;
        public double KcRough = 1.1;
        public double KcBunker = 0.2;

        // ---- Soil thermal + GDD (§4.7) -------------------------------------------
        public double GddBaseF = 50.0;        // cool-season base temperature
        public double SoilTempLag = 0.25;     // soilTemp moves this fraction toward air mean each day

        // ---- Growth / clip / carbohydrate reserves (§4.7) ------------------------
        public double GrowthPerGdd = 0.9;     // raw growth units per GDD at ideal conditions
        public double GrowthTempCenterF = 65.0; // cool-season optimum
        public double GrowthTempHalfWidthF = 28.0;
        public double NitrogenOptimum = 50.0; // N "units" considered fully sufficient
        public double ClipPerGrowth = 0.6;    // clip volume per unit growth at full density
        public double DensityGainPerGrowth = 0.35;
        public double DensityNaturalWear = 0.15; // baseline daily density loss (traffic/senescence)

        // Carbohydrate reserves (0..100): photosynthesis credits, growth + respiration debits.
        public double PhotosynthesisMax = 4.0;   // max daily reserve gain at ideal temp/light
        public double RespirationBase = 0.8;     // baseline daily reserve burn
        public double CarbCostPerGrowth = 0.5;   // reserves spent per unit growth
        public double HeatRespirationPerDegOverF = 0.06; // extra burn per degF of Tmean over optimum
        public double CarbStartPct = 60.0;

        // Nitrogen dynamics (0..100): uptake + leaching deplete; fertilize replenishes.
        public double NitrogenStart = 45.0;
        public double NitrogenUptakePerGrowth = 0.25;
        public double NitrogenLeachPerDrainage = 0.05; // N lost proportional to drainage volume

        // ---- Fertility PROGRAM (N is a fork, not a one-way slider) ----------------
        // Potassium (K) — a stress-tolerance pool, NOT a growth driver. Starts at the top of the N
        // band so in-band feeding is never K-deficient by default; the player must keep K up when
        // they push N (high N + low K = lush + fragile). No passive decline (K moves only via the program).
        public double PotassiumStart = 60.0;
        public double PotassiumOptimum = 60.0;
        // Iron (Fe) — colour ONLY. No growth, no disease, no debt. Fades over a few days.
        public double IronStart = 0.0;
        public double IronDecayPerDay = 4.0;

        // OVER-N consequences (Part A). All terms key off overN = max(0, N - grass.NOptMax), so they
        // are exactly ZERO for in-band turf — the existing model is untouched until you over-feed.
        public double OverNCarbDrainPerPt = 0.20;    // lush top growth SPENDS carbohydrate reserves
        public double OverNRootShrinkPerPt = 0.010;  // weak, shallow roots (heat-stress collapse waiting)
        public double OverNClipSurgePerPt = 0.05;    // surges clip volume (mow hours / scalp risk)
        public double OverNOmPerPt = 0.015;          // accelerates thatch / organic-matter accrual
        public double OverNStimpPenaltyPerPt = 0.04; // puffy, lush canopy fights green speed (Stimp)
        public double RootDepthBaselineIn = 6.0;     // roots recover slowly toward this when fed correctly
        public double RootRecoveryPerDay = 0.02;

        // Heat/wear FRAGILITY from N:K imbalance + over-N lushness. Zero when K keeps pace with N and
        // N is in-band (default K=60 >= any in-band N, so existing scenarios stay at zero).
        public double HeatStressThresholdF = 80.0;          // Tmean above this stresses cool-season turf
        public double FragilityHeatLossPerDegPerPt = 0.0020; // density/day per degF-over per fragility unit
        public double FragilityWearLossPerPt = 0.010;        // extra density loss on a mow day per fragility unit
        public double NKImbalanceWeight = 1.0;               // fragility from max(0, N - K)
        public double OverNFragilityWeight = 0.5;            // fragility from over-N lushness
        public double RootHeatLossPerDeg = 0.06;             // heat-stress density loss per degF-over per unit root deficit

        // OVER-N disease: brown patch (warm) + Pythium (hot, near-saturated). Driven by HIGH N — the
        // OPPOSITE end from dollar spot (which is a LOW-N risk). Folded into pressure via max(), so the
        // dollar-spot path is unchanged when overN = 0. High K RESISTS these two (only).
        public double BrownPatchTempCenterF = 85.0;
        public double BrownPatchTempHalfWidthF = 12.0;
        public double PythiumTempCenterF = 90.0;
        public double PythiumTempHalfWidthF = 10.0;
        public double HighNFavorabilityScale = 25.0;  // overN units mapping to full high-N favorability
        public double HighNDiseaseMax = 1.4;          // cap on the high-N favorability factor
        public double PythiumWetnessMin = 0.6;        // Pythium needs near-saturation to run
        public double KDiseaseResistanceMax = 0.5;    // high K cuts brown-patch/Pythium favorability up to this

        // Fertilizer APPLICATION model (source/release + foliar vs granular).
        public double FoliarUptakeFraction = 0.9;     // leaf absorbs most of a (small) foliar dose
        public double FoliarDoseSoftCap = 6.0;        // foliar N above this per app gives diminishing returns
        public double FoliarOverdoseAbsorb = 0.25;    // absorbed fraction of the dose ABOVE the soft cap
        public double FoliarOverdoseBurnPerPt = 0.04; // leaf burn per N pt dumped over the foliar soft cap
        public double FoliarKFraction = 0.85;         // foliar K uptake fraction
        public double GranularUptakeTempCenterF = 65.0;   // granular soil uptake is best in moderate soil temps
        public double GranularUptakeTempHalfWidthF = 22.0;
        public double GranularUptakeMoistureMinPct = 8.0;  // below this it's too dry to move nutrients
        public double GranularUptakeMoistureFullPct = 18.0;
        public double GranularUptakeRootFullIn = 6.0;      // shallow roots can't reach soil N
        public double GranularKFractionFull = 0.9;
        public double QuickReleaseBurnPerPt = 0.04;   // soluble shock-burn per available N pt (quick-release)
        public double SlowReleaseBurnMult = 0.20;     // slow/organic barely burn
        public double GranularDryBurnPerPt = 0.05;    // salt burn per unused N pt sitting on hot/dry soil

        // ---- Organic matter + grain (§4.7) ---------------------------------------
        public double OmFromGrowth = 0.02;     // OM accrual per growth unit (thatch)
        public double OmDecomposition = 0.01;  // daily microbial breakdown (absolute %)
        public double OmStartPct = 35.0;
        public double GrainFromGrowth = 0.04;
        public double GrainMowReduction = 1.5; // grain knocked down by a mow
        public double GrainStartPct = 10.0;

        // ---- Derived surfaces: firmness + Stimp (§4.4) ---------------------------
        public double FirmnessBase = 70.0;     // 0..100; drier + lower OM = firmer
        public double FirmnessMoistureWeight = 45.0;
        public double FirmnessOmWeight = 25.0;

        public double StimpBase = 9.0;         // feet
        public double StimpMin = 6.0;
        public double StimpMax = 15.0;
        public double StimpDensityBonus = 2.0; // dense, healthy turf rolls true and fast
        public double StimpMoisturePenalty = 2.5; // wet greens are slow
        public double StimpGrainPenalty = 1.5;
        public double StimpDebtPenalty = 2.0;  // a thinning/debt-laden green is bumpy and slow
        public double RollStimpBonus = 0.6;    // transient boost from a roll (decays)
        public double RollDecay = 0.5;

        // ---- Disease: dollar spot (§4.2 / §4.3) ----------------------------------
        // Favorability factors, each guarded max(0,...) and multiplied together.
        public double DiseaseTempCenterF = 72.0;
        public double DiseaseTempHalfWidthF = 18.0; // ~0 outside [54,90]F
        public double LeafWetnessOptimumHrs = 10.0; // hours of wetness for full favorability
        public double IrrigationWetnessHrsPerMm = 0.4; // irrigation adds leaf wetness
        public double SaturatedExcessWetnessHrs = 0.5; // each VWC% above FC adds wetness hours

        // Even well-fed turf carries baseline dollar-spot susceptibility when wet; starvation amplifies
        // it. Factor = clamp(DiseaseNitrogenBase + (1 - N/optimum), 0, DiseaseNitrogenMax).
        public double DiseaseNitrogenBase = 0.4;
        public double DiseaseNitrogenMax = 1.3;

        public double PressureGain = 6.0;     // pressure accrued per unit favorability per day
        public double PressureDecay = 0.10;   // natural daily decay fraction of pressure
        public double SpreadDiffusion = 0.18; // sub-cell diffusion toward neighbour mean

        // Infection only advances once a TELL exists (pressure over threshold) — keeps it legible.
        public double InfectionGain = 0.22;   // infection growth per (pressure-threshold) unit/day
        public double InfectionRecovery = 1.2; // daily healing when pressure is low
        public double InfectionDensityLoss = 2.0; // density lost per day at full infection (dollar-spot scars)

        // Spray: knocks down pressure/infection now and leaves residual protection.
        public double SprayPressureKnockdown = 0.85; // fraction of pressure removed on application
        public double SprayInfectionKnockdown = 0.6;
        public int SprayResidualDays = 14;
        public double SprayResidualFavorabilityMult = 0.1; // favorability scaled while residual active

        // ---- Fairness gate (§4.3 / GDD §3.1) -------------------------------------
        // Expression (a visible symptom step) may fire ONLY when one of these readable tells holds.
        public double TellPressureThreshold = 22.0; // pressure above this is a readable tell
        public double ThinDensityThreshold = 70.0;  // density below this is a readable "thin" tell
        public double ExpressionChanceMax = 0.9;    // cap on per-day expression probability
        public double ExpressionPressureScale = 80.0; // pressure that maps to ExpressionChanceMax
        public double ExpressionSeverityStep = 6.0;  // severity added when an expression fires

        // ---- Turf debt (§4.5) ----------------------------------------------------
        // Single accumulator (0..100). Offenses add; clean management slowly pays it down.
        public double DebtStartPct = 10.0;
        public double DebtRecoveryPerDay = 1.0;  // paid down when a day is offence-free
        public double DebtBleedThreshold = 45.0; // above this, debt bleeds density (visible thinning)
        public double DebtDensityBleed = 0.06;   // density lost per debt-point over threshold per day
        // Carb reserves SCALE the accrual rate: low reserves amplify every offence.
        public double DebtReserveAmplification = 0.8; // max extra multiplier at zero reserves

        // Offence magnitudes (debt points, before reserve amplification). Sized so a single bad habit
        // is a multi-week pressure, not instant death — debt is a slow burn that compounds.
        public double OffenseScalp = 1.0;        // mown below safe height
        public double OffenseWetTraffic = 0.8;   // mow/roll on saturated turf
        public double OffenseDrought = 1.5;      // moisture under wilt point
        public double OffenseOverwater = 0.8;    // moisture near saturation
        public double OffenseNStarvation = 1.0;  // nitrogen critically low
        public double OffenseSkippedAeration = 0.6; // high OM and overdue aeration
        public double OffenseDiseaseUntreated = 1.2; // active infection, no spray cover

        // Offence trigger thresholds.
        public double SafeMowHeightIn = 0.10;    // below this height = scalp
        public double WetTrafficMoisturePct = 32.0;
        public double OverwaterMoisturePct = 40.0;
        public double NStarvationPct = 12.0;
        public double HighOmPct = 45.0;
        public int AerationOverdueDays = 30;

        // Aeration relief.
        public double AerateOmRemoval = 6.0;
        public double AerateDebtRelief = 8.0;
        public double AerateDensityWear = 5.0;  // short-term thinning from coring
        public int AerateRecoveryDays = 10;

        // Mow / fertilize action effects.
        public double MowDensityWear = 0.5;
        public double FertilizerDefaultN = 12.0;

        // ---- Interrupts (Phase 4.4): when the skip must STOP and pull the player in ----
        public double HeatSpikeThresholdF = 91.0;     // a day this hot is a crisis to attend
        public double InterruptInfectionThreshold = 15.0; // a green crossing this is a fresh disease break
        public double InterruptClearFraction = 0.5;   // re-arm the disease interrupt once it falls back below this fraction

        // ---- Extreme weather events (Phase 5.3) ----
        public double StormRainThresholdMm = 18.0;    // a downpour: washes out bunkers, cleanup pressure
        public double FrostThresholdF = 32.0;         // a frost: mowing/rolling frozen turf damages it
        public double FrostMowDensityLoss = 6.0;      // bruised crowns / shattered blades from mowing on frost
        public double FrostMowDebt = 8.0;             // and it's a real offence against the turf
        public int FlashDroughtDays = 6;              // consecutive hot, rainless days = a flash drought
        public double FlashDroughtMaxRainMm = 1.0;
        public double FlashDroughtMinTmaxF = 88.0;

        // ---- Forecast (Phase 5.2): a fallible view of the future that tightens toward the day ----
        public int ForecastHorizonDays = 5;
        public double ForecastTempBandPerDayF = 1.2;       // day+5 ~ +/-6F, day+1 ~ +/-1.2F
        public double ForecastRainBandPerDayMm = 2.4;
        public double ForecastLeafWetBandPerDayHr = 1.0;
        public double ForecastHumidityBandPerDay = 0.05;

        // ---- Ball physics (TDD §7): the ball reads the green's maintained state, no new tuning system ----
        // Putt roll: a full-power putt rolls (Stimp * PuttRollFeetPerStimp) feet — so green SPEED (Stimp,
        // which you set via mowing/rolling/moisture) directly sets roll distance.
        public double PuttRollFeetPerStimp = 4.0;
        public double PuttSlopeRollFactor = 0.8;  // along-aim slope lengthens (downhill) / shortens (uphill) the roll
        public double PuttBreakFactor = 0.9;      // cross-slope curve per unit slope per foot of roll
        public double PuttAvgSpeedFps = 5.0;      // for roll-time (animation) only

        // Approach: firmer greens (low moisture/OM -> high firmness) release more; soft greens check up.
        public double ApproachCarryFeetFull = 60.0;
        public double ApproachReleaseBaseFt = 2.0;        // release even on a soft green
        public double ApproachReleaseFirmFactorFt = 18.0; // extra release at full firmness
        public double ApproachBounceMaxFt = 1.2;          // first-bounce height at full firmness

        // Off-green penalties (hooks for later): rough thins distance, bunkers kill it.
        public double RoughDistancePenalty = 0.5;
        public double BunkerDistancePenalty = 0.85;

        public double FieldCapacity(SoilType soil, double organicMatterPct)
        {
            double baseFc = soil == SoilType.UsgaSpec ? FieldCapacityUsga : FieldCapacityPushUp;
            return baseFc + FcPerOmPct * (organicMatterPct - OmStartPct);
        }

        public double WiltPoint(SoilType soil) => soil == SoilType.UsgaSpec ? WiltPointUsga : WiltPointPushUp;

        public double DrainFraction(SoilType soil) => soil == SoilType.UsgaSpec ? DrainFractionUsga : DrainFractionPushUp;

        public double CropCoefficient(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Green: return KcGreen;
                case ZoneType.Tee: return KcTee;
                case ZoneType.Fairway: return KcFairway;
                case ZoneType.Rough: return KcRough;
                default: return KcBunker;
            }
        }
    }
}
