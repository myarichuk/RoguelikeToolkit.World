# API Guide - `RoguelikeToolkit.World.Core`

This is the full tour of the library's public API: what each piece does, when you'd reach for it, and copy-pasteable examples. It assumes you've skimmed the README at least once. Nothing here is corporate gospel - just how the pieces fit together and what they're good for in an actual game.

Contents:

- [Quick start](#quick-start-build-a-world-in-four-lines)
- [The planet tier](#the-planet-tier-build-sample-query)
- [Addresses and seeds](#addresses-and-seeds-where-am-i)
- [Zooming in: regions and local maps](#zooming-in-regions-and-local-maps)
- [Why zoomed maps look like their parent](#why-zoomed-maps-look-like-their-parent)
- [Sites: ruins, cities, mines, landmarks](#sites-ruins-cities-mines-landmarks)
- [Finding stuff fast](#finding-stuff-fast)
- [Persistence: overrides and location bindings](#persistence-overrides-and-location-bindings)
- [Custom generation stages and history](#custom-generation-stages-and-history)

## Quick start: build a world in four lines

```csharp
using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Core.Primitives;

using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();

var coord = new GeoCoord(48.2, 16.4);
float height = world.SampleElevation(coord);
var (biome, danger) = world.SampleBiome(coord);
```

`World` is the facade you'll use 95% of the time. It owns the generated `WorldMap` plus the sparse feature catalogs (rivers, water bodies, ranges, deposits). `WorldBuilder` runs the default generation pipeline - tectonics -> elevation -> climate -> erosion -> hydrology -> biomes - and wires up the catalogs. `WithSize` is the icosphere subdivision level (tile count is `10 * 4^size + 2`, so size 3 is 642 tiles); `WithSeed` makes the whole thing reproducible.

### Where the types live

`RoguelikeToolkit.World.Core` itself holds only the facade (`World`, `WorldBuilder`, `TileHit`, `PlanetParameters`,
`IInsolationModel`). Everything else sits in a namespace that matches its folder, so the snippets below add the
`using` for whichever slice they touch:

| Namespace (`RoguelikeToolkit.World.Core.`) | What you find there |
|---|---|
| `Primitives` | `GeoCoord`, `Vector2D`/`Vector3D`, `Rng`, `DetMath` |
| `Topology` | `WorldMap`, `IHexMap`, `CubeCoord`, icosphere, addresses (`MapAddress`, `MapTier`) and `MapBounds` |
| `Queries` | `SpatialIndex`, `FeatureKind` |
| `Storage` | `WorldDataStore`, `IMapLayer`, `StoreLayer`, user-edit layer |
| `Generation` | `WorldGenerationPipeline`, `IWorldGeneratorStage`, `[WorldGeneratorStage]` |
| `Tectonics` / `Elevation` / `Climate` / `Hydrology` / `Biomes` / `Lithosphere` / `Settlement` | one domain slice each: layer struct, algorithm, stage, catalog |
| `Features` | sparse per-tile features (`TileFeatureInfo`, WKT) |
| `Hierarchy` | planet -> region -> local derivation (`RegionHandle`, `LocalMapHandle`, `RegionMaps`) |
| `Sites` | injector pipeline and the built-in injectors |
| `Integration` | game-side seams (`IHistoricalContext`, `QueryOptions`, materialization) |
| `Entities` / `Names` / `Localization` | unchanged from 1.x |

## The planet tier: build, sample, query

Sampling answers "what's at this spot":

```csharp
// Height. Negative = below sea level.
float h = world.SampleElevation(coord);

// Biome + danger level (0 = harmless stroll, higher = bring friends).
var (biome, danger) = world.SampleBiome(coord);
```

Radius and nearest queries answer "what's around here":

```csharp
// Every tile within 500 km, sorted nearest-first.
List<TileHit> nearby = world.QueryRadius(coord, 500.0);
foreach (var hit in nearby)
    Console.WriteLine($"{hit.Biome} at {hit.DistanceKm:F0} km (tile {hit.TileIndex})");

// Nearest open water, river reach, or copper deposit.
var water = world.NearestWater(coord);     // (TileIndex, DistanceKm)?
var river = world.NearestRiver(coord);
var copper = world.NearestDeposit(coord, DepositType.Copper);
```

All the `Nearest*` methods run on a spatial index (`world.Index`), so they're cheap even on big worlds - no full-map scan per call. More on that in [Finding stuff fast](#finding-stuff-fast).

Two higher-level helpers worth knowing early:

```csharp
// "Where should I put cities?" Returns the TopN best sites with reasons.
var sites = world.ScoreCitySites(new CitySiteFilter { TopN = 10 });
foreach (var s in sites)
    Console.WriteLine($"tile {s.TileIndex}: score {s.Score:F2} ({s.Reasons})");

// "Everything on this one hex" - biome, river reach with upstream/downstream
// neighbors, water body, ranges, glacier cover, deposits, placed sites.
TileFeatureInfo info = world.GetTileFeatures(coord);
```

And if you need to talk to editors or external tools, features export to WKT (storage stays as tile-id lists; WKT is produced on demand at the boundary):

```csharp
string riverWkt = world.RiverToWkt(0);
string lakeWkt = world.WaterBodyToWkt(0);
string depositWkt = world.DepositToWkt(0);
```

## The planet: tilt, sun and spin

Climate is a function of three astronomical numbers, all defaulting to Earth. An Earth-like set takes the
exact legacy arithmetic, so existing worlds are unchanged bit for bit.

```csharp
var world = new WorldBuilder().WithSize(5).WithSeed(42)
    .WithPlanet(new PlanetParameters { ObliquityDegrees = 60, StellarFlux = 0.95, RotationRate = 1.5 })
    .Build();                                                   // world.Planet reports what it was built with
```

| Parameter | Range | Effect |
|---|---|---|
| `ObliquityDegrees` | 0..90 (Earth 23.44) | Latitude distribution of annual-mean insolation (S(x) = 1 + S2·P2(x), S2 = -(5/16)(2 - 3 sin²ε)) and the size of the seasons. 0° freezes the high latitudes; 90° makes the poles out-heat the equator. |
| `StellarFlux` | 0.3..2.5 (Earth 1) | Raises or lowers every temperature (2.5 temperature units per unit of flux; ±10% is a ±0.25 shift, so useful worlds live in roughly 0.85-1.15). |
| `RotationRate` | 0.25..4 (Earth 1) | Circulation cells span a latitude ∝ 1/rotation (Held & Hou): the dry belt, storm track and polar desert move with it, and the prevailing wind alternates every 30°/rotation instead of every 30°. |
| `Insolation` | `IInsolationModel` | Replaces the tilt-driven pattern, e.g. `TidallyLockedInsolation` for a world with a permanent day side. |

Deviations are *differences against Earth*, so the biome thresholds calibrated on Earth's 0..1 temperature field
stay valid. The redistribution of heat between latitudes (a different tilt) is applied at 1.0 per unit of
insolation, the change of the mean (flux) at 2.5: winds and currents flatten the former, not the latter.
All temperature now comes from one function, `ClimateModel.Temperature`.

`ClimateInfo` also carries `SeasonalRange` (half the annual temperature swing, larger inland and towards the poles)
and `RainSeasonality` (0 even .. 1 monsoon or Mediterranean). They are data for hosts and the inspector; the biome
rules do not read them yet.

**Balanced plates** are on by default (`WithLegacyPlates()` or `WithBalancedPlates(false)` restores the previous worlds
bit for bit). The earlier generator drew plate seeds with uniform latitude (28% of them above 60 degrees, against 13% for a
uniform sphere), let growth speed vary 0.6 to 2.0 (plate area scales with speed to the power 2.4, so plates ranged from
under 1% to over 25% of the globe), and built the crust field mostly from each plate's own offset (weight 1.2 against 0.55
for the noise), so most plates were entirely land or entirely sea and 71% of coastline tiles sat on a plate border. The
preset spreads seeds evenly with a minimum spacing, bounds the speed spread to 0.85-1.35, lowers the plate offset to 0.6,
displaces and blurs the crust field before thresholding (`CrustWarp`, `CrustBlurPasses`), sets the continental share by
quantile (`LandFraction`) and restores the field's spread (`CrustContrast` for the land side, `OceanContrast` for the sea
floor) so relief matches the legacy worlds and volcanic arcs on oceanic crust stay under water. It also deals continental
crust out to the heaviest set of plates that do not touch (`DisperseContinents`): independent offsets left continental plates
adjacent, and adjacent continental plates fuse, so the largest landmass held 79% of the land (96% on some seeds) at size 6.
Over 15 seeds at size 6 the largest landmass now holds 46% (never above 71%) with about three landmasses of 5% or more,
the largest plate is 2.7 times the smallest (was 65), plates that are all land or all sea fall from 69% to 32%, coastline
tiles on a border from 71% to about 50%, and land is 31% (legacy 36%). A belt is about one tile wide at any size, so on a coarse
grid the volcanic arcs on oceanic crust covered a large part of the globe (22% of all tiles at size 3, against 4.6% at size
6) and small worlds came out mostly land; the preset sets `ElevationGenerationStage.ReferenceTileCount` to the size-6 tile
count, which scales arc height by `sqrt(tiles / reference)` below it, and land is now 28-32% at every size from 2 to 7
(size 2 was 86%, size 3 58%, size 4 45%; size 6 and up are unchanged). `WithElevation` configures that stage. All knobs are on
`TectonicPlateGenerationStage` and `NoisyFloodFillPlatePartitioner`; later `WithTectonics` calls override the preset. The
preset also sets the island-chain repair's `BridgeReach` to 2, so a strait also cuts the wider land that would bridge it. It turns on
`ErosionGenerationStage.OrderIndependent`: hillslope diffusion and the glacial pass then relax against the surface as it stood at the
start of each sweep instead of sweeping in tile-index order, so the result no longer depends on how the mesh happens to be numbered
(legacy worlds keep the in-place sweep and their exact output).

Three climate refinements and one erosion process are on by default, and each can be switched off:

- **Island-chain repair** (`WithoutIslandChains()` to disable, `WithIslandChains(s => s.Options...)` to tune). Where two
  oceanic plates meet, the uplift apron is a tile or two wide but continuous along the whole border, so the coast came
  out as a land ribbon hundreds of kilometres long through open ocean instead of an arc of separate volcanoes.
  `IslandChainDetector` finds them: land with no interior tile (coast depth >= 4) within two rings, restricted per
  tile to oceanic crust within two rings of a plate boundary, grouped into runs at least six rings end to end.
  `IslandChainStage` (order 17, after erosion and ahead of the climate refresh) then walks each run from one end and
  drowns 1-3-ring straits between 2-5-ring beads, deterministically per (seed, anchor tile). It runs after erosion
  because thermal creep refills a strait cut earlier. Hand-sculpted and pinned tiles are never drowned, and grids below
  8000 tiles (size < 5) are skipped, where a one-tile ribbon is already the smallest land feature. Over sixteen seeds
  at size 6 the audited ribbon tiles fall 83% (18032 to 3075) for 2.8% of land.
- **Climate refresh** (`WithClimateRefresh(false)` to disable). Erosion runs after the first climate pass and moves the
  coastline, because sediment fills shallows: a size-4 world gains around 20% more land tiles, and every one of them
  kept the ocean's 0.55 rainfall floor. `ClimateRefreshStage` (order 17, tied with erosion and placed after it by the
  stable sort) re-runs the same configured climate on the final relief, so rain shadows, temperature and the
  maritime-distance term follow the coast and mountains the player sees. Measured over seven worlds (sizes 3-5): land
  is 0.01-0.04 drier and 0.015-0.033 warmer, jungle drops from 16% to 12% of land, desert rises from 7% to 10%,
  glacier falls from 13% to 10%, lakes are unchanged. Rivers fell 12%, so `HydrologyStage.RiverThresholdScale`
  defaults to 0.92 (it was 1) to restore the count; on seven other worlds it landed at -4.7% in aggregate, with the
  per-world scatter of +/-30% that a river count of 35-120 always has.
- **Meridional wind** (`WithClimate(c => c.MeridionalWind = 0)` for purely zonal wind). The surface wind blows 0.3 of
  its strength along the meridian (equatorward in the Hadley and polar cells, poleward in the Ferrel cell), about 17
  degrees off the parallel. It leaves the mean temperature, rainfall and biome shares within half a point and moves
  3-6% of tiles to another biome; what it changes is the direction of the rain shadows and maritime plumes.

- **Ocean currents** (`OceanHeatStrength = 0` and `OceanRainGain = 0` on the climate stage to disable). The wind
  bands already in the model are a wind stress; its curl spins a subtropical gyre at about 30 degrees (anticyclonic)
  and a subpolar one at 60 (cyclonic), mirrored across the equator and rescaled with the rotation rate like the
  wind. The stream function solves the Stommel balance on the tile graph with no flow through any shore, and the
  beta effect (rotation varying with latitude, so a boundary layer of 0.06 rad, about 380 km, whatever the detail
  level) makes the western side of every basin a narrow, fast, poleward jet and the eastern side a broad, slow,
  equatorward drift: the Gulf Stream and Kuroshio against the Canary and Humboldt currents. Measured on a rectangular
  test basin, the western side's mean speed is 1.7-2.2 times the eastern and its peak 3.5-5.3 times (sizes 4-6; the peak keeps sharpening as the layer is better resolved), and with the beta
  effect removed the two sides are mirror images (a test pins both). A parcel carried along the flow keeps the
  temperature of where it came from and relaxes to the local equilibrium over 0.45 rad, so poleward flow is warm
  and equatorward flow cold; the anomaly peaks at about +0.19 / -0.11 temperature units (roughly 10 K and 6 K). Coastal land
  takes the anomaly of the water its maritime air crossed, weighted by that air (`ClimateInfo.OceanHeat`, already
  included in `Temperature`), and rainfall follows it (`1 + 2 x anomaly`, clamped to [0.5, 1.4]): cold-current coasts under the
  subtropical highs get the fog deserts, warm ones the wet east coasts. `ClimateInfo.CurrentX/Y/Z` is the surface
  current over water. It is solved after erosion only (the first climate pass exists to give erosion its rainfall and
  temperature, and the refresh overwrites it), on the icosphere's own level hierarchy: a coarse solve, then a
  fixed number of Gauss-Seidel sweeps per level, so the cost is linear in the tile count (+12 / +28 / +172 ms at sizes
  4 / 5 / 6) and the stream function agrees with a 20000-sweep flat solve to about 1% (relative L2) and the carried temperature to 1e-3 on average. Over eleven worlds 6.7% of land moves by more
  than 0.03 temperature units (about 1.6 K), 2.3% by more than 0.05, and 4% of land changes biome; river and lake counts
  move by 1-2%, and the biome shares by less than half a point. The tidally locked model and sea ice are not covered:
  the wind bands stay Earth-like and the water never freezes.

- **Wind erosion** (`WithErosion(e => e.AeolianIterations = 0)` to disable). After the fluvial passes the erosion stage
  lifts loose ground from arid, thawed land (a smooth function of first-pass rainfall and temperature), hops it one tile
  at a time to the neighbour the wind points at, and drops it where the surface rises, the wind dies, or the ground is
  vegetated; what is still airborne after the last pass settles where it hangs. It is a Jacobi update on a frozen
  surface, so tile order cannot matter, every move takes from one tile and gives to another (mass is conserved), a tile
  cannot lose more than `AeolianMaxDeflation` (0.03) in total, and pinned tiles neither give nor keep anything.
  A tile is tens to hundreds of km across, so the product is broad deflation basins upwind and sand blankets downwind,
  not dunes. Measured on seeds 42 and 7 at sizes 3-4: 17-20% of tiles move, the largest change is 0.009-0.013 (4 passes), lakes
  are unchanged and rivers move by 0-3 of 54-109. `AeolianIterations = 0` skips the pass entirely.

For the previous pipeline use `WithClimateRefresh(false)`, `MeridionalWind = 0`, `OceanHeatStrength = 0`,
`OceanRainGain = 0`, `RiverThresholdScale = 1` and `AeolianIterations = 0`.

## Editing a world: hand edits as a pipeline input

A world is a pure function of (seed, configuration, hand edits). Build it with `WithUserEdits()` and
the per-tile `UserEditInfo` layer is an *input* no stage writes:

```csharp
using var world = new WorldBuilder().WithSize(5).WithSeed(42).WithUserEdits().Build();
world.Edits[tile].HeightDelta = 0.8f;                         // sculpt: added before climate and erosion
world.Edits[other].Flags = UserEditFlags.PinHeight;           // fix: the final height, restored after erosion
world.Edits[other].PinHeight = 0.2f;
world.Edits[third].Flags = UserEditFlags.PinBiome;            // hydrology cover and the biome stage keep it
world.Edits[third].PinBiome = BiomeType.Desert;
world.Edits[fourth].TemperatureDelta = -0.15f;                // climate brush: +/- in Temperature units (~55 K per unit)
world.Edits[fourth].PrecipitationDelta = 0.2f;                //   and in Precipitation units, both clamped to [0, 1]
world.Recompute();                                            // elevation -> climate -> erosion -> hydrology -> biomes -> catalogs
```

`Recompute()` re-runs the pipeline from the elevation stage (`WorldGenerationPipeline.ExecuteFrom`),
clears and repopulates the river, water-body, range and deposit catalogs, and rebuilds the spatial
index. It does not repeat the plate simulation. With no edits it reproduces the generated world bit for
bit, and repeated calls never erode twice. The climate offsets are added last, in both climate passes and
after the ocean currents: literal and local, so a painted warm sea patch does not move the gyres and
sea air does not carry the offset inland (paint the coast too if you want that). Biomes, rivers and
lakes read the offset climate like any other. It is global: hydrology is a whole-world solve, so one
raised ridge may redirect a basin far from it.

**Threading.** A `World` is not thread-safe. Concurrent *reads* (queries, spans) are fine; `Recompute()`,
writes through `Edits`, and `Dispose()` are writers that rewrite the layers and catalogs in place, so a reader
that overlaps one sees torn data or freed memory. Nothing enforces this: guard writers against readers yourself
(a `ReaderWriterLockSlim` is enough) or keep the world on one thread. Re-fetch spans after `Recompute()`.

## Land use: what the ground could carry, and what people decide to do with it

A biome is what the climate grows, and the physics reproduces it. Farmland, pasture, a burn scar, a
hunting range or a fishery are not biomes: they sit on top of one and move nothing. Core therefore keeps
them in two layers of their own, outside the physics, and leaves the *decision* to plugins:

| Layer | Meaning | Written by |
|---|---|---|
| `LandCapacityInfo` | what a tile **could** support, 0-255 each: `Arable`, `Pasture`, `Game`, `Timber`, `Fish` | `LandCapacityStage` (order 25), from biome, climate, slope, fresh water and ocean currents |
| `LandUseInfo` | what is **decided**: `Use` (an exclusive id), `Activities` (overlapping `LandActivity` flags), `Intensity` | allocation stages you add, orders 26..98 |
| `UserEditInfo.LandUse` / `LandActivities` + `PinLandUse` | a hand-painted answer that always wins | `LandUseResolveStage` (order 99) |

```csharp
using var world = new WorldBuilder().WithSize(5).WithSeed(42).WithLandUse()   // implies WithUserEdits()
    .AddStage(new MyHerdersAndFarmers())                                      // your allocation, order 26..98
    .Build();
var use = world.Map.DataStore.GetSpan<LandUseInfo>()[tile];                   // Use, Activities, Intensity
var cap = world.Map.DataStore.GetSpan<LandCapacityInfo>()[tile];              // what it could carry
world.Edits[tile].Flags |= UserEditFlags.PinLandUse;                          // the player painted it
world.Edits[tile].LandUse = LandUseId.Farmland;
world.Recompute();
```

The split is deliberate. *Use* is exclusive (a tile is farmland **or** pasture **or** built-up), but
*activities* overlap (a forest is hunted and logged; a nomad's range crosses pasture), so they are bit
flags: `Grazing`, `Hunting`, `Foraging`, `Logging`, `Fishing`, with the top three bits free. Use ids 1-4
are Core (`Farmland`, `Ashlands`, `Pasture`, `Built`) and are persisted, so never renumber them; 5-127
are reserved; 128-255 belong to games and plugins, whose names and colours travel in their own content
pack. `LandCapacity.Evaluate(new LandSite(...))` scores a hypothetical tile without a world.

Plugin rules, all from the existing stage contract:

- `LandCapacityStage` is the first writer of `LandUseInfo`, so the pipeline clears it on every run and
  `Recompute()` never leaves a stale decision behind. Declare `Reads` **and** `Writes` for `LandUseInfo`
  (refine what earlier stages decided); a blind second writer throws, as for any layer.
- Nothing in climate, erosion, hydrology or the biome stage reads these layers, so land use cannot move a
  river or a biome. If you want people to change the land (clear a forest), write that into
  `UserEditInfo.PinBiome` or a plugin that runs *before* the biome stage; it is a physics decision, not
  a land-use one.
- Off by default: a default world carries no people and pays nothing for any of this.

Capacity is calibrated by direction, not precision (like `LandCover` and `SettlementScoring`): every
product is a product of smooth factors, so a threshold cannot stripe a coast into speckle. Measured on
seed 42, 14% of land tiles score 128+ for farming at size 4 (27% at size 3, 12.6% at size 5): coarse
worlds credit more, because a coarse tile averages steep ground away. At this scale a tile is tens to
hundreds of kilometres across, so "arable" means the best the tile could do, not a field. Fish follow
shallow shelves and cold currents (the Humboldt and Benguela fisheries), so the ocean-current layer
feeds the economy. Scripts get the same data: `arable(i)`, `pasture(i)`, `game(i)`, `timber(i)`,
`fish(i)`, `landUse(i)`, `activities(i)`, `intensity(i)` and the setters `setLandUse`, `setActivities`,
`setIntensity`, with `LANDUSE.*` and `ACTIVITY.*` constants (`samples/JintScripts/herders-and-farmers.js`).

## Addresses and seeds: where am I?

Every map in the hierarchy has a stable address. The planet tier uses tile indices; the lower tiers hang off those:

```csharp
PlanetHex hex = world.Resolve(coord);   // GeoCoord -> planet tile
int tile = hex.TileIndex;

// Compound addresses for the lower tiers (WorldSeed travels with them,
// so an address is meaningful on its own - handy for save files).
MapAddress regionAddr = MapAddress.ForRegion(world.Seed, tile, regionCellIndex: 17);
MapAddress localAddr = MapAddress.ForLocal(world.Seed, tile, 17, localTileIndex: 93);
```

The key property: **an address plus the world seed fully determines the map**. No database, no cache directory, no "did I generate this one already". Derivation is a pure function, so you can materialize any map on demand, in any order, on any machine, and get the same tiles. The per-tier seed streams are independent (distinct salts per tier in `MapSeeds`), so region detail never correlates weirdly with planet layout just because they share a tile index.

```csharp
uint regionSeed = MapSeeds.DeriveRegionSeed(world.Seed, tile);
uint cellSeed = MapSeeds.DeriveRegionCellSeed(world.Seed, tile, 17);
uint localSeed = MapSeeds.DeriveLocalSeed(world.Seed, tile, 17);
```

You rarely call these directly - `GetRegion`/`GetLocal` do it for you - but they're public so save/load code and tools can re-derive anything from an address.

## Zooming in: regions and local maps

One planet hex expands to a region grid; one region cell expands to a local grid. Both are real hex grids (flat-top, axial patch with hex adjacency), so movement and range code you write once works at every tier:

```csharp
RegionHandle region = world.GetRegion(hex);       // 8x8 by default
LocalMapHandle local = region.GetLocal(17);       // 16x16 by default

// Or straight from the world:
LocalMapHandle same = world.GetLocal(regionAddr.ToRegion());

// Same IHexMap<TTile> shape at every tier:
int count = local.TileCount;
LocalTile t = local.GetTile(93);
Span<int> nb = stackalloc int[6];
int n = local.GetAdjacent(93, nb);
int idx = local.GetTileAt(q: 3, r: 5);            // axial -> flat index, -1 if outside
int idx2 = local.GetTileAt(CubeCoord.FromAxial(3, 5));
```

Tile payloads carry what you'd expect, plus their heritage:

```csharp
// RegionCell: CellIndex, Seed, Elevation, Biome, Moisture, IsRiver, WaterDepth, Flow
// LocalTile: Index, Height, Biome, Danger, IsWater, Temperature, Precipitation,
//            WaterDepth, ParentBiome (the planet biome this tile zoomed out of)
```

Handles also tell you about themselves: `Address`, `Bounds` (center, radius in km, edge-ring cells), `Parent` (the parent context they were derived from), and river threading state (`HasRiver`, `RiverEntryCell`, `RiverExitCell`).

Sizes are customizable per call (`world.GetRegion(hex, regionSize: 12)`), and the old `DeriveRegion`/`DeriveLocal` helpers still exist as `[Obsolete]` shims if you have code using them.

## Per-cell coordinates: lon/lat below the planet tier

Every region cell and local tile has a geographic center, computed with the same projection derivation uses (cell offset onto the tangent plane at the map center, normalized back to the sphere). Centers are pure functions of the map bounds, so they need no store access and are identical on every machine:

```csharp
RegionHandle region = world.GetRegion(hex);
GeoCoord cellGeo = region.CellCenter(17);      // lon/lat of region cell 17
int cell = region.CellAt(cellGeo);             // inverse: 17 (nearest center wins)

LocalMapHandle local = region.GetLocal(17);
GeoCoord tileGeo = local.TileCenter(93);       // lon/lat of local tile 93
int tile = local.TileAt(tileGeo);              // inverse: 93
```

Round-trip accuracy is within half a cell diagonal, and results are deterministic across builds with the same seed. Because a cell center is just a `GeoCoord`, all planet-tier queries accept it directly — `world.GetTileFeatures(cellGeo)` returns the features of the planet hex containing that cell, `world.SampleBiome(tileGeo)` samples its biome, and so on.

## Why zoomed maps look like their parent

This is the part the library is proudest of, so here's the honest version of how it works. Inheriting just the average ("this hex is mountains, make mountain-ish noise") gives you a map that *feels* unrelated - the ridge that ran through the north-west of the parent tile vanishes. To keep it, the child map needs to know the parent's *direction*, not just its average. That's what `ParentContext` is:

```csharp
ParentContext parent = TerrainOrientation.Sample(world.Map.DataStore, tile);

// Scalars: what the tile is on average...
float elev = parent.MeanElevation;        // tile + neighbor ring mean
BiomeType biome = parent.DominantBiome;   // majority vote of tile + ring
float temp = parent.MeanTemperature;      // local lapse rate applies on top

// ...and vectors: which way things point.
Vector3D gradient = parent.ElevationGradient;  // least-squares slope fit
double aspect = parent.AspectRadians;          // downhill bearing
Vector3D strike = parent.OrogenyStrike;        // ridge/contour direction
float[] ring = parent.NeighborHeights;         // raw adjacent heights, for edge pinning
int entry = parent.FlowEntryTile;              // where surface water comes from
int exit = parent.FlowExitTile;                // where it leaves (-1 = sink)
```

`TerrainOrientation.Sample` computes all of this from the dense store spans plus adjacency. It loops the actual ring size, so 5-neighbor pentagon tiles and 6-neighbor hex tiles share one code path, and missing layers degrade to documented defaults instead of exploding (partial pipelines stay queryable).

Derivation then combines four ingredients (in `RegionMaps.DeriveRegionMap` / `DeriveLocalMap`):

1. **Base surface** interpolated from the parent neighborhood - a plane through the parent mean carrying the parent slope. This is what makes the NW ridge still run NW.
2. **Detail** from ridged noise sampled through an *anisotropic domain warp* around the parent strike: coordinates across the strike are compressed (tighter ridge spacing), coordinates along it elongated (smooth crests). Region tier follows the strike tightly; local tier more loosely.
3. **Edge pinning**: outer cells blend toward parent-interpolated boundary values, so neighboring child maps stitch instead of cliffing at shared borders.
4. **River threading**: when the parent tile is a river or carries high flow, a channel is carved from the entry side to the exit side (tapered at both ends); otherwise local drainage follows the inherited aspect.

If the defaults don't suit your game, the knobs are all in one place:

```csharp
var opts = RegionDetailOptions.LocalDefault;
opts.AcrossStrikeScale = 2.5;   // tighter ridges across the strike (default 1.8)
opts.AlongStrikeScale = 0.4;    // smoother crests along it (default 0.5)
opts.DetailAmplitude = 0.2f;    // more rugged interior
opts.EdgePinCells = 3.0;        // wider stitching band

var custom = RegionMaps.DeriveLocalMap(address, parent, center, radiusKm, seed,
    size: 16, options: opts);
```

`RegionDetailOptions.RegionDefault` vs `LocalDefault` is just "tight strike coupling" vs "loose strike coupling" with slightly different amplitude - start from those and tweak.

## Sites: ruins, cities, mines, landmarks

Once you have a region or local map, you'll want stuff *on* it. That's what site injectors do. An injector is one placement pass over a materialized map - it looks at the terrain (with its boundaries), looks at what earlier injectors already placed, and returns sites:

```csharp
RegionHandle region = world.GetRegion(hex);

SiteCatalog discovered = region.WithInjectors(new ISiteInjector[]
{
    new SettlementInjector(),   // towns where water + fertile + low danger line up
    new RuinInjector(),         // old stones in harsh, history-adjacent ground
    new MineInjector(),         // digs near deposit/rock country
    new LandmarkInjector(),     // waterfalls, passes, oddities
});

foreach (var site in discovered.Sites)
    Console.WriteLine($"{site.Kind} on cell {site.CellIndex} (danger {site.DangerDelta:+0.0;-0.0}) [{string.Join(",", site.Tags)}]");
```

A few things worth knowing about how they run:

- Injectors execute ascending by `Order`; two injectors with the same `Order` throw `InvalidOperationException` (fail fast beats silent last-write-wins).
- Each injector gets its own RNG sub-stream derived from (injector id, address), so adding a new injector later doesn't reshuffle the sites the old ones placed.
- Later injectors see earlier ones' output via `context.ExistingSites` - "ruins avoid living cities" is expressible, not a special case.
- `PlacedSite` carries `Kind`, `CellIndex`, `FootprintRadius`, `NameSeed` (an opaque draw for your own name generator), free-form `Tags`, and `DangerDelta`/`HabitabilityDelta` that feed back into queries.
- Works on local maps too: `local.WithInjectors(...)`. Injectors declare which tier(s) they apply to via `SiteTier` (`Region`, `Local`, `Both`).
- `SettlementInjector` takes knobs (`maxSites`, `order`, `tier`) and is the per-map sibling of the planet-scale `ScoreCitySites`.

Writing your own is straightforward - here's a haunted-barrows injector in full:

```csharp
public sealed class BarrowInjector : ISiteInjector
{
    public string Id => "barrows";
    public int Order => 10;
    public SiteTier Tier => SiteTier.Local;

    public IReadOnlyList<PlacedSite> Inject(SiteInjectionContext context)
    {
        var placed = new List<PlacedSite>();
        for (int cell = 0; cell < context.CellCount; cell++)
        {
            if (context.IsWater(cell)) continue;
            // Hills with a grim history, and not too close to an existing city.
            bool nearCity = false;
            foreach (var s in context.ExistingSites)
                if (s.Kind == SiteKind.City && s.CellIndex == cell) { nearCity = true; break; }
            if (nearCity) continue;

            float e = context.GetElevation(cell);
            if (e > 0.3f && context.Rng.NextDouble() < 0.06)
            {
                placed.Add(new PlacedSite
                {
                    Kind = SiteKind.Ruin,
                    CellIndex = cell,
                    FootprintRadius = 1,
                    NameSeed = context.Rng.NextUInt(),
                    Tags = new[] { "barrow", "haunted" },
                    DangerDelta = 1.5f,
                });
            }
        }
        return placed;
    }
}
```

Note `context.Rng` is a public *field*, not a property - `Rng` is a mutating struct, and a property getter would hand you a copy and silently eat your draws. That's deliberate, not a style lapse.

The context also gives you everything the injector needs to be smart: `Address`, `Parent` (the parent context), `Bounds` (center, radius, edge cells - so you know where the map ends), tier-appropriate accessors (`GetElevation`, `GetBiome`, `IsWater`, `IsRiverChannel`, `GetDanger`, `GetMoisture`), and `GetAdjacent` for neighborhood checks.

## Finding stuff fast

`World.Index` is a bucketed nearest-feature index over planet tile centers (same lat/lon bucketing idea as the store's own coordinate lookup). Queries order cells by center alignment and stop early with an exact bound - results always agree with an exhaustive scan, they just get there without one. The `NearestWater`/`NearestRiver`/`NearestDeposit` methods on `World` all delegate to it.

One enum covers every feature kind, so you don't need five method families:

```csharp
// Any feature, one call shape. Address or coordinate, your pick.
var river = world.Index.NearestFeature(coord, FeatureKind.River);
var range = world.Index.NearestFeature(hexAddr, FeatureKind.Range);
var glacier = world.Index.NearestFeature(coord, FeatureKind.Glacier);

// Deposits keep their type filter; sites keep their kind filter.
var gold = world.Index.NearestDeposit(coord, DepositType.Gold);
var city = world.Index.NearestSite(coord, SiteKind.City);

// "Give me all the tiles of this kind" for map-wide scans.
IReadOnlyList<int> allRivers = world.Index.TilesOfKind(FeatureKind.River);
```

`FeatureKind` covers rivers, water bodies, ranges, glaciers, deposits, and injected sites. Note the index is planet-tier: a `MapAddress` resolves to its parent planet tile for the lookup.

`GetTileFeatures` is the "tell me everything about this hex" call and now includes placed sites and the tile's address:

```csharp
TileFeatureInfo info = world.GetTileFeatures(tileIndex, sites: mySiteCatalog);
Console.WriteLine($"biome {info.Biome}, river {info.IsRiver}, sites: {info.Sites.Length}");
```

## Persistence: overrides and location bindings

Generation is a pure function - same seed, same map, forever. But games change things: the party burns down a forest, your campaign tool turns a tile into a named location with a tavern. The library handles this the same way it handles history: it doesn't store anything itself, it *consumes* a game-owned store at query time. Null store means pure geography (zero migration cost); plug one in and queries apply your deltas.

```csharp
var materialized = new MaterializationStore();   // in-memory; implement the
                                                 // interface over your own DB
                                                 // for real persistence
var opts = new QueryOptions { Materialized = materialized };

// The party burned the forest down. It's scrubland now, and spicy.
materialized.SetOverride(
    MapAddress.ForPlanet(world.Seed, tile),
    new TileOverride { Biome = BiomeType.Plains, DangerLevel = 4 });

// Bind a generated address to your game's location id (two-way lookup).
materialized.BindLocation(MapAddress.ForLocal(world.Seed, tile, 17, 93), "tavern-cell-93");
string? id = materialized.GetBoundLocationId(MapAddress.ForLocal(world.Seed, tile, 17, 93));

// Queries now see the world as modified:
var (biome, danger) = world.SampleBiome(coord, opts);   // Plains, danger bumped
```

Overrides are *replacement*, not additive: a set field wins over the generated value. `TileOverride` currently covers `Biome` and `DangerLevel` - the two things games mutate most. `SampleBiome`, `ScoreCitySites`, and `GetTileFeatures` all honor the store when you pass it via `QueryOptions`.

If you're wiring this to a campaign manager: store the `MapAddress` + seed in the location's metadata and you've got a stable re-derivation key - regenerate the tile from the address any time, apply the override, done. No snapshots of generated data needed.

## Custom generation stages and history

Two extension points predate the hierarchy work and still work the same way - the README covers both in detail:

- **`IWorldGeneratorStage` + `[WorldGeneratorStage(Order = N)]`**: planet-tier generation passes (tectonics is order 10, elevation 15, climate 16, erosion 17, hydrology 18, biomes 20). Declare `Reads`/`Writes` layer contracts so collisions fail fast, and `pipeline.ValidateContracts()` will check them.
- **`IHistoricalContext` via `QueryOptions.History`**: the library never simulates history - your game does, and the library blends it into danger/habitability at query time. Same ownership split as materialization: game owns the data, library consumes it.

That's the whole API. If something's unclear or a doc example doesn't compile against the code, that's a bug in the docs - file it like one.
