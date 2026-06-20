# Greenkeeper — docs

> **Note on the original specs.** The Phase 0–2 build guide references five spec
> files (`greenkeeper-gdd.md`, `greenkeeper-tdd.md`, `greenkeeper-test-spec.md`,
> production plan, decision log). Those source documents were **not present in
> this repository** when Phases 0–2 were implemented. The simulation was built
> from the model details embedded inline in the build guide, and the resulting,
> self-consistent model is documented here in **[`greenkeeper-model.md`](greenkeeper-model.md)**.
>
> If/when the authoritative spec docs are added, reconcile the constants in
> `Assets/Sim/Config/AgronomyTuning.cs` against them.

## Contents

- **[greenkeeper-model.md](greenkeeper-model.md)** — the implemented agronomy
  model: state schema, the daily resolve pipeline, every equation and guard,
  the disease/fairness/turf-debt systems, and the keystone-test contracts.

## Architecture rule (non-negotiable)

The simulation is **pure C# with zero `using UnityEngine`**, in its own assembly
(`Greenkeeper.Sim`), so it is testable headless. ScriptableObjects (which *are*
UnityEngine) live in the Unity layer (`Greenkeeper.Unity`) and convert to plain
structs the sim consumes. Tests (`Greenkeeper.Tests`) reference only `Greenkeeper.Sim`
+ NUnit, so the **exact same test files run inside the Unity Test Runner and
headless via `dotnet test`** (see `headless/`).
