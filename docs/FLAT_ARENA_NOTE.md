# Flat-arena generation spike — design note (backlog B3, no code)

Goal: scope what a non-spherical standalone scenario map ("flat arena",
city-scale battlemap, island scenario) needs. Estimate only; no
implementation until a game client asks for it.

## What already exists (reuse as-is)

- `IHexMap<TTile>` over `OffsetGrid`: flat-top hex grids with axial/cube
  addressing. Region/local handles already expose this shape, so adjacency,
  ranges, and movement code transfer untouched.
- Injectors + `SiteCatalog` overlay, `IMaterializationStore` bindings,
  `SpatialIndex` queries: all topology-agnostic, work on any `IHexMap`.
- `CanyonAnalysis`, `Glaciology` (fallback overload), deposit prospecting:
  pure functions of (position, height, climate) — reusable wherever those
  fields come from.

## What is sphere-anchored (needs a flat counterpart)

- Derivation (`RegionMaps`): `ParentContext` carries `Vector3D` position,
  gradient/strike/flow; child math projects through tangent planes and
  `CellUV`. A flat arena needs a 2D analogue: address = (arena seed, rect
  bounds, cell), context = (mean height, strike angle, flow vector).
- Field noise: `SphereNoise.Fbm(Vector3D)` everywhere. Flat fields need a
  2D Fbm with the same seed-derivation discipline (world seed + salt per
  use) so arena maps stay deterministic.
- `GeoCoord` addresses, `MapBounds` (center + radiusKm), WKT export via
  tile vectors: keep the *shapes*, back them with a planar projection
  (meters, not degrees) for arenas.
- Pipeline stages assume a `WorldDataStore` (icosphere topology). Arena
  generation wants a subset: elevation-ish heightfield → water routing →
  moisture → biomes over an `OffsetGrid` store. `HydrologyStage` and
  `LocalMapGenerationStage` are the closest templates but index through
  store topology — port, don't reuse blindly.

## Suggested spike order

1. 2D deterministic noise + flat heightfield on `OffsetGrid` (S).
2. Water routing + moisture on the grid, reusing the hydro/biome gate
   *shapes* with arena-tuned thresholds (M — thresholds will differ;
   planet gates are calibrated to sphere distributions).
3. Arena address type + injector/materialization interop proof (S).

## Estimate

M/L total (roughly: S + M + S with threshold calibration dominating).
The calibration step is the long pole — planet biome gates took two
retune cycles against measured matrices; expect the same for arenas.
No code until B1/B2 consumers exist (they do now: per-cell coords and
the plain-map viewer transfer directly to arena grids).
