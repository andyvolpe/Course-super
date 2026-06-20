# Greenkeeper — implemented agronomy model (Phases 0–2)

This documents the simulation as actually built in `Assets/Sim`. It is the authoritative reference
until the original GDD/TDD/Test-Spec source docs are added to the repo (see [README](README.md)).

All tunable coefficients live in one file: **`Assets/Sim/Config/AgronomyTuning.cs`**.

## Architecture

| Assembly | Folder | Rule |
|---|---|---|
| `Greenkeeper.Sim` | `Assets/Sim` | **Pure C#. No `using UnityEngine`.** `noEngineReferences: true`. |
| `Greenkeeper.Unity` | `Assets/Unity` | MonoBehaviours, ScriptableObject wrappers, save, UI, input. References Sim. |
| `Greenkeeper.Tests` | `Assets/Tests` | NUnit only + Sim. Runs in Unity Test Runner **and** headless via `dotnet test`. |

Determinism is enforced by construction: the sim takes a seed and a per-day plan, uses an injected
splitmix64 `Rng`, and never touches `DateTime.Now`, frame time, or `UnityEngine.Random`.

## State (`ZoneState`)

Natural units stored; normalized to 0–1 *inside* the math; every accumulator clamped each tick.

`SoilMoisturePct, RootDepthIn, DensityPct, OrganicMatterPct, TurfDebtPct, CarbReservesPct,
NitrogenPct, SoilTempF, GddAccum, GrainPct, ClipVolume, FirmnessPct, Stimp`, plus per-green
**9-cell grid** of `SubCell { Pressure, Infection, ExpressionSeverity }` and maintenance bookkeeping
(`MowHeightIn, SprayResidualDaysLeft, DaysSinceAeration, AerationRecoveryDaysLeft, RollBonus`).

## Daily resolve pipeline (`GameDirector.ResolveDay`)

Per day, course-wide weather is generated once; then each zone runs these steps in order (zones are
independent except for intra-green disease diffusion):

1. **Weather** — deterministic from `(seed, dayIndex)`; temperate climate, humid summers.
2. **Inputs** — fertilizer raises nitrogen (so it affects the same day's growth & disease).
3. **Water balance** (§4.1) — inputs − ET − drainage. **Guards:** drainage `= drainFrac · max(0, m−FC)`
   (one-directional); ET uses Hargreaves with `sqrt(max(0, Tmax−Tmin))`. USGA-spec drains faster than
   push-up.
4. **Soil temp + GDD** (§4.7) — soil temp lags air mean; `GDD += max(0, Tmean − base)`.
5. **Growth / clip / reserves** (§4.7) — growth `≥ 0`, gated by temp-bell × moisture × nitrogen;
   carbohydrate reserves credit photosynthesis, debit respiration + growth; density and nitrogen
   updated; all clamped.
6. **Organic matter + grain** (§4.7) — thatch from growth − decomposition; grain from growth.
7. **Disease** (§4.2/§4.3) — see below.
8. **Maintenance** (mow/roll/spray/aerate) — spray knocks down pressure/infection and sets residual.
9. **Turf debt** (§4.5) — see below.
10. **Derived surfaces** (§4.4) — firmness; Stimp **clamped 6–15**; roll bonus decays.
11. **Counters** — residual/recovery timers, aeration age.

An **interruptible skip** (`SkipUntil`) resolves days until a predicate or an interrupt flag stops it.

## Disease — dollar spot (`DiseaseSystem`)

`favorability = max(0, tempBell) · max(0, wetness) · max(0, lowNitrogen) · susceptibility`, scaled
down while a spray residual is active. Pressure accrues per cell and **diffuses** across the green's
grid. **Infection only advances once pressure crosses the tell threshold**, so a readable tell always
precedes infection.

### The fairness gate (KEYSTONE, GDD §3.1)

A visible **expression** may fire **only** when a readable tell is present:
`pressure > threshold OR infection > 0 OR moisture < wiltPoint OR density < thinThreshold`.
With no tell, `P(express) = 0`. The gate is mandatory and test-locked (T4). A `BypassFairnessGate`
"teeth" switch exists **only** so the test can inject ungated expressions and prove the audit catches
them — it is always `false` in normal play.

## Turf debt (`TurfDebtSystem`)

A single accumulator (0–100). Offenses add to it (scalping, wet-traffic, drought, over-water,
nitrogen starvation, skipped aeration, untreated disease). **Carbohydrate reserves only scale the
accrual rate** — low reserves amplify every offence. A clean day pays debt down. Past the bleed
threshold (~45) debt erodes density (visible thinning) before it can drive collapse.

## Tests (`Assets/Tests`) — all green

Run headless: `bash scripts/run-tests.sh` (or `dotnet test headless/Greenkeeper.Tests`), or in Unity
via the EditMode Test Runner.

| Test | Contract | Result |
|---|---|---|
| Phase 1.1 | clock advances 90 days; same-seed sequence; skip stops on interrupt | ✅ |
| Phase 2.1 | 18 greens (9 cells each), 18 tees/fairways, 40 bunkers; healthy start | ✅ |
| Phase 2.2 | zero-water+heat → monotonic dry-down; USGA drains faster; 500-day invariants | ✅ |
| Phase 2.3 | mismanaged green gets sick, well-kept stays clean | ✅ |
| **T1** Determinism | same seed+plan → deep-equal final state | ✅ |
| **T2** Invariant fuzz | 12,000 random days, no NaN/out-of-range/throw | ✅ |
| **T3** Behavioural | mean bad infection > 2× good over 300 seeds (≈ 100 vs 0) | ✅ |
| **T4** Fairness | 300 seeds: no expression without a tell; teeth check detects ungated ones | ✅ |
| Phase 3 survival | a managed green stays alive a full season; mismanagement thins it | ✅ |
| Phase 3.1 fairness | N-push == vigour by colour; starved paler; debt not encoded | ✅ |
| Phase 3.2 gate | hidden state stays hidden until scouted/metered; debt never on full | ✅ |
| Phase 3.3 assists | assists change surfacing only — sim state byte-identical (determinism) | ✅ |
| Phase 4.1 budget | over-assignment beyond ~30 crew-hours is rejected, forcing triage | ✅ |
| Phase 4.2 delegation | delegate-vs-hands-on quality gap, skill-scaled, never closes | ✅ |
| Phase 4.4 interrupts | quiet days skip; disease break / heat spike stop the skip | ✅ |
| A+.1 ball physics | faster green rolls farther; firmer green releases more; slope breaks | ✅ |
| 5.1 weather | weather is seed-deterministic; different seeds differ | ✅ |
| 5.2 forecast | near-term forecast tighter than far-term; misses happen; today == actual | ✅ |
| 5.3 events | storm washes out bunkers (rake restores); frost blocks mowing | ✅ |

## Phase 5 — weather, forecast, extreme events (`Assets/Sim/Systems`)

Deepens the proven loop without a new meta-layer: the agronomy already consumed weather; now you
plan against a fallible forecast.

- **5.1** `WeatherSystem` (already deterministic from Phase 2.2) extended with humidity; it is the
  real source the pipeline runs on. Determinism test added.
- **5.2** `Forecast`: a view of the next N days where each value carries an error band that TIGHTENS
  as the day approaches (`band = perDayBand · daysOut`) and can still miss. Deterministic from the
  seed. The gap between forecast and reality is the gameplay.
- **5.3** Extreme events fire pipeline interrupts (heat spike / storm / frost / flash drought) that
  stop the skip; storms wash out bunkers (a `RakeBunkers` task restores them) and frost blocks mowing
  until it lifts. `GameDirector.WeatherInterruptsEnabled` gates them (off to isolate clock tests).
- **5.4** `ForecastHud` (Unity): the forecast strip with visibly widening bands to plan against.

**Forecast-gamble tuning** (headless `Greenkeeper.FeelTest`): heat-spike threshold 91°F gives ~12
real spikes/yr; the +3-day forecast pays off ~60% of calls, wastes ~40%, and ~8/yr catch you flat —
useful but fallible.

## Milestone A finisher — the minimal putt (`Assets/Sim/Physics`)

The ball reads the course (TDD §7): putt/approach behaviour is a deterministic function of the
green's MAINTAINED state — no separate physics tuning to fiddle.

- **A+.1 `BallPhysics`** (Sim, pure C#): Stimp → roll distance; firmness → approach release + bounce;
  mesh slope → break + downhill/uphill roll. Tested headless.
- **A+.2 `PuttingController`** (Unity): putt + short approach on the existing first-person controller,
  rolling across the real green mesh with a per-hole stroke tally.
- **A+.3 interlock** (verified headless in `Greenkeeper.FeelTest`): a MEMBER setup (Stimp ~9.6, soft)
  and a TOURNAMENT setup (Stimp ~11.7, firm) reached *through maintenance* (mow height, water, roll)
  play measurably differently — the same putt rolls ~23% farther on the tournament green.

**Milestone A is the full fun-core:** read the course (legibility) → decide and act under the
hour-squeeze (window) → walk out and putt on the conditions you made (ball physics). The remaining
gate is not code — it is *playing the whole loop and judging if it's fun* (see README).

## Phase 4 — the maintenance window (`Assets/Sim/Crew`)

The daily heartbeat: a crew-hour budget you cannot beat, a delegation depth-dial, and time that
scales to stakes.

- **4.1 crew + hours.** `CrewMember` / `TaskOrder` / `TaskCatalog` (hour costs + effect mapping) +
  `MaintenanceWindow` (fixed budget = sum of crew availability ≈ 30 h; over-assignment rejected/
  flagged). Tasks convert to a per-zone plan applied on resolve step 6.
- **4.2 delegation.** `Delegation.StaffQuality = clamp(0.55 + 0.4·skill + 0.15·knowledge, 0, 0.95)`,
  player = 1.0 — a hard ceiling staff never beat. Quality scales the beneficial deltas; delegated
  spray runs on a **schedule, blind to the live tell**. Fully-delegated carries measurably more
  disease than hands-on; the gap shrinks with skill but never closes.
- **4.3 window UI** (`MaintenanceWindowHud`): budget bar, per-task delegate/do-it-myself + crew,
  over-assignment blocked, Resolve Window. **Triage squeeze verified headless**: a full program is
  ~59.5 h against a 30 h budget (~50% doable); keeping the course presentable (29.2 h) leaves no
  room to also attend a brewing #7 — something gets cut.
- **4.4 fidelity scaling.** Interrupt detection (pipeline step 11): heat spike / fresh disease break
  stop the interruptible skip and pull the player into the window; routine days fly by on the
  delegated auto-program.

## Phase 3 — legibility (`Assets/Sim/Legibility`)

Built on top of the agronomy core; all pure C# and headless-tested.

- **3.0 tuning pass.** The first neglect feel-test lurched fine-to-dead (summer ET was
  ~16 VWC%/day, so greens died of drought-debt before disease could develop). Fixed: a
  single realistic `MmToVwcPct` conversion for rain/irrigation/ET, lower weather radiation
  (summer ET ~6–7 mm/day), slower turf-debt offences + density bleed, baseline+starvation
  dollar-spot N susceptibility, and an infection→thinning link. The curve now reads as a
  ramp: ~6-day latent pressure build → tell + infection (~day 6–7) → visible thinning
  (~day 14) → collapse (~day 36); a well-kept green survives.
- **`LegibilityMapping`** — state → honest `TellAppearance` per sub-cell. Tells NARROW
  (dark green is ambiguous between vigour and N-push); turf debt is never an input.
- **`LegibilitySystem`** — THE single gate. Free visual tells always; infection / exact
  moisture / nutrients only when earned via scout / meter / soil-test; turf debt never
  surfaced on full difficulty. Read-only w.r.t. the sim.
- **`DifficultySettings`** — the assist layer changes only *surfacing* (debt bar, threat
  telegraph, auto-scout), never the simulation.

The Unity layer (`Assets/Unity/Rendering` + `Play` + `UI`) is a dumb consumer: the
`GreenSurface` URP shader renders the per-cell tell channels, `GreenRenderer` pushes them
through the gate (one material per sub-cell), and first-person inspection earns deeper reads.
