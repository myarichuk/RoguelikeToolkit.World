# Surface modes — implementation plan

Status: **plan, no code yet.** Supersedes `docs/FLAT_ARENA_NOTE.md` (backlog B3),
whose inventory is folded into [§9](#9-folded-flat-arena-note-b3).

## 1. Goal

One generation pipeline, one plugin contract, two **surface modes** chosen when
a world is built:

| Mode | Samples | Who instantiates it |
|---|---|---|
| `HexSphere` | icosphere tiles (today, sizes 0–7) | the visualizer, region/local hex derivation, everything that exists today |
| `Map2D` | a lat/lon rectangle rasterized at W×H | flat TTRPG-style maps (and the future ink/cartography renderer) |

```csharp
new WorldBuilder().WithSize(6)...Build();                                         // HexSphere, unchanged
new WorldBuilder().WithMap2D(new GeoRect(40, -5, 48, 8), width: 1024, height: 768)...BuildMap2D();
```

Geophysics stages, Jint scripts, and feature injectors are written **once**
against `GeoCoord` / `Vector3D` positions plus a neighbor graph, and run in
either mode. A world is one mode for its whole life; the two modes never have
to agree with each other.

## 2. Decisions (locked)

1. **Mode is per world, not per view.** No cross-mode consistency, no
   refinement windows, no second pipeline class.
2. **Coordinates are the universal identity.** Sample index `i` is a
   mode-local id: tile index in `HexSphere` (still public: visualizer,
   `MapAddress`, region/local derivation), pixel index in `Map2D`.
3. **`Map2D` is a rectangle on the real sphere**, not a fake plane. Every
   sample has a true `GeoCoord` and unit `Vector3D`. Consequences:
   - latitude-driven code (`ClimateStage.cs:75,82`, `Glaciology.cs:57,78,90`,
     Jint `lat`/`lon`) works unchanged; the chosen bounds *are* the climate;
   - `SphereNoise` on unit vectors, great-circle distances, tangent east/north
     frames all stay correct;
   - no `SurfaceFrame`/fake-latitude layer is needed.
4. **Precision is `double`.** `GeoCoord` already is (≈4 nm at the equator).
   No `decimal`. Optional `int32` fixed-point 1e-7° (≈1.1 cm) for stored
   feature geometry and RNG keys (Phase 8).
5. **Back-compat default:** compiled plugins that do not declare supported
   surfaces are `HexSphere`-only.
6. **The visualizer only ever instantiates `HexSphere`.** No App changes
   beyond following renamed/obsoleted store members.

## 3. Non-goals

- Fake planar worlds with invented latitude (rejected: decision 3).
- Whole-globe `Map2D` (equirectangular wrap + degenerate polar rows). The
  surface may allow longitude wrap later; nothing here depends on it.
- Hex-mode visuals changing in any way during Phases 1–3.
- Region/local hex derivation (`RegionMaps`, `ChildGridBuilder`) for `Map2D`.
  `Map2D` worlds are already high-resolution; zoom is "build a smaller rect".

## 4. Current state (what couples the pipeline to the icosphere)

The pipeline machinery is already topology-free: `WorldGenerationPipeline`
(ordering, `_clearCache`, read/write validation, discovery,
`PluginLoadContext`), `StageMetadata`, `StageContractValidator`,
`IDeclaredStage`, `ISeededStage`, and the layer half of `WorldDataStore`
(`RegisterLayer/Table`, `GetSpan`, `GetRef`, MMF) only need `TileCount`.

The coupling is in `WorldDataStore`'s topology half and in how stages consume it:

| Coupling | Where |
|---|---|
| `WorldDataStore(int size)` → `GetTileCount(size)` = `10·4^size+2`, throws for size > 7 | `WorldDataStore.cs:80-96` |
| Static `TopologiesBySize` cache keyed by `int size` (cap 16) | `WorldDataStore.cs:35,484-500` |
| Topology built from `IcosphereGenerator`; **faces discarded** after adjacency | `WorldDataStore.cs:501-594` |
| `GetTileIndex(GeoCoord)` lat/lon bucket index | `WorldDataStore.cs` (`GetTileIndex`, `GetTileIndexExact`) |
| MMF header v3 validates `size`; no surface descriptor | `WorldDataStore.cs:67,229-282` |
| `GetAdjacent(i, Span<int>)` **silently clips** at buffer length | `WorldDataStore.cs:478` |
| 33 stage/catalog call sites pass `stackalloc int[6]` to `GetAdjacent` | see §6 Phase 2 list |
| `GetTileVectors()` / `GetGeoCoord()` consumers in stages | Tectonics `:128,:376`; Elevation `:53`; Climate `:65`; Erosion `:150,:238`; Hydrology `:40`; LocalMap `:43`; Deposits `:117`; CitySites `:55,:123`; TerrainOrientation `:45`; HistoryHooks `:99`; FeatureCatalog `:126-165`; TileFeatures `:88-236`; SpatialIndex `:62-310`; World `:56-191`; StoreLayer `:20` |
| Tectonic seeds drawn over the whole sphere | `TectonicPlateGenerationStage.cs:97-117` |
| Jint `lat`/`lon` via `store.GetGeoCoord` | `JintWorldStage.cs:252-253` |
| Plugins receive the concrete `WorldMap` → concrete `WorldDataStore` | `IWorldGeneratorStage.cs` |

Note: once `Map2D` samples carry real unit vectors, most `GetTileVectors()`
consumers port **without logic changes** — they only stop assuming the samples
are icosphere vertices covering the whole sphere.

## 5. Target architecture

### 5.1 `ISurface`

```csharp
public enum SurfaceKind : byte { HexSphere = 1, Map2D = 2 }

[Flags]
public enum SurfaceKinds : byte { None = 0, HexSphere = 1, Map2D = 2, All = HexSphere | Map2D }

public interface ISurface
{
    SurfaceKind Kind { get; }
    int SampleCount { get; }
    int MaxDegree { get; }                         // HexSphere 6, Map2D 8 (D8)

    ReadOnlySpan<Vector3D> Positions { get; }      // unit vectors, both modes
    GeoCoord Coord(int i);
    int Locate(GeoCoord c);                        // nearest sample; -1 when outside bounds (Map2D)

    ReadOnlySpan<int> Neighbors(int i);            // CSR slice, zero-copy
    bool IsBoundary(int i);                        // HexSphere: always false
    double AreaKm2(int i);                         // HexSphere: uniform 4πR²/N by definition; Map2D ∝ cos(lat)
    double MeanSpacingKm { get; }                  // drives noise octave counts (Phase 5)

    SurfaceDescriptor Descriptor { get; }          // persisted in the file header (Phase 4)
}
```

- **`HexSphereSurface`**: today's `WorldTopology` moved behind the
  interface, plus retained faces (for later barycentric sampling).
  Cached process-wide by size exactly as now.
- **`LatLonRasterSurface`**: `GeoRect` bounds + `W×H`; sample `i = y·W + x`
  at pixel centers; 8-connected CSR (corners 3, edges 5, interior 8);
  `Locate` = `floor` in lat/lon; `AreaKm2` from the spherical rectangle
  area of the pixel. **Owned by the store** (not in the static cache: bounds
  are continuous, a static cache would leak one CSR per world).

### 5.2 Store

```csharp
public WorldDataStore(ISurface surface, string? filePath = null);
[Obsolete] public WorldDataStore(int size, string? filePath = null)   // → HexSphereSurface.ForSize(size)
public ISurface Surface { get; }
public int TileCount => Surface.SampleCount;
```

Forwarders kept for source compat (marked `[Obsolete]` in Phase 6, never
removed before 1.0): `GetGeoCoord` → `Surface.Coord`, `GetTileVectors` →
`Surface.Positions`, `GetTileIndex` → `Surface.Locate`, `GetAdjacent` →
copy from `Neighbors` (keeps clipping semantics, documented). `Size` stays
`HexSphere`-only and throws `NotSupportedException` on `Map2D`.

### 5.3 Pipeline and plugin contract

- `IWorldGeneratorStage.Execute(WorldMap)` is unchanged; `map.Surface`
  (alias of `map.DataStore.Surface`) is the new geometry entry point.
- `WorldGeneratorStageAttribute.Surfaces` (`SurfaceKinds`, default
  `HexSphere`); `IDeclaredStage` gains `SurfaceKinds Surfaces { get; }`
  via a default interface member returning `HexSphere` so existing
  implementers compile; `StageMetadata.GetSurfaces`.
- `Execute` rejects a stage whose `Surfaces` excludes `map.Surface.Kind`
  with `InvalidOperationException` naming the stage, before any layer is
  cleared. The order cache records the surface kind it was validated for;
  a different kind marks it dirty.
- `Discover(dir, onDiagnostic, SurfaceKind? only = null)` skips mismatches
  with a diagnostic when `only` is given.

### 5.4 Builder and façades

- `WorldBuilder.WithMap2D(GeoRect bounds, int width, int height)` +
  `BuildMap2D()` → `MapWorld`. `Build()` throws if `WithMap2D` was used, and
  vice versa (no mode-polymorphic return type).
- `MapWorld` mirrors the read API of `World` that is meaningful on a
  rectangle: `SampleElevation/SampleBiome(GeoCoord)`, `QueryRadius`,
  catalogs, `Nearest*`, WKT export, `ScoreCitySites`. It has **no**
  `GetRegion/GetLocal/Resolve(PlanetHex)`.
- Shared implementation lives in an internal base (`WorldCore`) so `World`
  and `MapWorld` do not fork query code.

### 5.5 Budgets

`Map2D` caps at `MaxMap2DSamples = 4_194_304` (2048²) initially; revisit after
Phase 5 benchmarks. Jint statement/memory budgets already scale with
`TileCount` and need no change.

## 6. Phases

Every phase is one or more PRs that ship green on CI
(`dotnet build`, `dotnet test --filter "Category!=Stress"`, benchmark smoke).
Phases 0–3 must not change any generated byte in `HexSphere` mode.

### Phase 0 — Golden baselines (S)

Existing `DeterminismTests` compare two runs of the *same* build; they cannot
catch a refactor that changes output deterministically.

- Add `GoldenOutputTests`: for seeds {7, 42, 1337} × sizes {3, 4, 5}, hash
  (xxHash64 over raw layer spans) every registered layer plus a canonical
  serialization of `RiverCatalog`, `WaterBodyCatalog`, `RangeCatalog`,
  `DepositCatalog`, and `ScoreCitySites()` top 32. Commit the hashes.
- A helper prints new hashes on mismatch so intentional changes (Phase 8)
  re-baseline in one commit.

Acceptance: test passes on `main`; a one-ULP perturbation in any stage fails it.

### Phase 1 — Extract `ISurface` / `HexSphereSurface` (M)

- New: `Surfaces/ISurface.cs`, `Surfaces/HexSphereSurface.cs`,
  `Surfaces/SurfaceKind.cs`.
- Move `WorldTopology` construction, bucket index, `GetTileIndex(Exact)`
  into `HexSphereSurface`; keep faces (`TriangleIndices[]`) on it.
- `WorldDataStore` gains `Surface`; old members forward. `WorldMap` gains
  `Surface`.
- No stage changes yet.

Acceptance: Phase 0 hashes identical; benchmark smoke within noise
(`GetTileIndex` must stay as fast: forwarding is one interface call; if it
shows, seal the class and call through the concrete type in the store).

### Phase 2 — `Neighbors(i)` everywhere (M, mechanical)

Replace `Span<int> x = stackalloc int[6]; int n = store.GetAdjacent(i, x);`
with `foreach (int j in surface.Neighbors(i))` / indexed span loops.

Call sites (33):
`ClimateStage.cs:118,179` · `ErosionGenerationStage.cs:73,113,181,310` ·
`HydrologyStage.cs:112,221,246,371,422,449,464` ·
`LocalMapGenerationStage.cs:128,191` ·
`TectonicPlateGenerationStage.cs:205,328,365` ·
`Hydrography.cs:25,139,191,218,241,256,438,476,555` ·
`PlatePartitioning.cs:189` · `Deposits.cs:133` · `TileFeatures.cs:214` ·
`CitySites.cs:98` · `TerrainOrientation.cs:43` · `MapBounds.cs:37`.

Not in scope: the grid-tier hex helpers (`GridProjection.GetHexAdjacent`,
`ChildGridBuilder`, `ChildContextDeriver`, `SettlementInjector`), which stay
hex-only, and `GlControl.cs:505,544` (visualizer, HexSphere only).

Neighbor **order** must be preserved (CSR order = current order) because
several loops break ties by first-seen; Phase 0 hashes enforce it.

Add `SurfaceContractTests`: for every surface implementation, neighbor
symmetry (`j ∈ N(i) ⇔ i ∈ N(j)`), no self-loops, `|N(i)| ≤ MaxDegree`, and a
reflection-free scan asserting no Core type still calls `GetAdjacent`
(grep-based test over `src/RoguelikeToolkit.World.Core/*.cs`).

Acceptance: Phase 0 identical; allocation benchmarks unchanged (span slice is
zero-alloc).

### Phase 3 — Surface declarations on plugins (S)

- `WorldGeneratorStageAttribute.Surfaces`, `IDeclaredStage.Surfaces`
  (default member), `StageMetadata.GetSurfaces`.
- Pipeline validation per §5.3, cached with the order cache.
- `JintStageSpec.Surfaces` (default `HexSphere`); `JintWorldStage` reports it.
- Built-in stages keep the default for now; they flip to `All` in Phase 5
  one by one as each is ported and tested.
- Docs: `docs/PLUGINS.md` section "Surface modes" + troubleshooting entry.

Tests: undeclared stage on a fake `Map2D` surface throws with the stage name;
`All` stage passes; cache invalidates when the same pipeline executes on a
different kind; Jint default rejection message.

Acceptance: Phase 0 identical.

### Phase 4 — `LatLonRasterSurface` + store/persistence (M)

- `GeoRect` record (`South, West, North, East`; validates `South < North`,
  no antimeridian crossing for now, `|lat| < 85`).
- `LatLonRasterSurface(GeoRect, int width, int height)`: positions, CSR,
  boundary flags, areas, `Locate`, `MeanSpacingKm`.
- `WorldDataStore(ISurface, path)`.
- **File format v4**: header gains a surface descriptor
  (`kind:u8`, HexSphere `size:i32`, Map2D `south,west,north,east:f64, w,h:i32`).
  v3 files open as `HexSphere` (read-compat); writing always emits v4.
  The raster is a pure function of the descriptor, so no CSR is persisted.
- `WorldBuilder.WithMap2D` / `BuildMap2D` → minimal `MapWorld` (field
  sampling + catalogs, no stages ported yet → builder only accepts
  `WithoutDefaultStages()` + custom `All` stages until Phase 5 lands).

Tests: raster `Locate(Coord(i)) == i` for all i; areas sum to the analytic
rect area within 1e-9 relative; boundary count `2W + 2H − 4`; v3 file opens;
v4 round-trips both kinds; descriptor mismatch on open throws.

### Phase 5 — Port the geophysics stages to bounded surfaces (L)

Each stage flips to `Surfaces = All` in its own PR with a `Map2D` test.

| Stage | Change for bounded surfaces | Size |
|---|---|---|
| Tectonics (10) | Seeds: draw `SeedCount` seeds in the bounds expanded by ~1 plate radius; samples take the nearest seed (plates may extend past the edge). Drift via tangent at position (already). Boundary detection ignores edges. `PlatePartitioning` flood-fill must start from in-bounds proxy samples for out-of-bounds seeds. | M |
| Elevation (15) | Noise octaves from `MeanSpacingKm`: add octaves until the finest wavelength ≈ 2× spacing (HexSphere: unchanged count by construction). | S |
| Climate (16) | Latitude works as is. Moisture advection: boundary samples on the upwind side get inflow moisture from a configurable `BoundaryMoisture` (default: ocean-like if the upwind neighbor would be ocean by elevation noise, else continental mean). | M |
| Erosion (17) | Slopes use great-circle `DistanceKm(i,j)` instead of assuming uniform spacing (D8 diagonals are √2 longer). Boundary samples are fixed base level (no incision past the frame). | S |
| Hydrology (18) | Boundary samples are **outlets** (water leaves the map), not sinks; priority-flood seeds from ocean **and** boundary. Flow accumulation weights runoff by `AreaKm2`. Receiver choice uses slope (drop / distance), not raw drop, so D8 does not bias to diagonals. | M |
| Biomes (20) | No structural change. | S |
| Deposits, CitySites, Ranges | Neighbor loops (Phase 2) + no global assumptions; verify only. | S |

Hydrology/climate changes must be **no-ops on HexSphere**: `IsBoundary` is
always false, and `HexSphereSurface.AreaKm2` returns the uniform
`4πR²/N` by definition (real icosphere cells vary by a few percent, and
using true areas would silently re-baseline), applied as
`runoff × AreaKm2(i) / meanArea` so the factor is exactly 1. Phase 0 hashes
enforce this.

Map2D tests (per stage): deterministic; no NaN/∞; rivers reach a boundary or
the sea (no orphan sinks except lakes); flow direction histogram over 8
directions has no >1.5× diagonal bias on a tilted plane; a coastal rect at 45°N
yields temperate biomes, a rect at 5°N tropical ones.

Benchmarks: add `Map2DBuildBenchmarks` (512², 1024², 2048²).

### Phase 6 — Queries, catalogs and façade for Map2D (M)

- `WorldCore` internal base; `World` and `MapWorld` share it.
- `SpatialIndex`: abstract the bucket grid; HexSphere keeps lat/lon buckets,
  Map2D uses the raster itself (O(1) cell, ring search).
- `FeatureCatalog` WKT export via `Surface.Coord` (works unchanged after
  Phase 1; verify Map2D output in lon/lat order).
- `TileFeatures`, `HistoryHooks`, `StoreLayer`: move to `Surface` API.
- Mark the store forwarders `[Obsolete]` with messages pointing at
  `Surface`; fix all in-repo callers (App included, mechanical).

### Phase 7 — Calibration (M/L, the long pole)

Biome/hydro gates were tuned on planet-scale distributions (two retune cycles,
see `docs/PLANET_SCALE_TODO.md`). At 0.3–1 km spacing expect different flow
magnitudes, river density, and lake counts.

- Normalize flow thresholds by area (`km²` of catchment) instead of sample
  counts, so thresholds are resolution-independent in both modes. If this
  changes HexSphere output, it is a deliberate re-baseline (own PR).
- Measured matrices for 3 reference rects (temperate coast, arid interior,
  tropical archipelago) × 3 seeds; gates documented with the numbers, as
  Phase 2 of the planet work did.

### Phase 8 — Coordinate-native features and RNG (M, behavior-changing)

- Feature geometry stored as `GeoCoord` (optionally fixed-point int32
  1e-7°): `River.Path`, `WaterBody` rings, `Range` axes, `Deposit` points.
  Tile/pixel lists become derived via `Surface.Locate`.
- Placement randomness keyed by quantized coordinate
  (`Rng.Derive(seed, salt, latE7, lonE7)`) or by feature id, not by sample
  index, so the same place draws the same number in either mode or at any
  resolution.
- Planet-tier injectors emit coordinates; `PlacedSite.CellIndex` stays as a
  computed property for hex callers.
- **Re-baselines Phase 0 hashes** — isolated PR, changelog entry.

### Phase 9 — Cartography renderer (separate project, outline only)

`RoguelikeToolkit.World.Cartography` consumes `MapWorld` and emits SVG:
marching-squares coastlines with wobble + offset ripple strokes, river
polylines with flow-scaled banks, Poisson-disk glyph scatter by biome with an
occupancy grid, mountain glyph rows along the orogeny strike, y-sorted
painter's order, hatching for farmland, dashed roads; per-feature RNG streams.
Needs road/realm catalogs (`RoadCatalog`, `RealmCatalog`) — planned
separately. Not started before Phase 7.

## 7. Testing strategy (summary)

| Guard | Introduced | Protects |
|---|---|---|
| Golden layer + catalog hashes | P0 | bit-identical HexSphere through P1–P6 |
| Surface contract tests (symmetry, degree, Locate round-trip, areas) | P2/P4 | every `ISurface` implementation |
| "No `GetAdjacent` in Core" scan | P2 | neighbor-truncation regressions |
| Surface-kind validation tests | P3 | plugins running on the wrong mode |
| Per-stage Map2D sanity (NaN, outlets, diagonal bias, latitude climate) | P5 | bounded-surface physics |
| Map2D benchmarks | P5 | budget/cap decisions |

## 8. Risks and open questions

- **Interface-call overhead in hot loops.** `ISurface.Neighbors` is called
  per sample per pass. Mitigation: stages fetch the CSR arrays once
  (`surface.NeighborOffsets`, `surface.NeighborIndices` as
  `ReadOnlySpan<int>`) and index directly; `Neighbors(i)` is the convenience
  path. Decide in P2 with a benchmark.
- **Tie-break order on Map2D.** Several algorithms break ties by
  first-neighbor; raster CSR order must be fixed and documented (E, NE, N,
  NW, W, SW, S, SE) so outputs are platform-stable.
- **Out-of-bounds plates/catchments.** A rectangle is an open system;
  boundary rules (P5) are approximations. Acceptable for TTRPG maps; document.
- **Memory at 2048².** 4.2M samples × current per-tile layers (~60–80 B) ≈
  300 MB mapped plus CSR (~135 MB of `int`). Fine for batch, heavy for
  interactive; P5 benchmarks decide the default cap.
- **Open:** should `Map2D` permit antimeridian-crossing rects now? (Plan: no;
  `GeoRect` validates.)
- **Open:** does `MapWorld` need history/materialization (`QueryOptions`)
  from day one? (Plan: yes, by sharing `WorldCore`; cost is small.)

## 9. Folded: flat-arena note (B3)

The B3 note (previously `docs/FLAT_ARENA_NOTE.md`) scoped a *non-spherical*
arena: 2D noise, `OffsetGrid` store, planar addresses and projection. This
plan replaces that direction (decision 3: `Map2D` stays on the sphere), which
removes its "sphere-anchored, needs a flat counterpart" list entirely:
`SphereNoise.Fbm(Vector3D)`, `GeoCoord` addresses, `MapBounds`, and WKT all
work as-is on a lat/lon rectangle.

What carries over from B3 unchanged:

- **Reuse as-is:** injectors + `SiteCatalog`, `IMaterializationStore`
  bindings, `SpatialIndex` query shapes, `CanyonAnalysis`, `Glaciology`,
  deposit prospecting — pure functions of position/height/climate.
- **Port, don't reuse blindly:** `HydrologyStage` / `LocalMapGenerationStage`
  index through store topology — now addressed by Phases 2 and 5.
- **Calibration is the long pole** — planet biome gates took two retune
  cycles against measured matrices; expect the same (Phase 7).
- **Determinism discipline:** world seed + salt per use, never iteration
  order — unchanged, extended to coordinate keys in Phase 8.

Region/local hex grids (`IHexMap`, `OffsetGrid`, `FlatHexLayout`/`PlainMap`)
remain the HexSphere zoom path and are out of scope here.

## 10. Estimate

| Phase | Size | Behavior change |
|---|---|---|
| 0 Golden baselines | S | none |
| 1 `ISurface` extraction | M | none |
| 2 `Neighbors(i)` migration | M | none |
| 3 Surface declarations | S | none (new validation) |
| 4 Raster surface + v4 file | M | file format v4 (v3 read-compat) |
| 5 Stage ports | L | none on HexSphere |
| 6 Queries/façade | M | obsoletions |
| 7 Calibration | M/L | possibly HexSphere re-baseline (isolated) |
| 8 Coordinate features/RNG | M | HexSphere re-baseline (isolated) |
| 9 Cartography | L | new project |

Phases 0–3 are worth doing even if `Map2D` never ships: they pin output,
shrink the accidental plugin surface, and remove the latent neighbor-clipping
hazard.
