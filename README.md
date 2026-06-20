# Greenkeeper

An agronomy simulation game: manage a golf course where the greens get sick if you mismanage them.
This repo is at the end of **Phases 0–2** — a deterministic, headless agronomy sim with a green
keystone-test bar guarding it, plus the Unity layer (first-person controller, config/save, debug UI)
ready for Phase 3.

> **Project status / Unity note.** The simulation, tests, and Unity scripts are complete and the
> keystone tests pass. This repo carries the source + assembly definitions + package manifest; open
> it with **Unity 6 (6000.x LTS)** to generate the rest of the editor project. The original spec docs
> referenced by the build guide were not in the repo, so the implemented model is documented in
> [`docs/greenkeeper-model.md`](docs/greenkeeper-model.md).

## Architecture (the one non-negotiable rule)

The simulation is **pure C# with zero `using UnityEngine`** (`Greenkeeper.Sim`), so it is testable
headless. ScriptableObjects live in the Unity layer (`Greenkeeper.Unity`) and convert to plain structs
the sim consumes. Tests (`Greenkeeper.Tests`) use only NUnit + Sim, so **the same test files run in the
Unity Test Runner and headless via `dotnet test`**.

```
Assets/
  Sim/      State/ Systems/ Math/ Config/   -> Greenkeeper.Sim   (pure C#)
  Unity/    Managers/ UI/ Input/ Play/ Config/ Save/  -> Greenkeeper.Unity
  Tests/    keystone + phase tests          -> Greenkeeper.Tests (NUnit)
headless/   .NET projects to compile+run the sim/tests without Unity
docs/       model + status
scripts/    run-tests.sh (the test gate)
```

## Run the tests (no Unity required)

```bash
bash scripts/run-tests.sh
# or: dotnet test headless/Greenkeeper.Tests/Greenkeeper.Tests.csproj
```

Enable the pre-commit gate so the keystone tests must pass before any commit:

```bash
git config core.hooksPath .githooks
```

## In Unity

1. Open the project with Unity 6 (6000.x LTS). It will import the packages in `Packages/manifest.json`
   (URP, Test Framework, Newtonsoft JSON).
2. **Edit → Project Settings → Player → Active Input Handling → Both** (the FP controller uses the
   legacy Input Manager axes).
3. Sandbox scene: create a `Player` (CharacterController + `FirstPersonController`, camera as child),
   a ground `Plane`, and a GameObject with `GameManager` + `DebugHud`. Press Play to walk around and
   click through the day loop.
4. EditMode tests: **Window → General → Test Runner → EditMode → Run All.**

See [`docs/greenkeeper-model.md`](docs/greenkeeper-model.md) for the full model and the test matrix.
