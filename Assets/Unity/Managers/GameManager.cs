using System.Collections.Generic;
using UnityEngine;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.Economy;
using Greenkeeper.Sim.Legibility;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;
using Greenkeeper.Sim.Tournament;
using Greenkeeper.Unity.Config;
using Greenkeeper.Unity.Save;

namespace Greenkeeper.Unity.Managers
{
    /// <summary>
    /// The Unity-side owner of the headless sim (TDD §9). Builds a CourseState from an authored
    /// CourseConfigSO, owns the GameDirector, and exposes the morning-plan → resolve → skip loop to
    /// the UI. All simulation lives in Greenkeeper.Sim; this is just the bridge.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Authoring")]
        public CourseConfigSO courseConfig;
        public int weatherSeed = 12345;

        [Header("Difficulty (surfacing only — never the sim)")]
        public bool assistsEnabled;

        public GameDirector Director { get; private set; }
        public CourseState Course => Director?.Course;

        /// <summary>The single legibility gate (GDD §3). The Unity layer renders only what this permits.</summary>
        public LegibilitySystem Legibility { get; private set; }

        /// <summary>The crew and the current morning maintenance window (GDD §4).</summary>
        public List<CrewMember> Crew { get; private set; }
        public MaintenanceWindow Window { get; private set; }

        /// <summary>The fallible weather forecast the player plans against (Phase 5).</summary>
        public Forecast Forecast { get; private set; }

        /// <summary>Course finances (Phase 6): condition -> demand -> revenue, minus costs.</summary>
        public EconomyState Economy => Director?.Economy;

        /// <summary>The tournament ladder to climb (Phase 7).</summary>
        public TournamentLadder Tournament => Director?.Tournament;

        /// <summary>Recent per-step log lines for the debug UI.</summary>
        public readonly List<string> RecentLog = new List<string>();
        public const int MaxLogLines = 24;

        // ---- Reading delegation (information depth dial, GDD §3.2 + §4.8) ----
        /// <summary>When on, the assigned tech runs the routine readings on a coast (week/month) so data
        /// returns to the clipboard without you walking each green. Carries the delegation-quality gap:
        /// competent NUMBERS, not the expert read, and coverage scales with skill (never your own eyes).</summary>
        public bool DelegateReadings;
        public string ReadingTechId;
        /// <summary>Per-green confidence of the LATEST read when it was tech-reported; absent => you read it.</summary>
        public readonly Dictionary<string, double> TechReadConfidence = new Dictionary<string, double>();
        /// <summary>Earned green-speed / firmness MEASUREMENTS (stimpmeter / firmness meter): (day, value).</summary>
        public readonly Dictionary<string, KeyValuePair<int, double>> StimpReads = new Dictionary<string, KeyValuePair<int, double>>();
        public readonly Dictionary<string, KeyValuePair<int, double>> FirmReads = new Dictionary<string, KeyValuePair<int, double>>();
        public const int MeasurementFreshDays = 1; // a measurement is "this morning's" for one day

        public void RecordStimp(string id, double v) { StimpReads[id] = new KeyValuePair<int, double>(Director.Clock.DayIndex, v); }
        public void RecordFirm(string id, double v) { FirmReads[id] = new KeyValuePair<int, double>(Director.Clock.DayIndex, v); }
        /// <summary>A green-speed measurement taken today (else NaN). Earned by rolling the stimpmeter.</summary>
        public double FreshStimp(string id) => Fresh(StimpReads, id);
        public double FreshFirm(string id) => Fresh(FirmReads, id);
        private double Fresh(Dictionary<string, KeyValuePair<int, double>> d, string id)
            => d.TryGetValue(id, out var kv) && Director.Clock.DayIndex - kv.Key < MeasurementFreshDays ? kv.Value : double.NaN;
        /// <summary>Mark that YOU read this green by hand (clears the tech-reported flag — you got the expert read).</summary>
        public void MarkPlayerRead(string id) => TechReadConfidence.Remove(id);
        public CrewMember ReadingTech => Crew?.Find(c => c.Id == ReadingTechId) ?? (Crew != null && Crew.Count > 1 ? Crew[1] : null);

        private void Awake() => NewGame();

        public void NewGame()
        {
            CourseConfig cfg = courseConfig != null ? courseConfig.ToConfig() : CourseConfig.Mvp();
            var course = CourseFactory.Build(cfg, weatherSeed);
            var econCfg = EconomyConfig.Default;
            Director = new GameDirector(course, weatherSeed, cfg.Tuning, cfg.Grass)
            {
                Economy = new EconomyState(econCfg),
                EconomyConfig = econCfg,
                Tournament = TournamentLadder.Mvp(),
            };
            Legibility = new LegibilitySystem(cfg.Tuning,
                assistsEnabled ? DifficultySettings.WithAssists() : DifficultySettings.Full());
            Crew = CrewMember.DefaultCrew();
            Forecast = new Forecast(weatherSeed, cfg.Tuning);
            BeginWindow();
            RecentLog.Clear();
            Log($"New game. {course.Zones.Count} zones, seed {weatherSeed}. Assists {(assistsEnabled ? "ON" : "OFF")}.");
        }

        /// <summary>Open a fresh morning window for the current day (clears the queue, full hour budget).</summary>
        public void BeginWindow() => Window = new MaintenanceWindow(Crew);

        /// <summary>
        /// Resolve today's window: accepted tasks become the plan, delegated tasks apply at staff
        /// quality (the depth dial), then the day advances and a fresh window opens. Returns the result.
        /// </summary>
        public DayResult ResolveWindow()
        {
            var plan = Window.ToDayPlan(Course, Delegation.Resolver(Crew));
            var result = Director.ResolveDay(plan);
            Log($"D{result.DayIndex}: ran window — {Window.Accepted.Count} tasks, " +
                $"{Window.UsedHours:F1}/{Window.BudgetHours:F0}h used" +
                (Window.CouldNotFitEverything ? $", {Window.Rejected.Count} cut for hours" : ""));
            foreach (var line in result.Log) Log($"  {line}");
            if (result.Tournament != null)
                Log($"** TOURNAMENT — {result.Tournament} | prize ${result.Tournament.PrizeAwarded:N0}, rep {result.Tournament.ReputationDelta:+0;-0} **");
            BeginWindow();
            return result;
        }

        /// <summary>Toggle the assist layer. Changes only what's surfaced — the sim run is identical.</summary>
        public void SetAssists(bool on)
        {
            assistsEnabled = on;
            if (Legibility != null)
                Legibility.Settings = on ? DifficultySettings.WithAssists() : DifficultySettings.Full();
            Log($"Assists {(on ? "ON" : "OFF")} (surfacing only).");
        }

        /// <summary>Resolve one day using the current morning window (used by the debug "Resolve Day" button).</summary>
        public void ResolveDay() => ResolveWindow();

        /// <summary>
        /// Fidelity scaling (Phase 4.4 / GDD §1): fly through routine days on the delegated auto-program,
        /// and STOP the moment an interrupt (heat spike / fresh disease break) fires — which yanks the
        /// player into a full morning window for that day. Returns days skipped.
        /// </summary>
        public int SkipRoutineDays(int maxDays = 365)
        {
            int start = Director.Clock.DayIndex;
            Director.ClearInterrupt();
            int resolved = Director.SkipUntil(
                stop: _ => false,
                planProvider: day => AutoRoutinePlan(),
                maxDays: maxDays);

            if (Director.InterruptRaised)
            {
                // Re-open the window so the player attends the crisis day at full attention.
                BeginWindow();
                Log($"Skipped {resolved} day(s) ({start} -> {Director.Clock.DayIndex}); INTERRUPT — into the window.");
            }
            else
            {
                Log($"Skipped {resolved} day(s) ({start} -> {Director.Clock.DayIndex}).");
            }
            return resolved;
        }

        /// <summary>
        /// The ONLY time-advancement verbs (GDD §1): a Day (resolve YOUR queued plan, hands-on), a Week
        /// (≤7) or a Month (≤30) coasting on the delegated routine. Week/Month are INTERRUPT-GATED — they
        /// stop the instant a crisis fires (heat spike / storm / frost / flash drought / disease break) and
        /// are CLAMPED so they never coast past an upcoming tournament (you stop the morning of, to confirm
        /// spec). Returns a legible summary of the bite taken and why it ended. No sim/balance change — this
        /// just drives the existing pipeline + AutoRoutinePlan.
        /// </summary>
        public PeriodSummary Advance(AdvanceKind kind)
        {
            int target = kind == AdvanceKind.Day ? 1 : kind == AdvanceKind.Week ? 7 : 30;

            var cfg = Director.EconomyConfig ?? EconomyConfig.Default;
            var sum = new PeriodSummary { Kind = kind };
            sum.CondStart = ConditionSystem.CourseCondition(Course, cfg);
            sum.CashStart = Economy?.Cash ?? 0;
            int startDay = Director.Clock.DayIndex;

            // Approaching-event clamp: a coast stops the morning the tournament is due (be present to confirm).
            bool eventStop = false;
            if (kind != AdvanceKind.Day && Tournament?.Current != null)
            {
                int du = Tournament.DaysUntilNext(Director.Clock.DayIndex);
                if (du >= 0 && du < target) { target = du; eventStop = true; }
            }

            Director.ClearInterrupt();
            for (int i = 0; i < target; i++)
            {
                DayPlan plan = kind == AdvanceKind.Day
                    ? Window.ToDayPlan(Course, Delegation.Resolver(Crew)) // hands-on: run my morning
                    : AutoRoutinePlan();                                  // coast: the delegated routine
                var r = Director.ResolveDay(plan);
                sum.DaysAdvanced++;
                if (r.Tournament != null) sum.Tournament = r.Tournament;
                if (Director.InterruptRaised)
                {
                    sum.StoppedEarly = true;
                    sum.StopReason = r.Interrupts.Count > 0 ? string.Join("; ", r.Interrupts) : "a crisis";
                    break;
                }
            }

            // The tech runs the routine readings on a coast (data lands on the clipboard; the read does not).
            if (kind != AdvanceKind.Day && DelegateReadings) PerformTechReadings();

            if (!sum.StoppedEarly && eventStop && sum.DaysAdvanced >= target)
            {
                int duNow = Tournament.DaysUntilNext(Director.Clock.DayIndex);
                sum.StoppedEarly = true;
                sum.StopReason = duNow <= 0
                    ? $"{Tournament.Current.Name} is TODAY — confirm your spec"
                    : $"{Tournament.Current.Name} in {duNow}d — be present";
            }

            sum.CondEnd = ConditionSystem.CourseCondition(Course, cfg);
            sum.CashEnd = Economy?.Cash ?? 0;
            BeginWindow();

            string verb = kind == AdvanceKind.Day ? "Resolved a day" : $"Advanced {sum.DaysAdvanced}d ({kind})";
            Log($"{verb} ({startDay}→{Director.Clock.DayIndex})" +
                (sum.StoppedEarly ? $" — STOPPED EARLY: {sum.StopReason}" : ""));
            return sum;
        }

        /// <summary>
        /// The assigned tech reads the most-suspicious greens (ranked by the FREE visible tell), revealing
        /// the NUMBERS via the same legibility gate — but flagged tech-reported with a skill-scaled
        /// confidence, and coverage scales with skill so a weaker tech leaves greens for you to walk. The
        /// expert READ (the per-cell nuance you get standing on the green) is never delegated (§4.8).
        /// </summary>
        private void PerformTechReadings()
        {
            var tech = ReadingTech;
            if (tech == null) return;
            int day = Director.Clock.DayIndex;
            double quality = Delegation.StaffQuality(tech); // ≤ 0.95 ceiling

            var greens = new List<ZoneState>(Course.Greens);
            greens.Sort((a, b) => Severity(b, day).CompareTo(Severity(a, day))); // worst tells first
            int covered = Mathf.Clamp(Mathf.RoundToInt(greens.Count * (float)quality), 1, greens.Count);

            for (int i = 0; i < covered; i++)
            {
                var g = greens[i];
                Legibility.Scout(g.Id, day);
                Legibility.SoilTest(g.Id, day);
                Legibility.MeterReading(g, 0, day);
                TechReadConfidence[g.Id] = quality; // tech-reported (vs your own eyes)
            }
            Log($"{tech.Name} read {covered}/{greens.Count} greens (tech-reported, conf {quality:0%}).");
        }

        private double Severity(ZoneState g, int day)
        {
            var obs = Legibility.Observe(g, day);
            double worst = 0;
            foreach (var t in obs.Tells) worst = System.Math.Max(worst, t.Lesions + t.Thinning + t.WiltTint);
            return worst;
        }

        /// <summary>The crew's standard delegated program for a routine day (staff quality applies).</summary>
        private DayPlan AutoRoutinePlan()
        {
            var w = new MaintenanceWindow(Crew);
            // A competent crew won't mow/roll frozen turf — skip mowing on actual-frost days.
            bool frost = new WeatherSystem(weatherSeed).Generate(Director.Clock.DayIndex).TminF
                         < AgronomyTuning.Default.FrostThresholdF;
            foreach (var z in Course.Greens)
            {
                if (!frost) { var mow = TaskCatalog.WalkMow(z.Id); mow.Delegated = true; mow.AssignedCrewId = Crew[0].Id; w.TryAssign(mow); }
                var water = TaskCatalog.Water(z.Id); water.Delegated = true; water.AssignedCrewId = Crew[0].Id; w.TryAssign(water);
            }
            // A scheduled (calendar) spray — the delegated routine, blind to the live tell by design.
            if (Director.Clock.DayIndex % 14 == 0)
                foreach (var z in Course.Greens)
                {
                    var spray = TaskCatalog.Spray(z.Id); spray.Delegated = true; spray.AssignedCrewId = Crew[1 % Crew.Count].Id; w.TryAssign(spray);
                }

            // Whole-course surfaces on a rotation (delegated). They COMPETE for the same crew-hours —
            // over-budget gang passes are simply rejected, which is the cross-surface triage.
            int day = Director.Clock.DayIndex;
            if (!frost && day % 2 == 0) Delegate(w, TaskCatalog.MowFairwaysAll(), 2);
            if (!frost && day % 3 == 0) Delegate(w, TaskCatalog.MowTeesAll(), 3);
            if (!frost && day % 4 == 0) Delegate(w, TaskCatalog.MowRoughAll(), 4);
            if (day % 5 == 0) Delegate(w, TaskCatalog.RakeBunkers(), 2);
            return w.ToDayPlan(Course, Delegation.Resolver(Crew));
        }

        private void Delegate(MaintenanceWindow w, Greenkeeper.Sim.Crew.TaskOrder task, int crewIndex)
        {
            task.Delegated = true;
            if (Crew.Count > 0) task.AssignedCrewId = Crew[crewIndex % Crew.Count].Id;
            w.TryAssign(task);
        }

        public void SaveGame()
        {
            var data = SaveData.Capture(Course, weatherSeed, Director.Clock.DayIndex);
            SaveSystem.Save(data);
            Log($"Saved (schema v{data.SchemaVersion}) to {SaveSystem.DefaultPath}");
        }

        private void Log(string line)
        {
            RecentLog.Add(line);
            if (RecentLog.Count > MaxLogLines) RecentLog.RemoveAt(0);
            Debug.Log($"[Greenkeeper] {line}");
        }
    }

    /// <summary>The only time-advancement verbs (GDD §1). Maps to fidelity: Day = hands-on; Week = routine
    /// delegated, stop on trouble; Month = stable/coasting.</summary>
    public enum AdvanceKind { Day, Week, Month }

    /// <summary>A legible report of an advance: the bite taken and exactly why it ended (for the summary card).</summary>
    public sealed class PeriodSummary
    {
        public AdvanceKind Kind;
        public int DaysAdvanced;
        public bool StoppedEarly;
        public string StopReason = "";
        public double CondStart, CondEnd;
        public double CashStart, CashEnd;
        public Greenkeeper.Sim.Tournament.TournamentResult Tournament;

        public double CondDelta => CondEnd - CondStart;
        public double CashDelta => CashEnd - CashStart;
    }
}
