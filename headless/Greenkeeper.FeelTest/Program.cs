using System;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Sim.Tournament;

// Phase 3.0 feel-test: drive green-01 into the ground (kept wet, N-starved, never sprayed, mown low)
// starting in summer, and print the dollar-spot curve day by day. This is the headless equivalent of
// the in-editor neglect test — the decision gate before building the shader.

class Program
{
    static void Main()
    {
        NeglectTest();
        Console.WriteLine();
        TriageSqueeze();
        Console.WriteLine();
        InterlockDemo();
        Console.WriteLine();
        ForecastGamble();
        Console.WriteLine();
        EconomyConsequence();
        Console.WriteLine();
        TournamentRun();
    }

    // Phase 7 gate: can you steer the greens to the agronomist's spec under the clock, and does nailing
    // it pay off? You can't snap Stimp up — it climbs over days of tight mowing/rolling/drying.
    static void TournamentRun()
    {
        Console.WriteLine("TOURNAMENT — steer to the setup spec under the clock, then get graded\n");

        var spec = new TournamentSpec {
            Name = "Club Championship", DayIndex = 120,
            StimpMin = 11.0, StimpMax = 12.5, FirmMin = 60, FirmMax = 85,
            MaxInfection = 5, MinDensity = 82, ConsistencyToleranceStimp = 0.5,
            PassScore = 70, PrizeMoney = 40000, ReputationGain = 12,
        };

        var cfg = CourseConfig.GreensOnly();
        var course = CourseFactory.Build(cfg, 202);
        var dir = new GameDirector(course, 202, cfg.Tuning, cfg.Grass)
        {
            Economy = new EconomyState(EconomyConfig.Default),
            Tournament = new TournamentLadder(new System.Collections.Generic.List<TournamentSpec> { spec }),
        };
        dir.Clock.JumpTo(105); // ~15 days to prep
        var g = course.Get("green-01");

        Console.WriteLine($"  Spec: Stimp {spec.StimpMin}-{spec.StimpMax}, firm {spec.FirmMin}-{spec.FirmMax}, day {spec.DayIndex}.");
        Console.WriteLine("  Tournament-prep routine (tight mow + roll + dry down):");
        TournamentResult fired = null;
        while (dir.Clock.DayIndex <= spec.DayIndex)
        {
            int day = dir.Clock.DayIndex;
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None;
                a.Mow = true; a.MowHeightIn = 0.100;                                  // tight cut -> speed
                a.Roll = true;                                                         // roll -> speed/firm
                a.IrrigationMm = Math.Max(0, (11.0 - z.SoilMoisturePct) / 0.9);        // dry -> firm/fast
                a.FertilizerN = z.NitrogenPct < 30 ? 8 : 0;
                a.Spray = (z.MaxInfection > 0 || z.MeanPressure > AgronomyTuning.Default.TellPressureThreshold);
                plan.Set(z.Id, a);
            }
            var res = dir.ResolveDay(plan);
            if (res.Tournament != null) fired = res.Tournament;
            if (day % 3 == 0 || res.Tournament != null)
                Console.WriteLine($"    day {day}: green-01 Stimp {g.Stimp:F1}  firm {g.FirmnessPct:F0}  density {g.DensityPct:F0}");
        }

        Console.WriteLine($"\n  RESULT: {fired}  (mean Stimp {fired.MeanStimp:F1} ±{fired.StimpStdev:F2}, firm {fired.MeanFirmness:F0})");
        Console.WriteLine($"    prize ${fired.PrizeAwarded:N0}, reputation {fired.ReputationDelta:+0;-0}, cash now ${dir.Economy.Cash:N0}");
        Console.WriteLine("  -> the payoff is the green you built reaching a number the agronomist set.");
    }

    // Phase 6 gate: does mismanagement HURT the books? Show cash + condition over a summer, well-run
    // vs neglected, so the consequence is money, not just a brown green.
    static void EconomyConsequence()
    {
        Console.WriteLine("ECONOMY — full year from a SPRING start (the real player path)\n");
        Console.WriteLine("  day  season | GOOD cond  cash        | NEGLECT cond  cash");
        Console.WriteLine("  -----+------+------------------------+---------------------");

        var (gDir, gCourse) = NewEcoRun();
        var (nDir, nCourse) = NewEcoRun();
        for (int d = 0; d < 360; d++)
        {
            gDir.ResolveDay(EcoGoodPlan(gCourse));
            nDir.ResolveDay(new DayPlan()); // neglect: keep the lights on, do nothing
            if (d == 14 || d % 30 == 29)
            {
                var gl = gDir.Economy.Latest; var nl = nDir.Economy.Latest;
                Console.WriteLine($"  {d + 1,4} {gDir.Clock.Season,-6} | {gl.ConditionIndex,6:F0} ${gl.Cash,11:N0} | {nl.ConditionIndex,6:F0} ${nl.Cash,11:N0}");
            }
        }
        double good = gDir.Economy.Cash, bad = nDir.Economy.Cash;
        Console.WriteLine($"\n  End of YEAR: well-run ${good:N0}  vs  neglected ${bad:N0}.");
        Console.WriteLine($"  -> smart play should survive the spring ramp and profit on the year; neglect should sink.");
    }

    static (GameDirector, CourseState) NewEcoRun()
    {
        var cfg = CourseConfig.Mvp();
        var course = CourseFactory.Build(cfg, 909);
        var dir = new GameDirector(course, 909, cfg.Tuning, cfg.Grass)
        {
            Economy = new EconomyState(EconomyConfig.Default),
            EconomyConfig = EconomyConfig.Default,
        };
        // Spring start (day 0) — what the player actually faces.
        return (dir, course);
    }

    static DayPlan EcoGoodPlan(CourseState course)
    {
        var plan = new DayPlan();
        foreach (var z in course.Greens)
        {
            var a = ZoneAction.None;
            a.Mow = true; a.MowHeightIn = 0.125;
            a.IrrigationMm = Math.Max(0, (16.0 - z.SoilMoisturePct) / 0.9);
            a.FertilizerN = z.NitrogenPct < 35 ? 12 : 0;
            a.Spray = (z.MaxInfection > 0 || z.MeanPressure > AgronomyTuning.Default.TellPressureThreshold);
            plan.Set(z.Id, a);
        }
        return plan;
    }

    // Phase 5 gate: does the forecast gamble bite? Show the forecast tightening toward a day (and
    // missing), then the +3-day heat-spike skill — i.e. how often pre-watering on the forecast pays off.
    static void ForecastGamble()
    {
        const int seed = 73;
        var t = AgronomyTuning.Default;
        var weather = new WeatherSystem(seed);
        var forecast = new Forecast(seed, t);

        Console.WriteLine("FORECAST GAMBLE — the forecast tightens toward the day, and sometimes misses\n");

        // Find a hot summer day and show its forecast from 5 / 3 / 1 days out vs reality.
        int target = -1;
        for (int d = 100; d < 200; d++) if (weather.Generate(d).TmaxF > 88) { target = d; break; }
        if (target > 6)
        {
            double actual = weather.Generate(target).TmaxF;
            Console.WriteLine($"  Day {target} actual high = {actual:F1}F. Forecast as it approaches:");
            foreach (int from in new[] { target - 5, target - 3, target - 1 })
            {
                var fd = forecast.Predict(from, target);
                Console.WriteLine($"    from day {from} (+{fd.DaysOut}d): {fd.Predicted.TmaxF:F1}F ± {fd.TempBandF:F1}  " +
                                  $"(off by {Math.Abs(fd.Predicted.TmaxF - actual):F1}F){(fd.PredictedHeatSpike ? "  [calls HEAT]" : "")}");
            }
        }

        // +3-day heat-spike skill over a couple of summers: if you pre-water every time the +3 forecast
        // calls a heat spike, how often is it real (payoff) vs a wasted call (false alarm)?
        int hits = 0, falseAlarms = 0, missed = 0, realSpikes = 0;
        for (int day = 90; day < 90 + 3 * 360; day++)
        {
            int tgt = day + 3;
            bool forecastHeat = forecast.Predict(day, tgt).PredictedHeatSpike;
            bool actualHeat = WeatherEvents.IsHeatSpike(weather.Generate(tgt), t);
            if (actualHeat) realSpikes++;
            if (forecastHeat && actualHeat) hits++;
            else if (forecastHeat && !actualHeat) falseAlarms++;
            else if (!forecastHeat && actualHeat) missed++;
        }
        int calls = hits + falseAlarms;
        Console.WriteLine($"\n  +3-day heat-spike forecast over 3 summers: {realSpikes} real spikes.");
        Console.WriteLine($"    pre-water-on-call: {calls} calls -> {hits} paid off, {falseAlarms} wasted " +
                          $"({(calls > 0 ? (double)hits / calls : 0):P0} hit rate)");
        Console.WriteLine($"    caught flat (spike you weren't warned of at +3): {missed}");
        Console.WriteLine($"  -> acting on the forecast helps but never guarantees — that gap is the gamble.");
    }

    // Step A+.3 (headless equivalent of the by-hand gate): two identical greens taken to MEMBER vs
    // TOURNAMENT setup THROUGH MAINTENANCE, then putted with identical power. If they roll differently,
    // the difference is something you dialed in — not a slider.
    static void InterlockDemo()
    {
        Console.WriteLine("INTERLOCK — same green, two setups you create by maintenance, identical putt\n");

        var member = PrepGreen(member: true);
        var tournament = PrepGreen(member: false);
        var t = AgronomyTuning.Default;

        var putt = new Greenkeeper.Sim.Physics.PuttInput(0.8, new Greenkeeper.Sim.Math.Vec2(1, 0), Greenkeeper.Sim.Math.Vec2.Zero);
        double memRoll = Greenkeeper.Sim.Physics.BallPhysics.SolvePutt(member, putt, t).RollDistanceFt;
        double tourRoll = Greenkeeper.Sim.Physics.BallPhysics.SolvePutt(tournament, putt, t).RollDistanceFt;

        // An identical approach to each, to show firmness release.
        var appr = new Greenkeeper.Sim.Physics.ApproachInput(0.7, new Greenkeeper.Sim.Math.Vec2(1, 0));
        double memRel = Greenkeeper.Sim.Physics.BallPhysics.SolveApproach(member, appr, t).ReleaseRollFt;
        double tourRel = Greenkeeper.Sim.Physics.BallPhysics.SolveApproach(tournament, appr, t).ReleaseRollFt;

        Console.WriteLine($"  MEMBER setup     (softer, less roll): Stimp {member.Stimp:F1}  firmness {member.FirmnessPct:F0}  " +
                          $"moisture {member.SoilMoisturePct:F1}");
        Console.WriteLine($"  TOURNAMENT setup (firm, double-cut+roll): Stimp {tournament.Stimp:F1}  firmness {tournament.FirmnessPct:F0}  " +
                          $"moisture {tournament.SoilMoisturePct:F1}");
        Console.WriteLine();
        Console.WriteLine($"  Same 0.8-power putt:    member rolls {memRoll:F1} ft   |   tournament rolls {tourRoll:F1} ft   " +
                          $"(+{tourRoll - memRoll:F1} ft, {(tourRoll / memRoll - 1) * 100:F0}% farther)");
        Console.WriteLine($"  Same 0.7 approach:      member releases {memRel:F1} ft  |   tournament releases {tourRel:F1} ft");
        Console.WriteLine($"\n  -> They play like two different greens, and the difference is the setup you dialed in.");
    }

    static ZoneState PrepGreen(bool member)
    {
        const int seed = 314;
        var cfg = CourseConfig.GreensOnly(1);
        var course = CourseFactory.Build(cfg, seed);
        var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
        dir.Clock.JumpTo(120); // mid-summer, settled growth
        var g = course.Get("green-01");

        for (int d = 0; d < 18; d++)
        {
            var plan = new DayPlan();
            var a = ZoneAction.None;
            if (member)
            {
                a.Mow = true; a.MowHeightIn = 0.150;                 // higher cut -> slower
                a.IrrigationMm = Math.Max(0, (24.0 - g.SoilMoisturePct) / 0.9); // keep it lush/soft
                a.Roll = false;
            }
            else
            {
                a.Mow = true; a.MowHeightIn = 0.100;                 // tight cut -> fast
                a.IrrigationMm = Math.Max(0, (12.0 - g.SoilMoisturePct) / 0.9); // dry it down -> firm
                a.Roll = true;                                        // roll for speed/smoothness
            }
            a.FertilizerN = g.NitrogenPct < 35 ? 10 : 0;
            plan.Set(g.Id, a);
            dir.ResolveDay(plan);
        }
        return g;
    }

    static void NeglectTest()
    {
        const int seed = 4242;
        const int summerStart = 90;
        const int days = 55;

        var cfg = CourseConfig.GreensOnly(1);
        var t = cfg.Tuning;
        var course = CourseFactory.Build(cfg, seed);
        var director = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
        director.Clock.JumpTo(summerStart);
        var g = course.Get("green-01");

        var neglect = new ZoneAction { Mow = true, MowHeightIn = 0.08, IrrigationMm = 20.0, FertilizerN = 0.0, Spray = false };

        Console.WriteLine("NEGLECT TEST — green-01, kept wet + N-starved + never sprayed + mown low, from summer\n");
        Console.WriteLine(" day | moist  N   dens  carb | pressP infectI exprS debt | tell  events");
        Console.WriteLine(" ----+-----------------------+------------------------+-------------");

        int firstTell = -1, firstInfect = -1, firstExpr = -1, firstThin = -1;
        double prevDensity = g.DensityPct;

        for (int d = 0; d < days; d++)
        {
            var plan = new DayPlan(); plan.Set(g.Id, neglect);
            var result = director.ResolveDay(plan);

            bool tell = result.TellByZone.TryGetValue(g.Id, out var tv) && tv;
            bool exprFired = result.Expressions.Count > 0;

            if (firstTell < 0 && tell) firstTell = d;
            if (firstInfect < 0 && g.MaxInfection > 0.01) firstInfect = d;
            if (firstExpr < 0 && g.MaxExpression > 0.01) firstExpr = d;
            if (firstThin < 0 && g.DensityPct < prevDensity - 0.05 && g.TurfDebtPct > t.DebtBleedThreshold) firstThin = d;
            prevDensity = g.DensityPct;

            string ev = "";
            if (d == firstTell) ev += "TELL ";
            if (d == firstInfect) ev += "INFECT ";
            if (d == firstExpr) ev += "EXPRESS ";
            if (d == firstThin) ev += "THINNING ";

            Console.WriteLine($" {d,3} | {g.SoilMoisturePct,4:F1} {g.NitrogenPct,4:F1} {g.DensityPct,5:F1} {g.CarbReservesPct,5:F1} |" +
                              $" {Bar(g.MeanPressure)} {g.MeanPressure,5:F1} {g.MaxInfection,5:F1} {g.MaxExpression,5:F1} {g.TurfDebtPct,5:F1} |" +
                              $" {(tell ? "yes" : "  .")}  {ev}");
        }

        Console.WriteLine($"\nMilestones: first tell day {firstTell}, first infection day {firstInfect}, " +
                          $"first expression day {firstExpr}, first thinning day {firstThin}");
        Console.WriteLine($"Final: density {g.DensityPct:F1}%  infection {g.MaxInfection:F1}  debt {g.TurfDebtPct:F1}");

        // Contrast: a well-kept green over the same window.
        var cfg2 = CourseConfig.GreensOnly(1);
        var course2 = CourseFactory.Build(cfg2, seed);
        var dir2 = new GameDirector(course2, seed, cfg2.Tuning, cfg2.Grass);
        dir2.Clock.JumpTo(summerStart);
        var g2 = course2.Get("green-01");
        for (int d = 0; d < days; d++)
        {
            var plan = new DayPlan();
            plan.Set(g2.Id, new ZoneAction { Mow = true, MowHeightIn = 0.125,
                IrrigationMm = Math.Max(0, (16.0 - g2.SoilMoisturePct) / 0.9),
                FertilizerN = g2.NitrogenPct < 35 ? 12 : 0, Spray = (d % 14 == 0) });
            dir2.ResolveDay(plan);
        }
        Console.WriteLine($"\nWell-kept contrast (same seed/window): density {g2.DensityPct:F1}%  " +
                          $"infection {g2.MaxInfection:F1}  pressure {g2.MeanPressure:F1}  debt {g2.TurfDebtPct:F1}");
    }

    // Phase 4.3 gate: does the morning triage have teeth? Disease brews on #7 while a realistic full
    // to-do list far exceeds the 30-hour budget — so attending #7 means cutting presentation work.
    static void TriageSqueeze()
    {
        const int seed = 909;
        var cfg = CourseConfig.Mvp();
        var t = cfg.Tuning;
        var course = CourseFactory.Build(cfg, seed);
        var dir = new GameDirector(course, seed, cfg.Tuning, cfg.Grass);
        dir.Clock.JumpTo(90); // summer

        Console.WriteLine("TRIAGE SQUEEZE — 18-hole course, default crew = 30 crew-hours/window\n");

        // The "ideal full day" a perfectionist would order, costed from the catalog.
        double mowGreens = 18 * TaskCatalog.BaseHours(TaskType.WalkMowGreens);
        double rollGreens = 18 * TaskCatalog.BaseHours(TaskType.RollGreens);
        double sprayGreens = 18 * TaskCatalog.BaseHours(TaskType.Spray);
        double waterGreens = 18 * TaskCatalog.BaseHours(TaskType.Water);
        double fertGreens = 18 * TaskCatalog.BaseHours(TaskType.Fertilize);
        double mowFairways = 18 * TaskCatalog.BaseHours(TaskType.MowFairways);
        double rakeBunkers = TaskCatalog.BaseHours(TaskType.RakeBunkers);
        double changeCups = TaskCatalog.BaseHours(TaskType.ChangeCups);
        double full = mowGreens + rollGreens + sprayGreens + waterGreens + fertGreens + mowFairways + rakeBunkers + changeCups;
        var budget = new MaintenanceWindow(CrewMember.DefaultCrew()).BudgetHours;

        Console.WriteLine($"  Full perfectionist program ~ {full:F1}h:");
        Console.WriteLine($"    walk-mow greens {mowGreens:F1} | roll {rollGreens:F1} | spray {sprayGreens:F1} | water {waterGreens:F1}");
        Console.WriteLine($"    fertilize {fertGreens:F1} | mow fairways {mowFairways:F1} | rake bunkers {rakeBunkers:F1} | change cups {changeCups:F1}");
        Console.WriteLine($"  Budget {budget:F0}h  ->  deficit {full - budget:F1}h. You can do about {budget / full:P0} of it.\n");

        // Brew disease on #7: keep it wet + unsprayed; keep the rest drier and mown.
        var seven = course.Get("green-07");
        for (int d = 0; d < 12; d++)
        {
            var plan = new DayPlan();
            foreach (var z in course.Greens)
            {
                var a = ZoneAction.None; a.Mow = true; a.MowHeightIn = 0.125;
                if (z.Id == "green-07") a.IrrigationMm = 22.0;                 // wet, no spray -> brews
                else { a.IrrigationMm = Math.Max(0, (14.0 - z.SoilMoisturePct) / 0.9); a.Spray = (dir.Clock.DayIndex % 14 == 0); }
                plan.Set(z.Id, a);
            }
            dir.ResolveDay(plan);
        }

        bool sevenTell = seven.MeanPressure > t.TellPressureThreshold || seven.MaxInfection > 0 || seven.MaxExpression > 0;
        Console.WriteLine($"  After 12 days: #7 pressure {seven.MeanPressure:F1}, infection {seven.MaxInfection:F1}, " +
                          $"expression {seven.MaxExpression:F1}  -> readable tell: {(sevenTell ? "YES, #7 needs you" : "no")}");

        // Now the choice, in real hours:
        double presentation = mowGreens + rakeBunkers + mowFairways; // what keeps the course looking right
        Console.WriteLine($"\n  The squeeze on this morning (budget {budget:F0}h):");
        Console.WriteLine($"    'Keep it presentable' = mow greens + rake bunkers + mow fairways = {presentation:F1}h" +
                          $"  -> leaves {budget - presentation:F1}h.");
        double attendSeven = TaskCatalog.BaseHours(TaskType.Spray) + TaskCatalog.BaseHours(TaskType.WalkMowGreens);
        Console.WriteLine($"    'Attend #7' = scout + spray + mow #7 ~ {attendSeven + 0.3:F1}h of focused attention.");
        Console.WriteLine($"    You can't do the full presentation AND properly work the rest — something gets cut.");
        Console.WriteLine($"    e.g. spraying ALL greens ({sprayGreens:F1}h) on top of presentation ({presentation:F1}h) = " +
                          $"{presentation + sprayGreens:F1}h >> {budget:F0}h.");
    }

    static string Bar(double v)
    {
        int n = (int)Math.Round(Math.Clamp(v, 0, 100) / 5.0);
        return new string('#', n).PadRight(20, '.');
    }
}
