# RoguelikeToolkit.World

[![CI](https://github.com/myarichuk/RoguelikeToolkit.World/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/myarichuk/RoguelikeToolkit.World/actions/workflows/ci.yml)

Deterministic procedural planets for RPGs and roguelikes: an icosahedral hex sphere with tectonics, elevation, climate,
erosion, hydrology, biomes and mineral deposits, lazy region/local zoom, a data-driven name generator, and a pluggable
generation pipeline. The library supplies geography; history stays in your game (see [History hooks](#history-hooks-game-owned-history)).

| Project | Targets | What |
|---|---|---|
| `RoguelikeToolkit.World.Core` | `net10.0`, `netstandard2.1` | The library. |
| `RoguelikeToolkit.World.Scripting.Jint` | `net10.0`, `netstandard2.1` | Sandboxed JavaScript generation stages. |
| `RoguelikeToolkit.World.App` | `net10.0` | Avalonia 11 visualizer. |
| `RoguelikeToolkit.World.NameTester` | `net10.0` | Avalonia name-generator playground. |

MIT.

## Install

- **.NET:** `dotnet add package RoguelikeToolkit.World.Core` (and `RoguelikeToolkit.World.Scripting.Jint` for script
  stages).
- **Unity:** see [Unity / IL2CPP](#unity--il2cpp).

## Quick start: `World` and `WorldBuilder`

For games, the `World` facade is the entry point (owns the `WorldMap` plus
managed feature catalogs):

```csharp
using var world = new WorldBuilder().WithSize(3).WithSeed(42).Build();

float h = world.SampleElevation(new GeoCoord(48.2, 16.4));
var (biome, danger) = world.SampleBiome(new GeoCoord(48.2, 16.4));
var nearby = world.QueryRadius(new GeoCoord(48.2, 16.4), 500.0);
var water = world.NearestWater(new GeoCoord(48.2, 16.4));
var river = world.NearestRiver(new GeoCoord(48.2, 16.4));

// Hierarchical detail: world hex -> region grid -> local grid (lazy, seeded).
RegionHandle region = world.GetRegion(tileIndex);
LocalMapHandle local = region.GetLocal(cellIndex);

// City placement for RPG settlement logic.
var sites = world.ScoreCitySites(new CitySiteFilter { TopN = 10 });

// Prospecting: tectonically seeded ore/fuel overlay.
var copper = world.NearestDeposit(new GeoCoord(48.2, 16.4), DepositType.Copper);

// WKT export for editors/external tools (storage stays tile-id lists).
string riverWkt = world.RiverToWkt(0);
string lakeWkt = world.WaterBodyToWkt(0);
```

Default generation order: tectonics (10) → elevation (15) → climate with
rain shadows (16) → erosion thermal+diffusion+glacial+fluvial (17) →
hydrology with lakes (18) → biomes (20). Mineral deposits seed after the
build from the finished tectonics + climate + hydrology.

## Layers: fields vs features

- **Field layers** (dense, blittable, in `WorldDataStore`): `TectonicPlate`
  (16 bytes per tile: `Crust` + `Boundary`, continentality, boundary rings, a
  signed orogeny driver, and a plate `Id` into the per-plate `PlateInfo` table), `ElevationInfo`, `HydrologyInfo` (discharge,
  water-body id, river flag, routing surface, lake depth, playa flag),
  `ClimateInfo` (temperature, precipitation, wind), `LocalMapInfo` (biome,
  danger, seed). File-backed stores use the v3 header (up to 32 field
  layers and tables); old v1/v2 files are rejected with `InvalidDataException` by design.
- **Feature catalogs** (sparse, managed, on `World`): `RiverCatalog` (rivers
  with sea/lake/sink terminals and mouth discharge), `WaterBodyCatalog`
  (ocean/seas plus open and endorheic lakes, with boundary loops),
  `RangeCatalog` (mountain ranges, valleys, canyons, playas), and
  `DepositCatalog` (ore/fuel/mineral overlay: iron, copper, gold, silver,
  tin, lead-zinc, uranium, coal, oil, gas, salt, gems, bauxite). Query in
  tile-id space; call `GeoWkt` / `World.RiverToWkt` for WKT only at the
  boundary.

## Landscape model notes

- **Tectonics** builds mixed plates (continents and oceans per plate),
  classifies boundaries from drift convergence with subduction polarity
  (oceanic dives, continental overrides), and spreads a signed orogeny belt
  around each boundary — wide for continent-continent collision, narrow for
  trenches, ridges for divergent oceans, rifts for divergent continents.
- **Elevation** stacks continentality, swells, rolling hills, micro ripples,
  belt orogeny with ridged crests, and hotspot swells.
- **Erosion** runs frost-boosted talus, hillslope diffusion, flux-weighted
  glacial U-valley carving with overdeepening (future tarns), and
  stream-power fluvial incision (`E = K·Q^m·S^n`) over rock hardness, with
  fan/delta splays — narrow and deep in arid canyon reaches.
- **Hydrology** routes discharge on the priority-flood filled surface with
  flat-resolved channels and desert transmission loss. Depressions pond by
  water balance (inflow vs evaporation) into open or endorheic lakes; dry
  sumps become playas. Every river terminates: sea, lake, or sink.
- **Glaciers** are a climate-aware cover mask (cold + accumulation, with dry
  polar interiors staying tundra) and a first-class `Glacier` biome;
  `Canyon` is a biome too. The visualizer adds Climate and Deposits color
  modes, hillshaded elevation, discharge-graded rivers, and deposit/lake
  details in the hex inspector.

## History hooks (game-owned history)

The library never simulates history. Implement `IHistoricalContext` in your
game (Caves-of-Qud-style events project onto tiles/sites) and pass it per
query — `null` means pure geography:

```csharp
var sites = world.ScoreCitySites(filter, new QueryOptions { History = myHistory });
var (biome2, danger2) = world.SampleBiome(coord, new QueryOptions { History = myHistory });
```

`TryGetTileModifier` shifts danger/habitability per tile (ruins raise danger,
roads raise habitability); `GetSitesNear` exposes historic sites for radius
blends. See `HistoricSiteIndex` to reuse the spatial-query path.

## Developing a New Pipeline Stage

To write and inject a new world generation step, follow these instructions:

### Step 1: Implement `IWorldGeneratorStage`
Create a new class that implements the interface. Implement your generation logic within the `Execute(WorldMap map)` method. Use `map.DataStore.GetSpan<T>()` to gain mutable, zero-allocation access to your underlying layer structs.

### Step 2: Apply `WorldGeneratorStageAttribute`
Decorate your class with the `[WorldGeneratorStage(Order = N)]` attribute to specify its execution order. Lower numbers execute earlier in the pipeline. Note that assigning duplicate order values to multiple stages will throw an `InvalidOperationException` upon discovery.

```csharp
using RoguelikeToolkit.World.Core;

[WorldGeneratorStage(30)]
public class MyBiomeGenerationStage : IWorldGeneratorStage
{
    public void Execute(WorldMap map)
    {
        var localSpan = map.DataStore.GetSpan<LocalMapInfo>();
        var tectonicSpan = map.DataStore.GetSpan<TectonicPlate>();

        for (int i = 0; i < localSpan.Length; i++)
        {
            // Apply logic to mutate localSpan based on tectonicSpan data
        }
    }
}
```

### Step 3: Injecting the Plugin
The `WorldGenerationPipeline` can discover stages via reflection. On Unity / `netstandard2.1` use `pipeline.DiscoverFrom(new[] { typeof(MyStage).Assembly })` (no runtime DLL loading there). Otherwise, by default, calling `pipeline.Discover("Plugins")` merges discovered stages with manually added stages and skips stage types that are already present. If you want a clean rebuild from discovery only, call `pipeline.DiscoverAndReplace("Plugins")` (or `pipeline.ResetStages()` first).

Discovery failures are reported structurally: pass a diagnostics callback to `Discover(..., onDiagnostic)` to collect non-fatal load/activation errors, or omit the callback to fail fast with an `AggregateException`.

### Step 4: Declare layer contracts
Stages that read or write dense field layers should declare them so plugin
collisions fail fast instead of silently last-write-wins:

```csharp
[WorldGeneratorStage(42, Reads = new[] { typeof(ElevationInfo) }, Writes = new[] { typeof(ClimateInfo) })]
public class MyClimateTweak : IWorldGeneratorStage { ... }
```

Call `pipeline.ValidateContracts()` (or `ValidateContracts(map)` to also check
layer registration). Rules: the first writer of a layer is fine; every later
writer must also read it (read-modify-write refinement, e.g. elevation →
erosion). A blind second writer throws `InvalidOperationException` naming both
stages. Execution order is cached, so repeated `Execute` calls stay
allocation-free.

## Core Library Architecture

The `RoguelikeToolkit.World.Core` library relies on two primary pillars to achieve zero-allocation, high-performance execution:

### 1. Centralized Data Storage: `WorldDataStore`
The hex sphere data structure is backed by a single contiguous native block (a memory-mapped file on `net10.0`, `Marshal.AllocHGlobal` for in-memory stores on `netstandard2.1`), represented by the `WorldDataStore` class. This store serves as the single source of truth for all queryable tile data in the world grid.
- All layer data (such as `TectonicPlate` and `LocalMapInfo`) are unmanaged blittable structs.
- Layers are registered with the `WorldDataStore` prior to allocation (`DataStore.RegisterLayer<T>()`). The store dynamically calculates byte offsets for all registered layers to store them contiguously.
- When `Allocate()` is called, that single block is created, so lookups afterwards are allocation-free and cache-friendly.
- Tile topology (tile center coordinates + adjacency) is precomputed once per world size and stored in contiguous arrays indexed by tile id. `GetTileIndex`, `GetGeoCoord`, and `GetAdjacent` all read from this canonical topology table.
- `WorldDataStore` caches per-size tile unit vectors (`GetTileVectors()`), so repeated generation runs avoid rebuilding spherical tile positions and repeated trig conversions in hot paths.

### 2. Pluggable Generation Pipeline: `WorldGenerationPipeline`
World generation logic is abstracted into discrete, pluggable pipeline stages (`IWorldGeneratorStage`). The visualizer (or any consuming application) simply sets up the pipeline, discovers the stages, and executes them over the `WorldMap`.

## Unity / IL2CPP

`RoguelikeToolkit.World.Core` and `RoguelikeToolkit.World.Scripting.Jint` also target `netstandard2.1`, the profile Unity
consumes. The whole test suite runs against that build too (`dotnet test -p:CoreTargetFramework=netstandard2.1`), and
CI publishes a NativeAOT smoke test (stricter than IL2CPP) whose world checksum must match the JIT run.
Supported floor: Unity 2022.3 (IL2CPP before 2022.1 cannot instantiate generics over value types at runtime).

### Install

Two UPM packages ship the prebuilt `netstandard2.1` DLLs: `com.rogueliketoolkit.world.core` and
`com.rogueliketoolkit.world.scripting.jint` (depends on core). Third-party DLLs (SharpArena, YamlDotNet, Jint) come
from [UnityNuGet](https://github.com/bdovaz/UnityNuGet) as `org.nuget.*` dependencies instead of being bundled: Unity
refuses two copies of one DLL, and Jint/YamlDotNet are shared with `RoguelikeToolkit.Entities`. In
`Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": ["com.rogueliketoolkit"]
    },
    {
      "name": "UnityNuGet",
      "url": "https://unitynuget-registry.openupm.com",
      "scopes": ["org.nuget"]
    }
  ],
  "dependencies": {
    "com.rogueliketoolkit.world.core": "0.3.0",
    "com.rogueliketoolkit.world.scripting.jint": "0.3.0"
  }
}
```

**Without registries:** every GitHub Release carries `RoguelikeToolkit.World-<version>-unity-plugins.zip` (both
assemblies, the full third-party closure, one `link.xml`). Unzip into one folder under `Assets/Plugins/`; when combining
with the Entities zip, use the same folder so shared DLLs (Jint, YamlDotNet, ...) exist once.

### What differs in the `netstandard2.1` build

- **Plugins:** no runtime DLL loading (there is no collectible `AssemblyLoadContext`, and IL2CPP cannot load
  assemblies at all). Compile plugin stages into the player and hand their assemblies over:
  `pipeline.DiscoverFrom(new[] { typeof(MyStage).Assembly });`. `Discover("dir")` reports a
  `PlatformNotSupportedException` diagnostic for the directory and still discovers the built-in stages.
- **Memory:** in-memory stores use `Marshal.AllocHGlobal` instead of an anonymous memory-mapped file, and arenas use
  SharpArena's `DotNetUnmanaged` backend instead of P/Invoking `mmap`/`VirtualAlloc` (neither resolves on every
  player platform). File-backed stores (`WithFilePath`) still memory-map; avoid them on WebGL.
- **Stripping:** types Core reaches only through reflection (built-in stages, name/locale YAML DTOs) carry
  `[Preserve]` (honored by UnityLinker) and `[DynamicDependency]`. Each package hands UnityLinker its own `link.xml`
  through an editor hook (Unity ignores `link.xml` inside packages): YamlDotNet for Core; Jint and the script stage for
  Scripting.Jint. Your own discovered stages need `[Preserve]` (any attribute named `PreserveAttribute`) or a
  `link.xml` entry under `Assets/`.
- **Script stage memory budget:** Jint enforces `MemoryLimitBytes` by reading `GC.GetAllocatedBytesForCurrentThread`
  after every statement. Whether IL2CPP reports real numbers there is unverified; treat the statement and time budgets
  as the hard limits on IL2CPP.
- **Determinism:** `DetMath` and the shims (`Math.ScaleB`, `PriorityQueue`) are bit-identical to the .NET ones; the test
  suite's determinism checks pass against the `netstandard2.1` build. Cross-runtime equality on Mono/IL2CPP itself
  (their own `Math.*`) is not verified here.

## Docs

- [API Guide](docs/API.md) — the full public API with examples: sampling, zooming in, injectors, queries, persistence.
- [Architecture](docs/ARCHITECTURE.md) — how the pieces fit together, with diagrams.
- [Names](docs/NAMES.md) — the data-driven name generator: YAML schema, styles, themes, scripts.
- [Assessment](docs/ASSESSMENT.md) — how useful the library is for procedural RPGs/roguelikes, the gaps, and a plan for procedural history.
- [Storage](docs/STORAGE.md) — storage design for planet-scale terrain and 100k+ NPCs.
- [Surface modes plan](SURFACE_MODES_PLAN.md) — one pipeline, HexSphere and Map2D (lat/lon raster) surfaces; phased implementation plan.
- [Plugins](docs/PLUGINS.md) — extend generation with full-trust compiled C# stages or untrusted Jint script stages.

## Releases

Conventional commits on `main` drive [release-please](https://github.com/googleapis/release-please): it keeps a release
PR open with the next version and CHANGELOG. Merging it bumps `version.txt`, both `<Version>`s and both UPM
`package.json`s, then tags `vX.Y.Z`. The tag triggers `publish.yml`: build, test, UPM tarballs + plugins zip attached to
the GitHub Release (OpenUPM picks the tarballs up), NuGet when the `PUBLISH_ENABLED` repo variable is `true`.
