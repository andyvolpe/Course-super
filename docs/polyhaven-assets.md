# Adding Poly Haven assets to the play test

[Poly Haven](https://polyhaven.com) is CC0 (public domain) — free for any use, no attribution
required. It has three things we use: **HDRIs** (sky + lighting), **PBR textures** (turf/sand), and
**3D models** (trees).

The runtime scene (`GameBootstrap`) builds itself from primitives. It now **auto-loads optional
assets from `Assets/Resources/PolyHaven/` by name** and falls back to flat colours if they're
missing — so nothing here is required, and adding a file "just works" on the next Play.

## The names it looks for

Put each asset at `Assets/Resources/PolyHaven/<Name>` (the `Resources` folder is what makes runtime
`Resources.Load` work):

| Asset (Resources/PolyHaven/…) | Type      | Used for                                  |
|-------------------------------|-----------|-------------------------------------------|
| `Skybox.mat`                  | Material  | sky + image-based lighting (from an HDRI) |
| `Ground.mat`                  | Material  | the big backdrop plane                    |
| `Green.mat`                   | Material  | putting greens                            |
| `Approach.mat`                | Material  | approaches/collars                        |
| `Fairway.mat`                 | Material  | fairways                                  |
| `Tee.mat`                     | Material  | tees                                      |
| `Rough.mat`                   | Material  | rough                                     |
| `Bunker.mat`                  | Material  | bunker sand                               |
| `Tree.prefab`                 | Prefab    | scattered off the playing corridors       |

Surfaces are still **tinted by turf health** at runtime (the legibility tells multiply the texture),
and texture tiling is auto-set (~1 repeat / 2 m), so a grass material reads as colour-graded turf.

**Inheritance:** related surfaces borrow a texture when they don't have their own, so you don't have
to import the same grass five times. A single **Fairway** import also dresses **Tee**, **Approach**
and **Green**; **Ground** and **Rough** share. Import a key explicitly to override (e.g. a distinct
fine **Green** turf). So a minimal set is just **Fairway + Rough + Bunker + Skybox**.

## Fastest path: the built-in importer

**Window > Greenkeeper > Poly Haven Importer.** Paste a slug (the URL tail from
polyhaven.com, e.g. `aerial_grass_rock` or `kloofendal_43d_clear`), pick a resolution and the
target surface key, and hit Import. It downloads the maps from Poly Haven's CDN, imports them
(setting the normal map correctly), builds a lit material — or a Skybox material for an HDRI — and
saves it straight to `Assets/Resources/PolyHaven/<Key>.mat`, where the play test auto-loads it.
Needs internet; everything it writes is normal project assets you then commit (see Git LFS below).

It handles all three kinds:
- **Texture** → a lit material at `Resources/PolyHaven/<SurfaceKey>.mat`.
- **HDRI** → `Resources/PolyHaven/Skybox.mat`.
- **Model** → downloads the **FBX** (native Unity import, no extra package) + its textures and builds
  `Resources/PolyHaven/<PrefabKey>.prefab` (default `Tree`), which the scene scatters off the corridors.

The manual steps below do the same thing by hand if you'd rather.

## Steps

### Textures (turf, sand) → a Material
1. On polyhaven.com open a texture (e.g. *aerial_grass_rock*, *leafy_grass*, *brown_mud*, a sand one
   for bunkers). Download **2K** (plenty), format **PNG/JPG** (or the Unity-ready zip).
2. Drag the files into Unity (e.g. `Assets/Art/PolyHaven/Fairway/`). Set the **normal map** texture
   type to *Normal map*.
3. Create a Material (right-click → Create → Material). Assign Base/Albedo, Normal, and
   Metallic/Roughness (Mask) maps. URP: use *Universal Render Pipeline/Lit*.
4. Put (or move) that material at `Assets/Resources/PolyHaven/Fairway.mat`. Repeat per surface.

### HDRI → sky + lighting
1. Download an HDRI (e.g. *kloofendal_43d_clear*, *venice_sunset*), **.hdr/.exr**, 2K–4K.
2. Import; set texture **Shape = Cube** (it becomes a cubemap).
3. Create a Material with shader **Skybox/Panoramic** (or *Skybox/Cubemap*), assign the HDRI.
4. Save it at `Assets/Resources/PolyHaven/Skybox.mat`. On Play it becomes the sky and ambient light.

### Models (trees) → a Prefab
1. Download a model as **glTF** or **FBX**; import into Unity.
2. Drag it into a scene, adjust scale, drag back into the Project to make a **prefab**.
3. Save it at `Assets/Resources/PolyHaven/Tree.prefab`. It's scattered along the rough edges
   (off the ±4.5 m playing corridor) automatically.

## Real trees (drop-in model packs)

The play test scatters **every prefab in `Assets/Resources/Trees/`** (plus `Resources/PolyHaven/Tree`)
randomly along the rough edges and as a perimeter forest. With none present it falls back to simple
procedural trees (placeholders — they look like primitives on purpose).

To get real 3D trees, import a CC0 low-poly nature pack and drop the tree prefabs into
`Assets/Resources/Trees/`:
- **Quaternius — Ultimate Nature / Stylized Trees** (CC0): https://quaternius.com — FBX/glTF, very low-poly, ideal.
- **Kenney — Nature Kit** (CC0): https://kenney.nl/assets/nature-kit
- Any Asset Store tree pack works too — just place the prefabs in that folder.

Make each a prefab (drag the model into a scene, then back into the Project), move it into
`Resources/Trees/`, and press Play. Several different prefabs → varied forest.

## The ground is now Unity Terrain

The play test builds a real **Unity Terrain** at runtime: a heightmap (rolling hills), **three
splat-blended texture layers** (rough / fairway / dirt mixed by noise, so it doesn't read as one
repeating tile), a TerrainCollider you walk on, and efficient **Terrain tree instances** from your
imported pack. The maintained surfaces (greens/fairways/tees/bunkers) still sit on top as the
sim-driven meshes. The splat layers pull their textures from your `Resources/PolyHaven/Rough`,
`Fairway`, and `Ground` materials if present (else flat colours), so importing those improves the
ground automatically. If Terrain creation fails for any reason it falls back to the old mesh ground.

Geometry grass: the Terrain now carries a **detail grass layer** in the rough + surrounds (real
camera-facing grass blades), masked OFF the greens/fairways/tees/bunkers so blades never grow on the
short surfaces. It uses a generated grass-blade billboard, so it needs no imported asset; it waves in
a light wind. Density is clumpy (Perlin noise).

## Grass that doesn't look obviously tiled

The surfaces sample a tiling texture, so a single repeating texture reads as a grid. Mitigations now
baked in: larger tile distance + a per-surface UV rotation so neighbours don't line up. To go
further, in order of effort:
1. **Better source texture** — use a *seamless* grass at 2K–4K (Poly Haven `aerial_grass_rock` is okay;
   a dedicated seamless lawn texture is better), and raise the material's tiling so blades look fine.
2. **Anti-tiling shader** — a triplanar / noise-blended material hides repetition entirely (no Unity
   Terrain needed). Ask and I'll add a URP shader the surfaces use.
3. **Unity Terrain** — the "proper" route: splat-mapped ground (multiple blended textures + normals)
   with the built-in tree + detail-grass systems (real geometry grass). Bigger change; ask and I'll
   migrate the course onto Terrain.

HDRIs/textures are large; track them with Git LFS so the repo stays sane:

```
git lfs install
git lfs track "*.hdr" "*.exr" "*.png" "*.jpg" "*.fbx" "*.glb"
git add .gitattributes
```

Then commit the assets normally.

## Notes / limits
- No official Poly Haven Unity plugin exists (they have a Blender add-on); the manual route above is
  the supported path. There's also a public API at `https://api.polyhaven.com` if you want to script
  downloads later.
- Everything is optional and additive: with no `Resources/PolyHaven/` files the scene looks exactly as
  it does today (flat colours), so you can add surfaces one at a time.
