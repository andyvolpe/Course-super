# HANDOFF — Greenkeeper (visual/art + recent work)

Pick-up notes for a **local** Claude Code session (this was written from a cloud
sandbox that has **no Unity and no git-lfs**, so all visual work so far was done
blind from screenshots). Branch: `claude/greenkeeper-phases-0-2-c9urh2`.

## How to ramp instantly
- Read `docs/greenkeeper-model.md` (sim model + the "Milestone B-prep" section
  on the diegetic UI/UX work).
- Sim is pure C# under `Assets/Sim`; **never** add a UnityEngine dependency there.
  Headless tests: `dotnet test headless/Greenkeeper.Tests/Greenkeeper.Tests.csproj`
  (73 tests, must stay green). A pre-commit/CI hook also runs them.
- All visuals/scene live in `Assets/Unity/Play/GameBootstrap.cs` (procedural
  course builder) + `Assets/Unity/Play/SurfaceRenderer.cs` (per-zone turf colour).

## Hard constraints (keep honoring)
- No changes to `Assets/Sim` for visual work; no balance changes.
- Develop on branch `claude/greenkeeper-phases-0-2-c9urh2`. Commit + push.

## Art direction (current target)
**Links / meadow.** Open, windswept; firm green playing surfaces; tawny golden
fescue rough; big coastal sky. Reference image the user gave is AAA-render
quality (EA PGA Tour-like) — that's the north star.
**Hero turf: PolyHaven `grass_bermuda_01`** for the playing surfaces.

## Hero-turf wiring (already in code — just add the asset)
`GameBootstrap.SharedSurfaceMaterial` → `HeroTurf()`:
- Uses `Resources/PolyHaven/grass_bermuda_01.mat` if present, **else**
  `BuildPbrFromFolder("PolyHaven/grass_bermuda_01")` auto-assembles a URP/Lit
  material from the raw PolyHaven map set (diff/nor/ao matched by filename).
- Applies to green/fairway/tee/approach. Rough = the meadow **terrain** (not a
  mesh; see below). `SurfaceRenderer` tints `_BaseColor` per turf health via MPB.
- **TODO when adding the asset:** set the normal map's import Type = *Normal map*
  in the Editor (runtime-loaded normals aren't flagged correctly), or author the
  `.mat` directly. Git LFS is configured (`.gitattributes`); run `git lfs install`
  locally before adding the textures.

## Visual journey so far (diagnoses that stuck — don't re-litigate)
1. **Dark rough polygon** = an opaque rough MESH drawn over terrain that is
   already rough. Fix: rough mesh is now **collider-only** (renderer disabled);
   terrain IS the rough. Lie detection still works via the collider.
2. **Patchwork blotches** = terrain splat painting fairway-bright/dirt patches.
   Fix: calmer splat; then repurposed to links meadow (sage base + golden fescue
   drifts + rare sand). Also fixed a real bug: `Layer()` ignored the requested
   tone and hardcoded one green for every grass layer.
3. **Dark hard-edged surface meshes** = NOT colour — **faceted normals** from
   coarse draped geometry shading dark vs the smooth terrain. Fix:
   `DrapeOntoTerrain` now assigns **smooth terrain normals** (finite differences)
   instead of `RecalculateNormals`. THIS removed the angular patchwork.
4. **Surface meshes too dark** = `SurfaceRenderer` MPB multiplies the turf
   texture by the dark agronomy "tell" colour (double-dark). Fix: healthy turf
   now tints **near-bright**; only real problems go dark/off-colour.
5. **Grid/checkerboard on surfaces** = 256px turf texture tiling every 4-5 m with
   non-seamless noise + baked mow-stripes. Fix: **seamless** tileable noise,
   dropped baked stripes, enlarged world tile (Ribbon/Blob → 9 m).
6. **Grass "sinking"/"shit"** = no grass assets exist; only the generated
   billboard rendered. Fix: lush base-anchored generated tuft, now retinted to
   **golden fescue**; plus `SeatOnGround` for scattered prefabs (path only runs
   if real grass prefabs are added to `Resources/Grass`).
7. **Atmosphere pass** (conservative, built-in only): warm low sun + SOFT
   shadows, procedural sky + sky-lit ambient, distance fog, camera clears to sky.

## NEXT — phase 2 (do this locally, where you can verify)
1. **URP post-processing volume** — the single biggest quality jump, **deferred**
   in the cloud because it needs URP package refs in
   `Assets/Unity/Greenkeeper.Unity.asmdef` (`Unity.RenderPipelines.Universal.Runtime`,
   `Unity.RenderPipelines.Core.Runtime`) + `renderPostProcessing=true` on the
   camera, and a wrong assembly name breaks the whole Unity compile. Add a global
   Volume with ACES Tonemapping, ColorAdjustments, subtle Bloom, Vignette; enable
   SSAO via the URP renderer. Verify it actually renders before committing.
2. Drop in `grass_bermuda_01` (hero) + consider real tree/bunker/water assets.
3. Tune the links palette (golden/green balance, bermuda tint) from a real
   screenshot — values in code are best-guess.
4. Optional: real large-scale mow bands on fairways/greens (do via world-space
   shader/vertex, NOT a tiling texture — that caused the grid).

## Gameplay state (for context)
Time advances via Day/Week/Month only (`GameManager.Advance`); reading delegation
+ measurement tools (stimp/firmness/moisture) wired; diegetic maintenance
building UI. All presentation-layer; sim untouched. See the docs section.
