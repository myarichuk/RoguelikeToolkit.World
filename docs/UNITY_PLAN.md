# Unity plugin — design plan

Status: **plan, no code.** Companion to [REVIEW_2026-10.md](REVIEW_2026-10.md) and
[WEATHER_PLAN.md](WEATHER_PLAN.md).

## 1. Where we are

Unity support today is **packaging only**:

- `upm/com.rogueliketoolkit.world.core` and `.scripting.jint` ship prebuilt `netstandard2.1` DLLs.
- Each has a `link.xml` and an `IUnityLinkerProcessor` that injects it.
- Third-party DLLs come from UnityNuGet.
- `tools/upm/pack.py` builds the tarballs and a plugins zip.

Missing:

- any `UnityEngine` code: no MonoBehaviour, mesh, texture, shader, picking or editor window;
- a `Presentation` build for Unity (it is `net10.0` only and not packaged);
- async or progressive generation;
- a Unity test project;
- verification that Mono and IL2CPP produce the same world.

The rendering knowledge lives in `App/GlControl.cs` (Avalonia + raw GL):

- **Mesh**: the triangle dual of the hex grid, from `IcosphereGenerator`. Vertex = tile. It is a
  non-indexed soup with per-face barycentrics, and hexes are drawn in the fragment shader by the
  Voronoi split of each triangle.
- **Displacement**: radial, via `TerrainShading.DisplacedRadius`.
- **Flat maps**: `IProjection` at z=0.
- **Picking**: `HexPicker`, which is broken on flat and terrain views (review #6).
- **Region/local 2D views**: `PlainMap` + `FlatHexLayout` into Avalonia polygons.

The palettes, `TerrainShading`, `TerrainNormals`, the projections, `FlatHexLayout`/`PlainMap` and
`HexPicker`'s ray math are engine-agnostic. The mesh assembly and per-tile colouring
(`TileDebugColor`, `BuildOverlays`) are trapped inside `GlControl`.

## 2. Goals

1. **Runtime API** a game calls to get a globe mesh, flat map textures, hex-prism 3D terrain,
   region/local heightmaps and picking, independent of render pipeline.
2. **Components** that render a planet in a scene with layer switching, hover/click picking,
   drill-down (planet → region → local) and an orbit camera.
3. **Editor tooling**: a world asset with an inline live-preview globe, a generator window with
   debounced, cancellable re-generation while tweaking parameters, and a bake step that writes
   textures, meshes, heightmaps and the world store into the project.
4. **Weather preview** once `WEATHER_PLAN.md` W3+ lands: clouds, storm tracks and a time scrubber.
5. **Same seed → same world in Editor (Mono) and Player (IL2CPP)**, checked by a test, not assumed.

Non-goals: HDRP-specific features in v1; DOTS/ECS integration in v1 (keep the door open, see §8);
running Core under Burst. Core is managed, span-based C#, and that is fine on worker threads.

## 3. Package layout

| Package | Asm | Contents |
|---|---|---|
| `com.rogueliketoolkit.world.core` (exists) | prebuilt DLL | unchanged |
| `com.rogueliketoolkit.world.presentation` (**new**) | prebuilt DLL, `netstandard2.1` | Presentation, plus the new engine-agnostic builders below. **No UnityEngine reference**: the App and the web editor's C# side reuse it too. |
| `com.rogueliketoolkit.world.unity` (**new**) | source, asmdefs `Runtime`, `Editor`, `Tests` | MonoBehaviours, ScriptableObjects, shaders, editor windows, samples (`Samples~`) |

Shipping the Unity layer as **source** (not DLL) is deliberate. Users debug into it, and shader and
asmdef conventions need it. Core stays a DLL for determinism and IL2CPP-tested builds.

## 4. Engine-agnostic layer (Presentation)

Extract and add plain-C# builders that write into caller-provided spans or arrays. They have no GL
and no Unity dependency, are unit-testable, and are shared by App, Unity and (through WASM) the
editor.

```csharp
// Globe: dual-hex triangle mesh (today's GlControl layout, extracted).
GlobeMesh GlobeMeshBuilder.Build(WorldDataStore store, GlobeMeshOptions o);
//   o: Relief scale, OceanScale, FlatHexFill, Seams for UV, CoordinateSystem (RightHandedZUp | LeftHandedYUp)
//   out: positions, normals, tileIds(3 per vertex: own + two face partners), barycentrics, indices (uint)

// Per-tile colours for a layer, decoupled from geometry: recolour = rewrite one array.
void TileColorizer.Fill(World world, MapLayer layer, Span<Rgba32> perTile, ColorizeOptions o);
//   MapLayer: Plates, Biome, Elevation, Relief, Hydrology, Climate(T|P|Wind), Deposits, Political(history), Weather(t)

// Flat map raster (port of MapEditor/src/engine/world/rasterize.ts to C#).
TileIndexRaster FlatMapRasterizer.BuildIndex(WorldDataStore store, IProjection proj, int w, int h);  // pixel -> tile, cached
void FlatMapRasterizer.Paint(TileIndexRaster idx, ReadOnlySpan<Rgba32> perTile, Span<Rgba32> pixels);  // O(pixels), no lookups
void FlatMapRasterizer.Heights(TileIndexRaster idx, ..., Span<float> heights, bool bilinear);          // smooth heightfield

// Cubemap faces: better than equirect for globe texturing (no pole pinch); same index+paint split.
TileIndexRaster CubeFaceRasterizer.BuildIndex(store, face, size);

// 3D hex terrain (Civ-style extruded columns) for planet patches and region/local maps.
HexPrismMesh HexPrismBuilder.Build(PlainMap map, HexPrismOptions o);   // top caps + skirts, per-cell height/colour

// Region/local heightmaps for engine terrains.
void HeightmapBaker.FromRegion(RegionHandle r, int resolution, Span<float> dst);   // e.g. 513 or 1025 for Unity Terrain
void HeightmapBaker.FromLocal(LocalMapHandle l, int resolution, Span<float> dst);

// Picking (fixed: review #6).
bool HexPicker.TryPick(in Ray ray, PickSurface surface, out int tile);   // surface: Sphere | DisplacedSphere(store, relief) | Plane(projection)
```

Key design points:

- **Index/paint split.** Pixel→tile and vertex→tile maps are computed once per (world, projection,
  resolution). Switching layers or advancing weather time is then a linear pass over the colour
  buffer, with no `GetTileIndex` calls. This is what makes a live preview cheap.
- **Crisp hexes.** Each vertex carries the three corner tile ids of its triangle (`tileIds`) plus a
  barycentric. The fragment shader picks the tile with the largest barycentric weight and samples
  its colour from a **per-tile colour texture**, a data texture of `ceil(tiles/4096) × 4096`, RGBA32.
  Hex fills come out flat with no interpolation smear (review: "hex mode blends tile colours"), and
  borders are drawn where the top-2 weights meet. Recolouring is a single texture upload, with no
  mesh rebuild.
- **Coordinates.** Core is right-handed with Z as the polar axis. Unity is left-handed, Y-up. Put
  the conversion in one place (`CoordinateSystem` option: swap Y and Z, which flips handedness, and
  keep winding consistent). Never sprinkle it through components.
- **Seams.** Flat meshes split triangles at ±180° instead of NaN-hiding them, and give pole vertices
  the average longitude of their face partners (review: flat-map artifacts).
- **`netstandard2.1` port of Presentation**: replace `ArgumentNullException.ThrowIfNull`
  (`PlainMap.cs:124,145,168`), link Core's `IsExternalInit` polyfill, and run the Presentation tests
  under `-p:CoreTargetFramework=netstandard2.1` in CI the same way as Core.

## 5. Core changes the plugin needs

| Change | Why |
|---|---|
| `IProgress<GenerationProgress>` + `CancellationToken` on `WorldBuilder.Build` / pipeline `Execute` (per stage, and per sub-step inside long stages: erosion iterations, ocean solve, history turns) | Editor live preview must cancel a stale build when a slider moves, and the progress bar needs real data |
| **Step runner**: `pipeline.CreateRunner(map)` returning `bool Step(TimeSpan budget)` | WebGL has no threads, so generation has to be time-sliced in a coroutine. The same runner gives the desktop the progress callback |
| Fix the build leak (review #10) | Cancelled or failed preview builds otherwise leak native memory on every slider tick |
| Lift the "store under CWD" restriction (`ResolveStorePath`) behind an explicit `AllowAnyPath` / a root option | `Application.persistentDataPath` and `StreamingAssets` live outside the CWD |
| `World.Open(path)` read-only load of a baked store without the pipeline | Ship baked worlds; the player never regenerates |
| `World.GetRegion/GetLocal` take `RegionDetailOptions` | Editor exposes region detail knobs |
| Determinism batch (review #1, #2, #11) | Precondition for §7 |

## 6. Unity layer (`com.rogueliketoolkit.world.unity`)

### 6.1 Runtime

- **`WorldAsset : ScriptableObject`** holds the generation recipe:
  - seed, size, plates and `PlanetParameters`;
  - stage toggles and knobs, exposed as a serializable mirror of the stage settings;
  - plugin stages via `[SerializeReference]`, plus a list of assemblies for `DiscoverFrom`;
  - name and peoples data overrides as `TextAsset`s;
  - history settings.

  `ComputeHash()` keys every cache.
- **`PlanetGenerator`** (static service):
  - `Task<World> GenerateAsync(WorldAsset, IProgress, CancellationToken)` on a worker thread
    (`Task.Run`).
  - `IEnumerator GenerateCoroutine(...)` via the step runner on WebGL.
  - Results come back to the main thread through the `SynchronizationContext`.
- **`PlanetView : MonoBehaviour`**, the globe:
  - Builds the `Mesh` with **`Mesh.AllocateWritableMeshData`** filled from a worker thread, which is
    near zero-copy from `GlobeMeshBuilder` spans.
  - Uses `IndexFormat.UInt32`. Size 7 is ~983k vertices; above size 6, offer chunking into
    icosahedron-face sub-meshes for culling.
  - Properties: `Layer`, `Relief`, `ShowHexBorders`, `ShowRivers`, `ShowPlates`, `SelectedTile`, and
    `WeatherTime` (when weather exists).
  - Per-tile colours go through the data texture, and selection/hover through a shader uniform
    (`_SelectedTile`). The flat-map highlight bug can't recur because the comparison is on tile id,
    not position.
- **`FlatMapView`**: a quad or `RawImage` with a `Texture2D` painted by `FlatMapRasterizer`, in
  Equirect, Mercator or Gnomonic. It keeps the cached `TileIndexRaster`, so a layer switch is a
  `SetPixelData` + `Apply`.
- **`HexMapView`**: region/local maps as a hex-prism mesh, with an option for a `Tilemap` with a
  Hexagonal grid. It uses `FlatHexLayout` coordinates, for 2D roguelike use.
- **`TerrainView`**: region/local into a Unity `Terrain`, using `HeightmapBaker` plus splat maps from
  biomes (one alpha layer per biome family, with `TerrainLayer`s supplied by a `BiomeTerrainSet`
  asset).
- **`PlanetPicker`**:
  - No physics colliders on million-triangle meshes. It casts the camera ray, then
    `HexPicker.TryPick` against the right surface: sphere, displaced sphere or projection plane.
  - Raises `UnityEvent<TileHit>` `Hovered`/`Clicked` with tile index, `GeoCoord`, biome, elevation,
    climate and history owner.
  - Works with the new Input System and the legacy one.
- **`OrbitCameraRig`**: orbit, zoom and pan for the globe; pan and zoom for flat maps; a smooth
  "fly to tile".
- **`DrillDownController`**: click a planet tile → `RegionHandle` → `HexMapView`/`TerrainView`;
  click a region cell → local. A breadcrumb stack, plus an event so the game can intercept.
- **Overlays**:
  - rivers as a ribbon mesh from `RiverCatalog` with width by discharge;
  - plate boundaries, roads and realm borders (history) as line meshes;
  - labels for places and realms, using TextMeshPro, with the editor's label placement logic as a
    later port.
- **Shaders**:
  - HLSL for URP and Built-in, plus Shader Graph custom-function nodes (`HexFill`, `TileColor`) so
    users can build their own looks.
  - An ocean shader that reads depth from elevation.
  - An optional atmosphere shell (a single-scattering approximation).
  - A **cloud shell** driven by a weather snapshot texture (W5), and storm sprites or vortices placed
    from `StormsAt(t)`.

### 6.2 Editor

- **`WorldAsset` inspector**: the parameter UI, plus an inline **live preview** built on
  `PreviewRenderUtility` (orbit by drag, layer dropdown).
  - Changes debounce for 250 ms, then cancel the in-flight build and start a new one.
  - Preview size is capped (default 4, max 5) independently of the asset's bake size, so tweaking
    stays at ~0.15–0.4 s per rebuild (measured gen times in `docs/PLANET_SCALE_BACKLOG.md`).
  - Shows stats: tile count, biome histogram, river and lake counts, generation time per stage from
    the progress events.
- **`World Generator` window** (UI Toolkit):
  - side-by-side globe and flat-map previews, with a seed-browser strip of thumbnails for seed ±N
    (batch-generated at size 3);
  - a history panel (polity timeline from `HistorySummary`);
  - a weather panel (month or time scrubber, storm-track overlay, a hurricane season list).
- **Bake** writes into a chosen folder:
  - the `World` store file, `.rtw`, copied into `StreamingAssets`;
  - textures as PNG (albedo per layer) and EXR (height, normal), equirect or cubemap;
  - globe and hex-prism meshes as `.asset`;
  - region heightmaps as RAW16;
  - a `BakedWorld` ScriptableObject that references them all.

  Runtime then loads `BakedWorld` with no generation.
- **Scene gizmos**: hovered tile outline, tile info in the Scene view toolbar overlay, and plate
  vectors.
- **Plugin authoring helpers**: a "New Generation Stage" script template carrying the
  `[WorldGeneratorStage]`, `[Preserve]` and contract attributes, and a validation button that runs
  `ValidateContracts()` and shows the results inline.

### 6.3 Public API sketch

```csharp
// Runtime, game code
World world = await PlanetGenerator.GenerateAsync(worldAsset, progress, destroyCancellationToken);
planetView.Bind(world);
planetView.Layer = MapLayer.Biome;
picker.Clicked.AddListener(hit => drillDown.Open(hit.TileIndex));

// Baking APIs (usable at runtime too)
Texture2D albedo = MapBaker.Flat(world, ProjectionKind.Equirectangular, 4096, 2048, MapLayer.Relief);
Cubemap   globe  = MapBaker.Cube(world, 1024, MapLayer.Biome);
Mesh      mesh   = MeshBaker.Globe(world, new GlobeMeshOptions { Relief = 0.08f, FlatHexFill = true });
TerrainData td   = TerrainBaker.Region(world.GetRegion(tile), resolution: 513, biomeSet);

// Live weather (after WEATHER_PLAN W5)
planetView.WeatherTime = world.Weather.Calendar.ToTime(year, day, hour);
```

## 7. Determinism and testing

- **Unity test package** (`Tests/Runtime`, `Tests/Editor`) running in the Unity Test Framework:
  - The world checksum for the AotSmoke seeds must equal the value `tests/RoguelikeToolkit.World.AotSmoke`
    asserts.
  - Run it in Edit Mode (Mono) and in an IL2CPP player build (PlayMode, standalone). This closes the
    README's "cross-runtime equality on Mono/IL2CPP is not verified".
  - Picking round-trip: tile → project → ray → pick == tile, for the sphere, terrain and every
    projection.
  - Mesh builder invariants: vertex count, no NaN, winding outward after the coordinate conversion.
- **CI**: game-ci `unity-test-runner` needs a Unity license secret. Gate it behind the
  `UNITY_LICENSE` repo secret, and keep the engine-agnostic builders tested in the normal `dotnet
  test`, so most coverage needs no Unity at all.
- **WebGL smoke**: the step runner completes a size-4 world. Jint stages fall back to statement-count
  budgets only (the timeout can't fire on a single thread); document it and warn in the inspector.

## 8. Phases

| Phase | Content | Acceptance |
|---|---|---|
| **U0 Prereqs** | Review #6 (picking), #10 (leak), determinism batch. Core progress, cancellation and step runner; `ResolveStorePath` option; `World.Open`. | Tests: cancellation mid-erosion leaves no leaked store. Progress is monotonic and per stage. Step-runner output ≡ `Build()` output (checksum). |
| **U1 Presentation extraction** | Presentation on `netstandard2.1`. `GlobeMeshBuilder`, `TileColorizer`, `FlatMapRasterizer` (ported from `rasterize.ts`), and seam/pole handling, moved out of `GlControl`. The App switches to them (that is the regression test: the App must look identical). | Presentation tests pass on both TFMs. App screenshots unchanged (contact-sheet diff). Raster paint at 4096×2048 under 30 ms after the index is built. |
| **U2 Package skeleton** | `com.rogueliketoolkit.world.presentation` packing in `pack.py`; `com.rogueliketoolkit.world.unity` with asmdefs, `WorldAsset`, `PlanetGenerator` (async + coroutine), and a sample scene. | Fresh Unity 2022.3 and 6.x project: install through OpenUPM, open the sample, it generates and logs a checksum equal to AotSmoke. |
| **U3 Globe + picking** | `PlanetView` (MeshData, data-texture colours, hex fill shader for URP and Built-in), `PlanetPicker`, `OrbitCameraRig`, river ribbons, plate lines. | Size 6 globe at 60 fps on a mid-range GPU. Layer switch under 16 ms. Picking test round-trips on all surfaces. |
| **U4 Flat maps + drill-down** | `FlatMapView`, `HexMapView`, `TerrainView` + `BiomeTerrainSet`, `DrillDownController`. | Planet → region → local works end-to-end in the sample. A region heightmap at 513² bakes in under 100 ms. |
| **U5 Editor tooling** | Inspector live preview, World Generator window, seed browser, bake pipeline into `BakedWorld`. | Slider drag never blocks the editor UI: stale builds are cancelled, and preview latency ≤0.5 s at size 4. A baked world loads in the player with zero generation calls. |
| **U6 Weather visualisation** | Cloud shell, precipitation tint, storm icons and tracks, time scrubber. Depends on WEATHER_PLAN W3–W5. | A hurricane season replays in the editor window. Scrubbing is deterministic (the same t always shows the same frame). |
| **U7 Determinism CI + docs** | The Unity test package in CI (when licensed); README Unity section rewrite (including the stale `0.3.0` version pin); a samples gallery. | IL2CPP checksum equality is green, or the README states clearly which runtime and platform pairs are verified. |

## 9. Open questions (need a decision before U2)

1. **Minimum Unity version.** Stay at 2022.3 (the README floor), or move to Unity 6? Unity 6 has a
   better `MeshData` and UI Toolkit runtime bindings. Recommendation: build against 2022.3 and test
   on both.
2. **Render pipelines in v1.** Recommendation: URP plus Built-in. Leave HDRP to users through the
   Shader Graph custom-function nodes.
3. **Source vs DLL for the Unity layer.** Recommendation: source (see §3).
4. **Licensing.** The dual PolyForm-NC/commercial licence applies to the new packages too. The Asset
   Store would need its own EULA path if that channel is wanted.
