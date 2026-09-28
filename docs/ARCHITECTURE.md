# Architecture — how the pieces fit together

This doc is for contributors and the curious: the big picture, the data flow, and why things are shaped the way they are. For "how do I use it", see [API.md](API.md). Diagrams are Mermaid — GitHub renders them inline.

## The one-paragraph version

The planet is generated once (tectonics through biomes) into dense per-tile field layers plus sparse feature catalogs. Everything below the planet — region grids, local grids, placed sites — is derived on demand as a pure function of (world seed, address), inheriting the parent's scalars *and* direction so zoomed maps look like magnifications, not fresh rolls. Games persist things (overrides, location bindings, history) on their side; the library consumes them at query time and stores nothing itself.

## Tier hierarchy

```mermaid
flowchart TB
    P["Planet tier<br/>icosphere hex grid<br/>tectonics → elevation → climate →<br/>erosion → hydrology → biomes"]
    R["Region tier<br/>one hex grid per planet hex<br/>inherits scalars + direction"]
    L["Local tier<br/>one hex grid per region cell<br/>looser strike coupling, more rugged"]
    S["Sites<br/>injector passes over region/local maps<br/>ruins, cities, mines, landmarks"]

    P -->|"GetRegion(PlanetHex)<br/>address + seed"| R
    R -->|"GetLocal(cellIndex)<br/>address + seed"| L
    R -->|"WithInjectors"| S
    L -->|"WithInjectors"| S
```

Three things to notice:

- Arrows point one way. Children know their parent (every handle carries `Parent` and `Address`); parents know nothing about children. That's what keeps derivation storageless.
- Region and local maps are the same `IHexMap<TTile>` shape — flat-top hex grids with axial/cube addressing — so adjacency, ranges, and movement code is written once.
- Sites are an overlay, not a layer. They live in a managed `SiteCatalog`, never in the memory-mapped field store.

## Scale contract (which tier do I play on?)

Tiles = 10·4^size + 2. Measured Release numbers (Phase 2 baselines, seed 42):

| Size | Tiles | Gen time | Hex across | Region cell | Local tile |
|------|-------|----------|------------|-------------|------------|
| 3 | 642 | ~0.05 s | ~1000 km | ~126 km | ~8 km |
| 4 (visualizer default) | 2,562 | ~0.1 s | ~500 km | ~63 km | ~4 km |
| 5 | 10,242 | ~0.3 s | ~250 km | ~31 km | ~2 km |
| 6 | 40,962 | ~0.8 s | ~126 km | ~16 km | ~1 km |
| 7 (max supported) | 163,842 | ~2 s | ~63 km | ~8 km | ~0.5 km |
| 8 (unsupported) | 655,362 | ~20–30 s est. | ~31 km | ~4 km | ~0.25 km |

Tier → play use:

- **Planet** — strategic (continents, kingdoms). Playable at any size.
- **Region** (one hex grid per planet hex, ÷8) — operational (provinces).
- **Local** (one hex grid per region cell, ÷128) — overland travel; reaches
  1–4 km hexes at sizes 4–6.
- **Tactical** (sub-km) — game-owned: injectors + tactical maps. Even size 8
  planet hexes (~31 km) can't carry roguelike tactics, so planet density has
  steeply diminishing returns past size 6–7. Don't chase planet density;
  invest in the hierarchy.

Ceiling: `WorldBuilder.WithSize` accepts 0–7 (`MaxSupportedSize`) and throws
`ArgumentOutOfRangeException` beyond it. Size 8 is batch-only and
unsupported. The visualizer slider runs 1–7 and warns past 6 (~2 s
generation, ~1M mesh verts at size 7).

## Planet generation pipeline

```mermaid
flowchart LR
    subgraph store["WorldDataStore (memory-mapped, blittable structs)"]
        T["TectonicPlate<br/>crust, boundary,<br/>continentality, orogeny"]
        E["ElevationInfo<br/>height"]
        C["ClimateInfo<br/>temp, precip, wind"]
        H["HydrologyInfo<br/>flow, rivers,<br/>lakes, routing"]
        B["LocalMapInfo<br/>biome, danger, seed"]
    end
    subgraph cats["Managed catalogs (on World)"]
        RC["RiverCatalog"]
        WB["WaterBodyCatalog"]
        RG["RangeCatalog"]
        DP["DepositCatalog"]
    end

    S10["10 · tectonics"] --> T
    T --> S15["15 · elevation"]
    S15 --> E
    E --> S16["16 · climate"]
    S16 --> C
    E --> S17["17 · erosion"]
    C --> S17
    S17 --> E
    E --> S18["18 · hydrology"]
    C --> S18
    S18 --> H
    H --> RC
    H --> WB
    E --> RG
    C --> S20["20 · biomes"]
    H --> S20
    S20 --> B
    T & C & H --> DP
```

Notes:

- Stages are `IWorldGeneratorStage` with `[WorldGeneratorStage(Order)]` and `Reads`/`Writes` contracts — a blind second writer of a layer throws instead of silently winning. Erosion is the model citizen: it reads elevation and refines it.
- Field layers (dense, every tile) live in the store. Features (sparse, some tiles) live in catalogs. That split is load-bearing: it keeps the hot store cache-friendly and blittable while features stay flexible managed objects with tile-id lists and WKT export.
- Deposits seed *after* the build from finished tectonics + climate + hydrology — prospecting reads the finished world, it doesn't participate in shaping it.

## Zoom derivation: keeping the ridge in the north-west

```mermaid
flowchart TB
    PT["parent planet tile<br/>+ neighbor ring"]
    TO["TerrainOrientation.Sample<br/>gradient fit · orogeny strike ·<br/>means · flow entry/exit · wind"]
    PC["ParentContext<br/>scalars + direction"]
    BASE["base surface<br/>plane through parent mean<br/>carrying parent slope"]
    DET["detail<br/>RidgedFbm through<br/>anisotropic strike warp"]
    PIN["edge pinning<br/>blend outer cells to<br/>parent boundary values"]
    RIV{"parent IsRiver<br/>or high flow?"}
    THR["thread river<br/>entry → exit, tapered"]
    LOC["local drainage<br/>along inherited aspect"]
    MAP["child hex grid"]

    PT --> TO --> PC
    PC --> BASE --> DET --> PIN --> RIV
    RIV -->|yes| THR --> MAP
    RIV -->|no| LOC --> MAP
```

Why this shape:

- **Scalars alone lose direction.** Mean elevation + biome + moisture tells the child *what* the parent is, not *which way it tilts*. The gradient/strike/flow fields are what make a zoomed map read as "the same place, closer".
- **Strike comes from the orogeny field, not elevation.** Orogeny is smoother and is the actual belt signal; elevation already has hills and noise mixed in. When tectonics are present the strike also aligns with the drift-perpendicular, so ranges run along boundaries the way they do on Earth.
- **Edge pinning is what makes sibling maps stitch.** Without it, two adjacent child maps each roll their own boundary and you get cliffs along invisible seams. Pinning blends outer cells to parent-interpolated values over a configurable band (`EdgePinCells`).
- **Region follows tightly, local loosely.** `RegionDefault` uses strong across/along-strike warp (3.0 / 0.2); `LocalDefault` relaxes it (1.8 / 0.5) with a bit more amplitude — structure at region scale, ruggedness at local scale.

## Injector pipeline

```mermaid
flowchart LR
    H["materialized map<br/>RegionHandle / LocalMapHandle"]
    CTX["SiteInjectionContext<br/>address · parent · bounds ·<br/>existing sites · own RNG"]
    I1["Order 0<br/>SettlementInjector"]
    I2["Order 10<br/>RuinInjector"]
    I3["Order 20<br/>MineInjector"]
    IN["...your injector here"]
    CAT["SiteCatalog<br/>overlay"]

    H --> CTX --> I1 --> I2 --> I3 --> IN --> CAT
    I1 -.->|"ExistingSites"| I2
    I2 -.->|"ExistingSites"| I3
```

- Ordered passes, duplicate `Order` throws. Boring and predictable on purpose.
- Each injector's RNG is derived from (injector id, address) — adding a new injector never reshuffles earlier placements. Removing one doesn't either.
- Injectors see the map *with its boundaries* (`Bounds`: center, radius, edge-ring cells) plus the parent context, so placement can reason about "near the map edge" or "on the wet side".
- `NameSeed` is deliberately opaque: the library doesn't do fantasy names. It gives you a stable random draw; your name generator does the rest.

## Queries and persistence

```mermaid
flowchart TB
    subgraph lib["library (stateless)"]
        W["World facade"]
        IDX["SpatialIndex<br/>bucketed, exact early-stop"]
        DER["derivation<br/>pure functions"]
    end
    subgraph game["game side (owns state)"]
        HIST["IHistoricalContext<br/>events → tile modifiers"]
        MAT["IMaterializationStore<br/>overrides + location bindings"]
    end

    W --> IDX
    W --> DER
    HIST -.->|"QueryOptions.History"| W
    MAT -.->|"QueryOptions.Materialized"| W
```

The dotted lines are the whole philosophy: the library never stores game state and never simulates history. Both cross the boundary per-query through `QueryOptions`, and null means pure geography. Concretely:

- `IHistoricalContext` (Caves-of-Qud-style: your events projected onto tiles/sites) shifts danger/habitability.
- `IMaterializationStore` applies `TileOverride`s (replacement, not additive) and maps `MapAddress` ↔ your location ids. `MaterializationStore` is the in-memory implementation for tests and small games; a campaign manager implements the interface over its own DB.
- `SpatialIndex` answers nearest-feature queries without scanning the map. It's planet-tier; lower-tier addresses resolve to their parent planet tile.

## Determinism contract

Worth stating plainly since everything depends on it:

1. Same `(worldSeed, size, stage set)` → same planet. (`Rng` sub-streams keyed by seed + salt, never by iteration order.)
2. Same `(worldSeed, address)` → same child map, on any machine, in any derivation order. Parent fields are deterministic, and everything downstream is a pure function of them.
3. Same `(injector id, address, map)` → same sites. Injectors share no RNG state.
4. Overrides and history are the *only* non-deterministic inputs, and they arrive per-query from the game side.

If you ever find two runs disagreeing, it's a bug — the test suite has determinism tests at every tier, and they'd like to hear about it.

## File map

| Area | Files |
|---|---|
| Facade + builder | `World.cs` |
| Planet store + topology | `WorldDataStore.cs`, `WorldMap.cs`, `IcosphereGenerator.cs` |
| Generation stages | `TectonicPlateGenerationStage.cs`, `ElevationGenerationStage.cs`, `ClimateStage.cs`, `ErosionGenerationStage.cs`, `HydrologyStage.cs`, `LocalMapGenerationStage.cs` |
| Pipeline + contracts | `WorldGenerationPipeline.cs`, `IWorldGeneratorStage.cs`, `WorldGeneratorStageAttribute.cs`, `StageContractValidator.cs`, `ISeededStage.cs` |
| Field layers | `TectonicPlateLayer.cs`, `ElevationLayer.cs`, `ClimateInfo.cs`, `HydrologyInfo.cs`, `LocalMapInfo.cs`, `ElevationInfo.cs`, `IMapOverlay.cs`, `IMapLayer` bits |
| Hierarchy + derivation | `MapAddresses.cs`, `MapBounds.cs`, `ParentContext.cs`, `TerrainOrientation.cs`, `RegionMaps.cs`, `IHexMap.cs` |
| Sites | `SiteInjectors.cs` |
| Queries | `SpatialIndex.cs`, `TileFeatures.cs`, `CitySites.cs`, `Deposits.cs`, `FeatureCatalog.cs`, `Hydrography.cs`, `CanyonAnalysis.cs` |
| Game-owned state | `HistoryHooks.cs`, `Materialization.cs` |
| Randomness + noise | `Rng.cs`, `SphereNoise.cs`, `Glaciology.cs`, `PlatePartitioning.cs`, `ArenaDefaults.cs` |

## Performance notes (no benchmarks were harmed)

- Generation is the expensive part and happens once per world; it leans on contiguous spans, cached tile vectors, and a cached pipeline order so repeated runs stay allocation-light.
- Derivation is per-map and lazy — you pay for the hexes you zoom into, not the planet.
- Queries hit the spatial index, not full scans. The index build itself is O(tiles) once.
- The one thing to watch: `TileFeatures.Query` copies the elevation span for upstream/downstream tracing (single-tile inspector path, not a hot loop). If it ever shows up in a profile, that's the first place to look.
