using System;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

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
