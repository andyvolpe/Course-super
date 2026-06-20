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
| **T3** Behavioural | mean bad infection > 2× good over 300 seeds (≈ 55 vs 0) | ✅ |
| **T4** Fairness | 300 seeds: no expression without a tell; teeth check detects ungated ones | ✅ |
