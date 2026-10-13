# Migrating from 1.x to 2.0

2.0 reorganises `RoguelikeToolkit.World.Core` into vertical slices (one folder, one namespace each). **Generation output is unchanged**:
the golden SHA-256 fingerprints in `GoldenWorldTests` are identical before and after, so the same `(seed, size, stage set)` still yields the same planet.
What breaks is source compatibility (namespaces, a few renames and visibility changes), the persisted file formats, and the binary compatibility of out-of-tree plugins.

## 1. Namespaces

Everything that used to live in `RoguelikeToolkit.World.Core` now lives in `RoguelikeToolkit.World.Core.<Slice>`. Only the facade stays in the root namespace:
`World`, `WorldBuilder`, `TileHit`, `PlanetParameters`, `IInsolationModel`, `TidallyLockedInsolation`.

Most code needs a handful of added `using` lines. Where each public type went:

| Namespace (`RoguelikeToolkit.World.Core.` +) | Types |
|---|---|
| `Primitives` | `DetMath`, `GeoCoord`, `Rng`, `SphereNoise`, `TileOrdering`, `Vector2D`, `Vector3D` |
| `Topology` | `CubeCoord`, `IHexMap<T>`, `IcosphereGenerator`, `LocalRef`, `MapAddress`, `MapBounds`, `MapTier`, `OffsetGrid`, `PlanetHex`, `RegionRef`, `TriangleIndices`, `WorldMap` |
| `Storage` | `IMapLayer<T>`, `StoreLayer<T>`, `UserEditFlags`, `UserEditInfo`, `UserEditLayer`, `WorldDataStore` |
| `Entities` | `CellIndex`, `EntityId`, `EntityStore`, `IAdjacency`, `ICellOf<T>`, `IPopulationPolicy`, `PopulationGrid` |
| `Names` | `FantasyNameGenerator`, `GeneratedName`, `NameStyle`, `NameLint`, `Cultures`, `FantasyRaces`, `PlaceKinds`, `NameDataStore`, ... |
| `Localization` | `LocaleStore`, `LocaleDataException` |
| `Generation` | `IWorldGeneratorStage`, `WorldGeneratorStageAttribute`, `ISeededStage`, `IDeclaredStage`, `IStageNamed`, `StageOrder`, `WorldGenerationPipeline`, `StageDiscoveryDiagnostic` |
| `Tectonics` | `TectonicPlate*`, `PlateInfo`, `CrustType`, `PlateBoundaryType`, `IPlatePartitioner`, `VoronoiPlatePartitioner`, `NoisyFloodFillPlatePartitioner` |
| `Elevation` | `ElevationGenerationStage`, `ErosionGenerationStage`, `ElevationInfo`, `ElevationLayer` |
| `Climate` | `ClimateStage`, `ClimateRefreshStage`, `ClimateModel`, `ClimateInfo`, `ClimateLayer`, `Glaciology`, `SeaIce`, `SeaIceKind` |
| `Hydrology` | `HydrologyStage`, `HydrologyInfo`, `HydrologyLayer`, `Lake`, `CanyonAnalysis`, `RangeCatalogBuilder` |
| `Biomes` | `BiomeGenerationStage`, `BiomeClassifier`, `BiomeInfo`, `BiomeLayer`, `BiomeType`, `LandCover` |
| `Lithosphere` | `Deposit*`, `Geology`, `CrustalProvince`, `IslandChain*` |
| `Settlement` | `LandCapacity*`, `LandUse*`, `LandActivity`, `LandSite`, `CitySiteScorer`, `CitySiteFilter`, `CitySiteScore`, `SettlementScoring` |
| `Features` | `RiverCatalog`, `WaterBodyCatalog`, `RangeCatalog`, `River*`, `WaterBody*`, `Range*`, `TileFeatures`, `TileFeatureInfo`, `GeoWkt` |
| `Hierarchy` | `RegionMaps`, `RegionHandle`, `LocalMapHandle`, `GridMapHandle<T>`, `LocalTile`, `RegionCell`, `RegionDetailOptions`, `ParentContext`, `TerrainOrientation`, `MapSeeds` |
| `Sites` | `ISiteInjector`, `SiteInjectorBase`, `SiteInjectorPipeline`, `SiteInjectionContext`, `SiteCatalog`, `PlacedSite`, `SiteKind`, `SiteTier`, `*Injector` |
| `Integration` | `QueryOptions`, `IHistoricalContext`, `TileHistoryModifier`, `HistoricSite*`, `IMaterializationStore`, `MaterializationStore`, `TileOverride` |
| `Queries` | `SpatialIndex`, `FeatureKind` |

Notable moves: `MapAddress` and friends are in **`Topology`** (not `Hierarchy`), `SpatialIndex` is in **`Queries`**, `QueryOptions` is in **`Integration`**.

## 2. Renames and removals

- `LocalMapInfo` / `LocalMapLayer` / `LocalMapGenerationStage` are now `BiomeInfo` / `BiomeLayer` / `BiomeGenerationStage`. `LocalMapHandle` and `DeriveLocalMap` keep their names: they are the local-map *tier*.
- Per-stage order constants (`PipelineOrder`, `StageOrder` on individual stages) are gone. Use `StageOrder.Tectonics`, `StageOrder.Elevation`, `StageOrder.Climate`, `StageOrder.Erosion`, `StageOrder.Hydrology`, `StageOrder.Biomes`, ... from `Generation`.
- Files and types are split one-per-file; this does not change the public surface except as listed here.

## 3. Visibility

These were public in 1.x and are now `internal`: `StageContractValidator`, `StageMetadata`, `PluginLoadContext`, `LocaleData`, `Hydrography`, `LakeSolver`, `LakeSolution`, `IslandChainDetector`, `EntityKeyMap`, `WorldAdjacency`.
`NamePart`, `CultureData` and `MorphemeYaml` stay public because they appear in public signatures.

## 4. Persisted formats (hard break)

Type identity in file headers is now `StableTypeId`: a hash of the declaring chain, simple name and generic arguments, **without the namespace**. 1.x hashed `Type.FullName`, so a namespace move would have invalidated every file; 2.0 makes future moves safe.

| Format | 1.x | 2.0 |
|---|---|---|
| `WorldDataStore` file | version 3 | **4** |
| `EntityStore` snapshot | version 3 | **4** |
| `EntityStore` delta | version 1 | **2** |

Files written by 1.x fail the header check loudly; they are never silently misread. There is no in-place upgrader: regenerate the world from `(seed, size, stages)` (it is deterministic), and re-save entity data from your own source of truth.
Renaming a persisted layer or column type still changes its id, exactly as before.

## 5. Plugins (binary break)

Type identity includes the namespace, so out-of-tree stage assemblies compiled against 1.x will not load: recompile them against 2.0 after adding the `using` lines above.
Stage discovery itself is unchanged (`[WorldGeneratorStage(StageOrder.X, ...)]`); see [PLUGINS.md](PLUGINS.md).

## 6. Unity / AOT

Namespaces are source-level and invisible to assembly-scoped `link.xml`, so existing linker config keeps working. The `[DynamicDependency]` hooks that keep built-in stages alive under trimming remain on `WorldGenerationPipeline`. I have not re-run the AOT smoke test or a Unity import since the namespace moves, so treat those two as unverified.

## Behaviour changes after the first 2.0 draft

- `ErosionGenerationStage(0)` / `Iterations = 0` no longer disables erosion; use `Enabled = false`.
- `GeoWkt` water-body polygons and range lines have a different vertex order, and empty geometry is `LINESTRING EMPTY` / `POLYGON EMPTY`.
- `WorldDataStore.GetRef<T>` throws `InvalidOperationException` for table types.
