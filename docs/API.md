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

using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();

var coord = new GeoCoord(48.2, 16.4);
float height = world.SampleElevation(coord);
var (biome, danger) = world.SampleBiome(coord);
```

`World` is the facade you'll use 95% of the time. It owns the generated `WorldMap` plus the sparse feature catalogs (rivers, water bodies, ranges, deposits). `WorldBuilder` runs the default generation pipeline - tectonics -> elevation -> climate -> erosion -> hydrology -> biomes - and wires up the catalogs. `WithSize` is the icosphere subdivision level (tile count is `10 * 4^size + 2`, so size 3 is 642 tiles); `WithSeed` makes the whole thing reproducible.

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
