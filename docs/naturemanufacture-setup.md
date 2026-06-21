# Using NatureManufacture Dynamic Nature (Forest + Meadow) in the play test

You own **Dynamic Nature – Forest Environment** and **Meadow Environment** and imported them. The
runtime scene loads assets from **`Resources/` folders by name** (it auto-builds, so there's no
Inspector to wire). To use the NM content, copy the specific prefabs/assets you want into these
folders (create the folders if they don't exist):

| Folder | What to put there | Used for |
|---|---|---|
| `Assets/Resources/Trees/` | NM **tree prefabs** (a handful — e.g. a few pines/oaks) | scattered as Terrain tree instances (LODs/billboards work) |
| `Assets/Resources/Grass/` | NM **grass prefabs** (meshes) and/or grass **billboard textures** | the Terrain detail-grass layer in the rough + surrounds |
| `Assets/Resources/TerrainLayers/` | NM **TerrainLayer** assets named `Rough`, `Fairway`, `Ground` | the terrain ground splat (textured + normal-mapped) |

### How to copy without breaking anything
- In the Project window, find an NM prefab, **right-click → Duplicate** (or Ctrl/Cmd-D), then **drag
  the copy into the target Resources folder**. (Dragging the original just *moves* it, which also
  works since nothing else in this project references it — but duplicating is safer.)
- A prefab keeps all its material/texture references wherever they live; only the prefab itself needs
  to be under `Resources/`.

### Recommended starter set
- **Trees:** drop 4–8 varied NM tree prefabs into `Resources/Trees/`. The scene picks randomly, so
  variety reads as a natural forest. (Start with fewer if the framerate dips — NM trees are detailed.)
- **Grass:** drop 2–4 NM grass prefabs into `Resources/Grass/`. They're painted (clumpy) through the
  rough/surrounds and **masked off greens/fairways/tees/bunkers** automatically. They wave in wind.
- **Ground (optional):** if NM ships TerrainLayer assets, copy three you like into
  `Resources/TerrainLayers/` named `Rough` / `Fairway` / `Ground`. Otherwise the terrain uses the
  PolyHaven/flat textures as before.

Then press **Play**. Trees + grass come from NM; greens/fairways stay the sim-driven surfaces.

### Notes
- If a grass prefab renders oddly (unlit/too dark), it may want `DetailRenderMode.VertexLit` instead
  of `Grass` — tell me and I'll switch it (one line). The detail step is wrapped in try/catch, so a
  bad prototype just skips grass rather than breaking the scene.
- NM assets are large — track them with **Git LFS** before committing (`*.fbx *.png *.tga *.exr`).
- Tree/grass counts and view distance are tunable; say the word if it's too dense/sparse or heavy.
