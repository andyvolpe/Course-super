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

## Committing the binaries (Git LFS)

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
