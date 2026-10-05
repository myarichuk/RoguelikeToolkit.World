# Groomed backlog: planet scale, map modes, benchmarks, relief, glaciers

Research date: 2026-09-28. All measurements: Release, .NET 10 SDK 10.0.301,
`WorldBuilder` default pipeline, Earth radius 6371 km. Probes were scratch
projects under `%TEMP%` (not committed); method is reproducible from this doc.

## Verdicts (one line each)

| # | Question | Verdict |
|---|----------|---------|
| A | 20k hexes not enough planet-wise? | True for direct play, but the region/local hierarchy already answers it (16,384x lazy magnification). Don't chase planet density; invest in the hierarchy. |
| B | Hex / "plain" / 3D map modes + lon/lat tracking? | Partly exists (Sphere + 3 flat projections + Hex/Terrain3D views; `GeoCoord` at planet tier). Missing: per-cell lon/lat below planet tier, and any region/local viewer. |
| C | BenchmarkDotNet stress tests? | Nothing exists. Recommended: new benchmarks project + xunit scale smoke tests. |
| D | Shaders show only mountains, not depressions? | Confirmed. Terrain normals are radial (relief invisible to lighting), displacement is ~10:1 mountains-vs-pits, Elevation hillshade is one-sided (pits unshaded). |
| E | Mountains almost equal glacier — climate bug? | Numerically no (measured overlap 0–35%), but YES there are two real defects underneath: (1) a hydro-zero bug that kills dry interiors, (2) a snowline/tundra threshold overlap that extincts Tundra (0–1 tiles in 8 builds). Plus a visual confound (snow ≈ ice white). |

## Scale reference (measured + derived)

Tiles = 10·4^size + 2. Hex "across" ≈ 2·√(area/π). Region = ÷8, local tile = ÷128.

| Size | Tiles | Gen time (meas.) | Hex across | Region cell | Local tile |
|------|-------|------------------|------------|-------------|------------|
| 3 | 642 | ~0.1–0.3 s | ~1000 km | ~126 km | ~8 km |
| 4 (viz default) | 2,562 | ~0.15 s | ~500 km | ~63 km | ~4 km |
| 5 | 10,242 | ~0.3–0.4 s | ~250 km | ~31 km | ~2 km |
| "20k" | — | — | ~180 km | — | — (not addressable: sizes jump 10k → 41k) |
| 6 | 40,962 | ~0.5–1.1 s | ~126 km | ~16 km | ~1 km |
| 7 | 163,842 | ~5 s, 23 MB managed | ~63 km | ~8 km | ~0.5 km |
| 8 (extrapolated) | 655,362 | ~20–30 s est. | ~31 km | ~4 km | ~0.25 km |

Region+local derivation: ~21 ms (lazy, storageless — you pay per zoom, not per planet).

Reading: planet tier is strategic (continents/kingdoms) at any feasible size;
region is operational (provinces); local reaches 1–4 km overworld hexes at
sizes 4–6 — good for travel. Sub-km tactical scale stays game-owned
(injectors + tactical maps), per the architecture's storageless-derivation
doctrine. Even size 8 planet hexes (~31 km) can't carry roguelike tactics,
so planet density has steeply diminishing returns past size 6–7.

---

## Epic A — Planet-scale strategy (answer to "20k not enough")

**Decision:** cap the planet tier at size 6–7 (benchmarks decide, see C1);
do not add fractional sizes (icosphere subdivision is 4x steps by
construction, [WorldDataStore.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.Core/WorldDataStore.cs:61)).
Gameplay density comes from the hierarchy, not the planet grid.

- **A1 — Document the scale contract (S).** Add the table above (condensed)
  to `docs/ARCHITECTURE.md` with the tier→play-use mapping
  (strategic/operational/travel/tactical-game-owned). Acceptance: a new
  contributor can answer "which tier do I play on?" without reading code.
- **A2 — Set the supported size ceiling (S, blocked by C1).** Declare max
  supported size (proposed: 7 interactive, 8 batch) based on benchmark
  numbers; `WorldBuilder.WithSize` throws `ArgumentOutOfRangeException`
  beyond it. Acceptance: sizes 0..ceiling build in tests; ceiling+1 throws
  with a message naming the ceiling.
- **A3 — Visualizer guard for large worlds (S).** Size 8 mesh ≈ 3.9M verts
  (~190 MB GPU buffers); warn/confirm above size 6 in `GlControl`.
  Acceptance: slider past 6 shows time/memory warning; no silent freeze.

## Epic B — Map modes + lon/lat tracking

What exists: `GlControl` projection modes (Sphere/Equirectangular/Mercator/
Gnomonic) × color modes × Hex/Terrain views; `GeoCoord` centers cached per
planet tile; `MapBounds.Center/RadiusKm` per derived map; implicit
cell→offset math inside `RegionMaps.DeriveChildContext`.

- **B1 — Per-cell lon/lat API (M).** Add `RegionHandle.CellCenter(int) →
  GeoCoord` and `LocalMapHandle.TileCenter(int) → GeoCoord` (pure functions
  of bounds + `CellUV`, same projection math as derivation), plus the
  inverse (cell containing a `GeoCoord`). Acceptance: round-trip
  center→cell→center within half a cell diagonal; determinism tests;
  `World.GetTileFeatures` works with a region-cell coord.
- **B2 — Region/local "plain map" viewer (M).** Visualizer mode rendering
  the selected planet hex's region grid (and a region cell's local grid)
  as a flat hex map with the existing palettes + B1 coordinates in the
  inspector. This is the missing "plain map mode" — there is currently no
  way to SEE derived maps. Acceptance: click planet hex → region view;
  click region cell → local view; back-navigation; colors match planet.
- **B3 — Flat-arena generation spike (M/L, design first).** Scope what a
  non-spherical standalone scenario map needs (`IHexMap` over `OffsetGrid`
  already exists, but derivation is sphere-anchored via `ParentContext`/
  `Vector3D`). Acceptance: design note + estimate; no code until B1/B2 land.
  Superseded by `SURFACE_MODES_PLAN.md` (Map2D surface mode).

## Epic C — BenchmarkDotNet + scale smoke tests

Nothing exists today (`docs/ARCHITECTURE.md` literally says "no benchmarks
were harmed"). CI runs `dotnet test` only.

- **C1 — Benchmarks project (M).** New
  `tests/RoguelikeToolkit.World.Benchmarks` (net10.0, BenchmarkDotNet,
  `MemoryDiagnoser`): `FullBuild` sizes 3–7, per-stage `Execute`,
  `GetTileIndex` vs `GetTileIndexExact`, `GetAdjacent`, `DeriveRegionMap`/
  `DeriveLocalMap`, `SpatialIndex` build + `QueryRadius`/`NearestRiver`/
  `ScoreCitySites`. Acceptance: `dotnet run -c Release` from the project
  root produces a full report; README documents the baseline workflow
  (`results/` gitignored, numbers pasted into perf PRs).
- **C2 — CI wiring (S).** CI builds the benchmarks project and runs one
  smoke benchmark (short job) to prevent bitrot; full suites are local-only
  (CI timing is too noisy for regression gates). Acceptance: benchmark
  breakage fails CI; normal CI time grows < 2 min.
- **C3 — xunit scale smoke tests (S).** Size 6 full build + determinism in
  the normal suite (measured < 1.1 s); size 7 behind `[Trait("Category",
  "Stress")]`, run nightly (measured ~5 s). Acceptance: both assert
  generation success + pipeline determinism, not timings (no flaky
  wall-clock asserts).

## Epic D — Relief: show depressions, not only mountains

Confirmed causes (all CPU-side today, so fixes stay GLES-1.00-safe for the
ANGLE backend — no shader-language upgrade needed):

1. `GlControl.SetupMesh` writes **radial normals** even in Terrain relief
   mode ([GlControl.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.App/GlControl.cs:764)) —
   displacement is invisible to lighting; relief reads only via silhouette.
2. `TerrainShading.DisplacedRadius` is ~10:1 asymmetric: land +0.16·h
   (up to +0.24) vs ocean −0.025·|h|, lake −0.02 max, river −0.004…−0.01
   ([TerrainShading.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.Presentation/TerrainShading.cs:23)).
3. `TileElevationColor` hillshade is **one-sided**: `drop = max(0, h−h_nbr)`
   is 0 for pit tiles, so valley floors get no shading
   ([GlControl.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.App/GlControl.cs:405)).

- **D1 — True displaced normals (M).** After Terrain displacement, compute
  per-face normals from displaced positions (CPU, once per rebuild) and
  upload to `aNormal`. Acceptance: side-by-side screenshot shows valleys/
  crater walls shaded; unit test on the normal computation; no FPS
  regression (static VBO, same draw path).
- **D2 — Two-sided hillshade (S).** Elevation mode: brighten sun-facing
  up-slopes, darken pits (use signed relief vs neighbor mean, not
  one-sided drop). Acceptance: pit tile renders darker than its ring;
  peak brighter; existing palette tests still pass.
- **D3 — Depression exaggeration control (S).** Separate relief scale for
  below-surroundings displacement + bathymetry parity option (ocean scale
  0.025 → match land, toggleable). Acceptance: slider/checkbox in
  visualizer; default unchanged.
- **D4 — Depression color cue (S).** Below-neighbor-mean land band (umber
  shadow) distinct from the `Canyon` biome color; keep `Canyon`
  (0.62,0.34,0.18) for cataloged reaches. Acceptance: overdeepenings read
  without the Elevation color mode.

## Epic E — Glacier/climate defects (the "mountains = glacier" report)

Measured biome overlap (8 builds, sizes 3–6, seeds 42/7): P(glacier|h>0.5)
0–35%, P(h>0.5|glacier) 0–20%. Glaciers are mostly polar *lowland* — the
mask is not "mountains". But the report points at three real problems:

**E0 — Root cause #1 (bug, confirmed): hydro-zero moisture bonus.**
`ClimateStage` runs (order 16) before `HydrologyStage` (order 18), but
`WorldBuilder`/`GlControl` *register* the hydro layer up front, so at
climate time the span is all zeros and `hydro[i].WaterBodyId >= 0` is true
for **every** tile ([ClimateStage.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.Core/ClimateStage.cs:76)).
Every inland tile wrongly gets the +0.06 water-proximity bonus; the lake/
sea moisture signal is dead. Probe (size 3, seed 42, tectonics+elevation+
climate only): hydro-registered-but-unrun mean 0.5938/min 0.2248/**0**
tiles < 0.20 vs hydro-unregistered 0.5832/0.1808/2 tiles < 0.20. Small
global delta (~0.01) but concentrated on exactly the inland tiles that
should form dry interiors.

**E0 — Root cause #2 (design tension, confirmed): tundra band squeezed out.**
Glacier snowline `0.15 + 0.11·p` (up to 0.26) sits *above* the Tundra
threshold `temp < 0.18`, and the dry-polar gate needs `p < 0.20` while
measured global precip minima are 0.17–0.24 ([Glaciology.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.Core/Glaciology.cs:58),
[LocalMapGenerationStage.cs](/C:/projects/RoguelikeToolkit.World/src/RoguelikeToolkit.World.Core/LocalMapGenerationStage.cs:69)).
Result: **Tundra 0–1 tiles in all 8 full builds**; cold land is glacier or
nothing. Deserts are suppressed by the same dry-tail compression (4–183
tiles vs thousands of forests/jungles).

**E0 — Visual confound (confirmed by reading):** Terrain snow
(0.90,0.92,0.95 above snowline, always above h 0.80) ≈ glacier ice
(0.80,0.89,0.95); Biome-view Tundra (0.80,0.85,0.90) ≈ Glacier
(0.88,0.93,0.97). Snow-capped mountains *look* like glaciers even where
the mask differs.

**E0 — Phase 0 verdict (2026-09-28): rain shadow YES; "hectic" = data
noise (primary) + palette gain (amplifier).** `OrographicEffect` test
passes; 6-build matrix (sizes 3–5 × seeds 42/7) all leeward < windward,
Δ 0.04–0.19. Hectic: land-neighbor mean|ΔP| ≈ 0.106 (~25% of pairs jump
> 0.15, max ≈ 0.64) from the orographic ×1.6/÷1.65 ridge discontinuity
+ 4-octave Fbm moisture noise reaching tile scale + zero smoothing
(biomes get 2 majority-vote passes; a 1-pass box blur cuts mean|ΔP| to
0.040). `ClimatePalette` is continuous (no banding) but dG/dP = 0.9
renders every data jump as green speckle; mesh vertices carry raw
per-tile values. Fix → E6 (below), sequenced before E2's retune.

- **E1 — Fix hydro-zero bonus (S, bug).** `ClimateStage`: treat an un-run
  hydro layer (all `Flow`/`Surface` zero — one O(n) scan) as absent and
  use the elevation fallback; document the climate↔hydrology ordering
  circularity. Acceptance: registered-but-unrun ≡ unregistered moisture
  field; existing `ClimateTests` + orographic test still pass;
  determinism preserved.
- **E2 — Restore the tundra band (M, blocked by E1).** Rebalance so dry
  polar interiors exist per the documented intent ("dry polar interiors
  staying tundra"): retune dry gate / snowTemp slope / tundra threshold
  against measured precip distribution; re-measure biome histogram over
  the seed matrix. Acceptance: Tundra > 0 in all matrix builds; Glacier
  confined to documented cases (wet cold + high caps); no desert collapse
  (Desert within 0.5–2x of pre-change on the same matrix, or justified).
- **E3 — Hydro-aware climate refresh (M, follow-up).** Optional second
  climate pass after hydrology so *lakes* feed moisture (today only ocean
  adjacency does, via the fallback). Acceptance: precip downwind of great
  lakes measurably higher; pipeline order documented; no determinism break.
- **E4 — Stale lapse-rate coupling (S/M, investigate).** Climate temps
  embed pre-erosion heights (order 16 < 17); erosion then lowers peaks,
  leaving temps too cold → excess high-altitude ice. Quantify post-E1/E2
  (compare glacier count with temps recomputed from post-erosion
  heights); cheapest fix is likely sea-level-temp + lazy lapse at query.
  Acceptance: measurement note + fix or wontfix-with-reason.
- **E5 — Disambiguate ice/snow/tundra + histogram guardrails (S).**
  Separate Terrain ice vs snow vs rock hues and Biome Tundra vs Glacier;
  add xunit biome-histogram invariants (Glacier/Tundra/Mountain bands on
  the seed matrix; glacier-before-mountain precedence documented in code).
  Acceptance: screenshot-distinguishable; tests fail if Tundra hits 0 again.
- **E6 — Smooth climate precip + soften orographic discontinuity (S/M,
  new from Phase 0, before E2).** 1–2 blur passes on precip (or an
  upwind-aware smoothing that preserves the rain shadow) and/or a
  continuous orographic factor; optionally lower palette precip gain.
  Acceptance: land-neighbor mean|ΔP| < ~0.05 on the Phase 0 matrix;
  orographic test still passes with Δ > 0; Climate mode reads as belts
  not speckle; determinism preserved.

## Suggested order

1. E1 (bug fix, unblocks E2) → 2. C1+C3 (baselines for everything else) →
3. E6+E2+E5 (smooth first — E2 retunes against the smoothed distribution) → 4. B1+B2 (lon/lat +
region viewer — the actual "plain map mode") → 5. D1+D2 (relief) →
6. A2+A3 (ceiling + guards, on C1 numbers) → 7. E3/E4/B3/D3/D4 as follow-ups.
