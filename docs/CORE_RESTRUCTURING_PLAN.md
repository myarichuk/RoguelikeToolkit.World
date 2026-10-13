# Core restructuring plan

Status: **proposal, nothing moved yet.** Breaking changes are explicitly on the table, so this targets a 2.0.

## 1. What is actually wrong (measured, not vibes)

`src/RoguelikeToolkit.World.Core` today:

| Fact | Number |
|---|---|
| `.cs` files | 88 (72 sit loose in the project root) |
| Lines | ~15.4k |
| Top-level types (public + internal) | 215 |
| Namespaces | 4 (`Core`, `.Entities`, `.Names`, `.Localization`). **Everything else, 170+ types, is in one flat `RoguelikeToolkit.World.Core`.** |
| Public members missing XML docs (CS1591, currently `NoWarn`'d) | **751** |
| Consumers of `using RoguelikeToolkit.World.Core;` | 80 files outside Core, 177 files reference the assembly |

The flat namespace is the real problem; the flat folder is merely its symptom. Moving files alone would give you pretty
folders and exactly the same IntelliSense swamp. Worse, nothing stops a "Climate" file from reaching into "Sites"
internals today; the dependency graph is whatever grew organically.

### Specific rot I found

1. **One file, many unrelated types.** `SiteInjectors.cs` has 14 types (enums, context, two internal surfaces, an
   interface, a catalog, a pipeline and four concrete injectors) in 507 lines. `Names/NameTypes.cs` has 12.
   `FeatureCatalog.cs` has 12 (river, water body, range, three catalogs, WKT writer). `HistoryHooks.cs` has 6,
   `MapAddresses.cs` 6, `World.cs` holds `World` + `WorldBuilder` + `TileHit`, `GeoCoord.cs` also contains
   `Vector2D` and `Vector3D`, `IHexMap.cs` also contains `CubeCoord` and `OffsetGrid`, `LandCapacity.cs` contains *two
   pipeline stages*, `HydrologyStage.cs` hides `RangeCatalogBuilder`.
2. **File names that lie.** `IMapOverlay.cs` declares `IMapLayer`. `LocalMapInfo`/`LocalMapLayer`/`LocalMapGenerationStage`
   are the **biome** layer (they hold `BiomeType` and danger) but collide linguistically with the *local tier*
   (`LocalMapHandle`, `LocalRef`, `MapTier.Local`). A newcomer reading `LocalMap*` will assume tier-3 grids. They'd be wrong.
3. **Concept clusters are scattered alphabetically.** To understand climate you open `ClimateInfo`, `ClimateModel`,
   `ClimateStage`, `ClimateRefreshStage`, `OceanCurrents`, `SeaIce`, `Glaciology`, which sit between `ChildGridBuilder`
   and `Deposits` in the listing. Ditto hydrology (`Hydrography`, `HydrologyInfo`, `HydrologyStage`, `FeatureCatalog`,
   `TileFeatures`).
4. **Giant files carrying several jobs.** `NameGenerator` 944, `Hydrography` 704 (it also hosts `Lake`, `LakeSolution`,
   `LakeSolver`), `WorldDataStore` 666, `ErosionGenerationStage` 536, `Entities/EntityStore` 573.
5. **Generic names squatting in the root namespace.** `Conflict`, `Lake`, `River`, `Column`, `Rng`, `Vector2D/3D`,
   `Geology`, `Deposit`. Any of these can clash with a consumer's own types the moment they add a second `using`
   (`Vector3D` is the likeliest offender next to `System.Numerics`/Unity code).
6. **Infrastructure in the domain root.** `PluginLoadContext`, `StageContractValidator`, `Rng`, `DetMath` live next to
   `BiomeClassifier`.
7. **Doc coverage is aspirational.** 751 public members are undocumented and the warning is switched off, so
   it can only get worse.

Things that are *fine* and should be left alone: `Entities/`, `Names/`, `Localization/` already have a home and a
namespace; stage discovery is attribute-driven (`[WorldGeneratorStage]`), so moving stage classes between namespaces
does not break discovery.

## 2. Target shape

Principle: **vertical slices by domain concept, horizontal layers only for genuinely shared infrastructure.** A slice
owns its blittable layer struct, `IMapLayer` implementation, algorithm(s), stage and catalog. "Where's the climate
code?" has a one-word answer.

Namespace == folder, always (enforced, see 6).

```
Core/                               RoguelikeToolkit.World.Core            the 80 % entry point
  World.cs  WorldBuilder.cs  TileHit.cs  PlanetParameters.cs  (+IInsolationModel, TidallyLockedInsolation)
  Primitives/                       .Core.Primitives                       dependency-free math
  Topology/                         .Core.Topology                         the sphere + hex grids
  Storage/                          .Core.Storage                          dense per-tile layers
  Generation/                       .Core.Generation                       pipeline + stage contracts (no domain knowledge)
  Tectonics/  Elevation/  Climate/  Hydrology/  Biomes/  Lithosphere/  Settlement/   one namespace each
  Features/                         .Core.Features                         sparse per-tile features, WKT
  Hierarchy/                        .Core.Hierarchy                        planet to region to local derivation
  Sites/                            .Core.Sites                            injector pipeline + built-in injectors
  Integration/                      .Core.Integration                      game-side seams (history, materialization)
  Entities/  Names/  Localization/  (unchanged)
  Internal/Compat/                  internal only (polyfills, netstandard shims, PriorityQueue)
```

### Allowed dependency direction (the part that makes it "orderly" rather than "tidy")

```
Primitives
   ^
Topology  <-  Storage  <-  Generation
                              ^
        Tectonics, Elevation, Climate, Hydrology, Biomes, Geology, Settlement   (domain slices; may read earlier
                              ^                                                  slices' *layer structs*, never their algorithms)
        Features, Hierarchy
                              ^
                    Sites, Integration           <-  World (facade, may see everything)
```

Rules, each one enforced by a test:

- `Primitives` references nothing in Core.
- `Generation` and `Storage` know no concrete domain slice.
- A domain slice may depend on `Primitives`, `Topology`, `Storage`, `Generation` and on *layer data types* of
  slices earlier in stage order. Climate reading `ElevationInfo` is legitimate; Climate calling `ErosionGenerationStage` is not.
- `Hierarchy`/`Sites`/`Integration` sit above the planet slices; nothing below them references up.
- Only `World`/`WorldBuilder` reference everything.

Cycles will surface the first time the test runs. Expect a handful (see risks); each one is a real design smell
that the folder move merely reveals, not creates.

### File-by-file destination map

| New home | Contents (current file, split noted) |
|---|---|
| `Primitives/` | `Rng`, `DetMath`, `SphereNoise`, `TileOrdering`, `GeoCoord`, `Vector2D`+`Vector3D` (split out of `GeoCoord.cs`), `CubeCoord`+`OffsetGrid` (split out of `IHexMap.cs`) |
| `Topology/` | `IHexMap` (interface only), `WorldMap`, `IcosphereGenerator` (+`TriangleIndices`), `GridProjection` (+`TangentFrame`), `SpatialIndex` (+`FeatureKind`) |
| `Storage/` | `WorldDataStore`, `StoreLayer`, `IMapLayer` (file renamed from `IMapOverlay.cs`), `UserEditInfo` (flags, info, layer) |
| `Generation/` | `WorldGenerationPipeline` (+`StageDiscoveryDiagnostic` split), `IWorldGeneratorStage`, `IDeclaredStage`, `ISeededStage`, `WorldGeneratorStageAttribute`, `StageMetadata` (+`IStageNamed`), `StageContractValidator`, `PluginLoadContext` |
| `Tectonics/` | `TectonicPlateLayer` (split: `CrustType`, `PlateBoundaryType`, `PlateInfo`, `TectonicPlate`, layer), `TectonicPlateGenerationStage`, `PlatePartitioning` (split: `IPlatePartitioner`, `Voronoi...`, `NoisyFloodFill...`) |
| `Elevation/` | `ElevationInfo`, `ElevationLayer`, `ElevationGenerationStage`, `ErosionGenerationStage` (decomposition candidate, see 4.5), `TerrainOrientation` stays in Hierarchy |
| `Climate/` | `ClimateInfo` (+`ClimateLayer`), `ClimateModel`, `ClimateStage`, `ClimateRefreshStage`, `OceanCurrents` (internal), `SeaIce`, `Glaciology` |
| `Hydrology/` | `HydrologyInfo` (+`HydrologyLayer`), `HydrologyStage`, `Hydrography`, `Lake`/`LakeSolution`/`LakeSolver` (split from `Hydrography.cs`), `River`, `RiverTerminal`, `WaterBody`, `WaterBodyKind`, `RiverCatalog`, `WaterBodyCatalog` (all split from `FeatureCatalog.cs`), `CanyonAnalysis` |
| `Biomes/` | `BiomeType`, **`BiomeInfo`/`BiomeLayer`/`BiomeGenerationStage`** (renamed from `LocalMapInfo`/`LocalMapLayer`/`LocalMapGenerationStage`), `BiomeClassifier` (+`AridSite`), `LandCover` |
| `Lithosphere/` | (was `Geology/`; a namespace `Core.Geology` would be ambiguous with the `Geology` class) `Geology`, `CrustalProvince`, `Deposits` (split: type, size, `Deposit`, catalog, builder), `RangeKind`/`RangeFeature`/`RangeCatalog`/`RangeCatalogBuilder`, `IslandChains` (options, chain, detector), `IslandChainStage` |
| `Settlement/` | `CitySites` (filter, score, scorer), `SettlementScoring`, `LandCapacity` (+`LandSite`), `LandCapacityStage`, `LandUseResolveStage` (split into two files), `LandUse` (id, activity, infos, layers) |
| `Features/` | `TileFeatures`, `TileFeatureInfo`, `RiverWaterSource`, `GeoWkt` |
| `Hierarchy/` | `MapAddresses` (split: `MapTier`, `PlanetHex`, `RegionRef`, `LocalRef`, `MapAddress`, `MapSeeds`), `MapBounds`, `ParentContext`, `TerrainOrientation`, `ChildContextDeriver`, `ChildGridBuilder`, `RegionMaps`, `RegionHandle` (+`RegionCell`), `LocalMapHandle` (+`LocalTile`), `GridMapHandle`, `RegionDetailOptions` |
| `Sites/` | `SiteInjectors.cs` exploded into ~10 files: `SiteTier`, `SiteKind`, `PlacedSite`, `SiteInjectionContext`, `ISiteInjector`, `SiteCatalog`, `SiteInjectorPipeline`, `SiteInjectorBase`, `Injectors/{Settlement,Ruin,Mine,Landmark}Injector`, internal `ITierSurface`/`RegionSurface`/`LocalSurface` |
| `Integration/` | `IHistoricalContext`, `TileHistoryModifier`, `HistoricSite`/`HistoricSiteKind`/`HistoricSiteIndex`, `QueryOptions`, `TileOverride`, `IMaterializationStore`, `MaterializationStore` |
| `Internal/Compat/` | existing `Compat/*` (internal, namespace stays `Core` or becomes `Core.Internal`, since polyfills such as `IsExternalInit` must keep their BCL namespace) |

(Placement of a few items, such as `CanyonAnalysis`, `TerrainOrientation`, `TileFeatures`, is a judgement call; the
dependency test will tell us if a choice is wrong.)

## 3. Decision: namespaces, assemblies, or just folders?

| Option | Verdict |
|---|---|
| A. Folders only, one namespace | Zero consumer churn, zero benefit. Rejected. |
| **B. Folders + matching namespaces, one assembly** | **Recommended.** Real discoverability, enforceable layering, no packaging change. |
| C. Split into several assemblies (`Core.Abstractions`, `Core.Climate`, ...) | Strongest layering, but multiplies the Unity UPM packaging (`tools/upm/pack.py` builds per-project netstandard2.1 DLLs + `.meta` GUIDs), AOT trimming and NuGet surface by N for a library of 15k lines. Revisit only if someone needs to ship a subset. The architecture tests in 6 give 90 % of the benefit. |

For the 80 % case keep the facade small: `World`, `WorldBuilder`, `MapAddress`, `TileHit`, `QueryOptions` and
`PlanetParameters` should be reachable from `using RoguelikeToolkit.World.Core;` alone. (This means `MapAddress`
etc. are the exceptions that live in `Hierarchy/` *folder* but arguably want root *namespace*; decide once, see
open questions.)

## 4. Breaking changes worth taking (since you said they're free)

Each is optional and independent; they go in their own commits so any can be dropped.

1. **Rename the biome layer** (`LocalMap*` to `Biome*`). Highest value in the whole plan; removes a genuine naming collision.
2. **Nest or rename generic names:** `StageContractValidator.Conflict` to a nested type, `Column`/`ColumnBase` into
   `EntityStore`, `Lake`/`River` fine once namespaced; consider `Vector2D/3D` to `Vec2d/Vec3d` or at least keep them in `Primitives`.
3. **Tighten visibility.** Run the public API through a trim pass: `*Catalog` builders, `TangentFrame`,
   `MapSeeds`, `SiteInjectorBase` helpers and similar probably do not need to be public. Anything `public` that no sample,
   test, app or Jint binding touches is a candidate. `Microsoft.CodeAnalysis.PublicApiAnalyzers` makes this
   a reviewed diff instead of guesswork (see 6).
4. **Stage order constants.** Orders are bare ints in attributes (10, 15, 16, 17, 18, 20 per `ARCHITECTURE.md`). A single
   `StageOrder` static (`Tectonics = 10`, ...) kills a class of silent ordering bugs. Verify current usage before committing.
5. **Decompose the giants** (behavioural no-ops, golden-tested):
   - `World.cs` to `World` (queries) + `WorldBuilder` + `TileHit`.
   - `WorldDataStore` (666) to storage core + typed accessors; check what is really in there before splitting.
   - `Hydrography` (704) to flow routing / lake solving / river extraction as `internal static` collaborators.
   - `NameGenerator` (944) to `partial` per concern (place names, person names, lineage), or collaborators if state allows.
   - `ErosionGenerationStage` (536): stage shell + `internal` solver.
   - `EntityStore` (573): store / column / delta-tracking.
6. **Plugin surface.** `PluginLoadContext` and the discovery logic in `WorldGenerationPipeline` become
   `Generation/Plugins/`; this is where `samples/DesertPlugin` and the Jint stage plug in, so document it as *the*
   extension point.

## 5. Documentation plan

Status quo: CS1591 is suppressed and 751 public members have no `<summary>`.

1. **Per-slice README + `NamespaceDoc`.** One short `README.md` per top-level folder (what lives here, what it may
   depend on, which stage order(s) it owns) and a `/// <summary>` on a `NamespaceDoc` class in each namespace so
   it appears in generated docs and IntelliSense. 
2. **XML docs, in priority order by blast radius:**
   1. Everything reachable from `World`/`WorldBuilder` (the 80 % API).
   2. Extension seams: `IWorldGeneratorStage`, `IDeclaredStage`, `ISeededStage`, `[WorldGeneratorStage]`, `ISiteInjector`,
      `IHistoricalContext`, `IMaterializationStore`, `IMapLayer`, `IPlatePartitioner`, `IInsolationModel`.
   3. Blittable layer structs: every field with **units and range** (`Temperature`: degrees C or K? `Precipitation`:
      mm/yr or 0..1?). This is the documentation that is most expensive to reverse-engineer from code.
   4. Algorithms: one `<remarks>` paragraph on *what model* and *which determinism-relevant choices* (salts, sub-stream keys).
   5. `Names/NameTypes.cs` (176 of the 751 gaps, mostly enum members and constants; cheap).
3. **Inline "why" comments** only where the code encodes a non-obvious invariant (determinism salts, the edge-pin
   band, the neighbour-ring ordering). No restating the code.
4. **Rewrite `docs/ARCHITECTURE.md` "File map"** from the table above and link each slice README. Add a short
   `docs/MIGRATION_2.0.md` (old name to new name/namespace). 
5. **Ratchet, not cliff.** Land the docs per slice, and flip `CS1591` from suppressed to a *warning*, then to *error* for
   a slice once it reaches zero. Final state: `GenerateDocumentationFile` + `TreatWarningsAsErrors` for CS1591 on Core.
6. Examples: `<example>` blocks on `WorldBuilder`, `World.GetRegion`, `SiteInjectorPipeline`, and one stage plugin.
   They double as compile-checked snippets if lifted into the `Core.Tests` project.

## 6. Guardrails (so it stays orderly)

- **Architecture tests** in `Core.Tests` (no new dependency required; reflection over the assembly is enough, or
  NetArchTest if you prefer): the dependency rules in section 2, plus "namespace == folder", "one top-level public type per file",
  "no public type in an `Internal` namespace".
- **`.editorconfig`**: `IDE0130` (namespace matches folder) as error, `dotnet_style_namespace_match_folder = true`,
  file-scoped namespaces.
- **`PublicApiAnalyzers`** with `PublicAPI.Shipped.txt` / `Unshipped.txt` so every API change is a visible diff in the PR.
  Generate the baseline *before* the move so it also doubles as the rename audit.
- **Golden determinism snapshot** (see Phase 0). This is the single most important safety net, since the project's headline
  promise is bit-reproducible worlds.

## 7. Execution phases

Every phase is independently mergeable, ends with `dotnet build` (both TFMs) + full test run green, and is
history-preserving (`git mv`, moves in commits that contain **no** content edits).

**Phase 0, safety net (no production change).**
- Run the whole suite and record the baseline, including the netstandard2.1 build and `tests/...AotSmoke`.
- Add a determinism golden test: hash the dense store + catalogs + a region + a local map + injected sites for
  a few `(seed, size)` pairs (e.g. 42 at sizes 3 and 4, plus a tidally-locked planet). Commit the hashes. The refactor
  must never change them (phases 1 to 3 especially). `DeterminismTests` exists; extend it to cover *everything* and pin to constants.
- Generate the PublicAPI baseline files.
- Check `tools/upm/pack.py` and `upm/` have no path or namespace assumptions on Core internals (preliminary look: link.xml is
  assembly-scoped, only `Editor/` uses a `Core.Editor` namespace; both are unaffected).

**Phase 1, pure file moves.** Create the folders from section 2, `git mv` whole files, namespaces *unchanged*. Zero
consumer impact. Already a large readability win on its own, and a safe stopping point.
Careful: `Names/Data/*.yaml` and `Localization/*.yaml` are matched by embedded-resource *name* (`LocaleStore`, `NameData`
enumerate `GetManifestResourceNames()` by suffix/prefix). **Do not move those files** in this phase; if they ever move,
pin names with `LogicalName` in the csproj.

**Phase 2, split multi-type files.** One type per file (nested/private helpers excepted). Mechanical; no
semantic edits. Fixes the "file names lie" problem. Rename `IMapOverlay.cs` to `IMapLayer.cs` here.

**Phase 3, namespaces follow folders.** Roslyn-driven (IDE "sync namespace" / `dotnet format` / a small script),
then fix consumers: 80 `using` lines in src/tests/samples, the Jint binding, `DesertPlugin`, the App/Presentation/MapEditor/NameTester
projects, the UPM Editor script. Add per-project `GlobalUsings.cs` in tests and apps if the churn offends you. Documentation snippets in
`README.md`/`API.md`/`PLUGINS.md` get a find-and-replace pass.
After this phase the architecture test is turned on, in *report* mode first; list the cycles; fix or consciously waive each.

**Phase 4, breaking cleanups from section 4**, one commit per item, golden hashes must stay identical (renames and
visibility changes are not allowed to touch behaviour).

**Phase 5, decompose giants.** Strictly after phases 0 to 3, because moving and rewriting in the same diff is how you lose a week
bisecting. Each decomposition in its own PR with the golden test + existing unit tests as the net. Benchmarks
(`tests/...Benchmarks`) before/after for the hot ones: hydrology, climate, `SpatialIndex`. A refactor that makes size-7 generation
slower than the ~2 s baseline in `ARCHITECTURE.md` is a regression, not a cleanup.

**Phase 6, documentation drive.** In parallel with 4 and 5, per slice, ratcheting CS1591 as in section 5. New slice README
files; rewrite the `ARCHITECTURE.md` file map; write `MIGRATION_2.0.md`; update `CHANGELOG` via a `feat!:` conventional commit (the repo
uses release-please, so the `!` drives the 2.0.0 bump).

Suggested PR sequence: P0 | P1 | P2 | P3 | P4 (n small PRs) | P5 (one PR per giant) | P6 per slice. Phases 1 and 2 could land in a single
afternoon; Phase 3 is the first one that touches downstream code.

## Progress log

- **Phase 0 done:** `GoldenWorldTests` pins SHA-256 fingerprints for (42,3), (7,4) and a tidally-locked (42,3). Baseline suite: 538 pass, 2 fail
  (`NameQualityTests.PersonNames_HaveVariety`, `Catfolk_AreSingleStoryNames_WithAnOptionalDeed`; both fail before any refactor).
  PublicApiAnalyzers baseline is deferred to the start of Phase 3 (it would add ~750 RS0016 noise warnings now).
- **Phase 1 done:** all root files moved into slice folders with `git mv`, namespaces untouched. Lesson: `Scripting.Jint.csproj` compiles
  `Compat/CompilerPolyfills.cs` straight out of Core by path, so csproj path links are a second place to check (fixed).
  Still pending from the plan: the `Names/Data` and `Localization` YAML stay put (resource names).
  `docs/PLANET_SCALE_BACKLOG.md` has absolute `C:/projects/...` file links that were already non-portable; they will be refreshed in Phase 6.

### Phase 2 - done
- Every multi-type file split into one top-level type per file with a brace-aware script (strings, comments and attribute `new[] { }` initializers handled; the script verified that no non-whitespace source text was dropped). Nested types were left alone.
- `IMapOverlay.cs` renamed to `IMapLayer.cs` (it holds `IMapLayer<T>`); `IcosphereGenerator.cs` moved from a block-scoped namespace to a file-scoped one and `TriangleIndices` split out.
- Intentionally left alone: `Internal/Compat/*` (polyfills, linked by path into the Jint project) and `Generation/PluginLoadContext.cs` (top-level `#if`).
- Build and tests identical to the Phase 0 baseline; golden hashes unchanged.

### Phase 3 - done
- Every Core file's namespace now equals its folder (`RoguelikeToolkit.World.Core.<Slice>`); the root namespace keeps only `World`, `WorldBuilder`,
  `TileHit`, `PlanetParameters`, `IInsolationModel`, `TidallyLockedInsolation` and the internal compat shims (**strict** option for open decision 2: `MapAddress`
  and `QueryOptions` are in `Hierarchy`/`Integration`; relaxing that later is a one-line `namespace` edit per file).
- Mechanics: namespace rewrite by script, then every file got all slice usings and `dotnet format style --diagnostics IDE0005` pruned
  the unused ones, so the surviving `using` lines are the true dependency graph. 266 files touched across src/tests/samples. Docs snippets updated.
- `.editorconfig` added (namespace-matches-folder as `suggestion`; IDE0005 left at `suggestion` too, flip to `warning` to enforce).
- `ArchitectureTests` added: namespace==folder, one top-level type per file, `Primitives` depends on nothing, and a **ratchet** on layer direction.
  The first run found 25 upward edges (all listed with a reason in the test, none new) and one cyclic group containing 14 slices: the domain
  slices read each other's layer structs in both directions, and `Topology`/`Storage`/`Generation` each reach up into domain slices
  (`SpatialIndex`, `UserEditInfo`, the pipeline's AOT `DynamicDependency` hooks). Those are Phase 4/5 material, not a reason to block the move.
- **Found, not fixed:** `WorldDataStore.LayerNameHash` and `EntityStore.TypeHash` FNV-hash `typeof(T).FullName` into persisted file headers.
  Moving namespaces therefore invalidates store/entity files written by 1.x (they fail the header check loudly, they do not corrupt). Queued for Phase 4:
  hash a namespace-independent stable id and bump the file version, so the next move does not repeat this.
- Build: 0 warnings / 0 errors on net10.0 and netstandard2.1. Tests unchanged versus baseline (Core 541 pass + the same 2 pre-existing failures, Presentation 103, Jint 18, MapEditor.Engine 36), golden hashes identical.

### Phase 4 - done
- `LocalMapInfo`/`LocalMapLayer`/`LocalMapGenerationStage` are now `BiomeInfo`/`BiomeLayer`/`BiomeGenerationStage`. `LocalMapHandle` and `DeriveLocalMap` keep their names: they are the local-map *tier*.
- Persisted ids: `StableTypeId` (declaring chain + simple name + generic args, no namespace) replaces `Type.FullName` in the store and entity headers. `WorldDataStore` file version 3 to 4, entity snapshot 3 to 4, delta 1 to 2. Pinned by `StableTypeIdTests`. Renaming a persisted type still changes its id.
- `StageOrder` holds every built-in stage order; `PipelineOrder`/`StageOrder` consts on individual stages are gone.
- Visibility: `StageContractValidator`, `StageMetadata`, `PluginLoadContext`, `LocaleData`, `Hydrography`, `LakeSolver`, `LakeSolution`, `IslandChainDetector`, `EntityKeyMap`, `WorldAdjacency` are `internal`. `NamePart`, `CultureData`, `MorphemeYaml` stay public (they leak through `GeneratedName`/`NameDataStore`).
- Nesting: `StageContractValidator.Conflict` and `EntityStore.Column<T>` were already nested; the `Vector2D/3D` rename was dropped as pure churn now that they live in `Primitives`.
- Waivers burned down 25 to 20: addresses (`MapAddress`, `MapTier`, `PlanetHex`, `RegionRef`, `LocalRef`) and `MapBounds` moved to `Topology`; `SpatialIndex`/`FeatureKind` moved to a new `Queries` slice (rank 5). Remaining waivers are the pipeline's AOT hooks, `UserEditInfo`, Hydrology/Features and the aggregators.
- Golden hashes identical; tests as baseline (Core 548 + the same 2 pre-existing failures).

## 8. Risks / things I have not verified yet

- **Hidden cycles.** I have not computed the real type-level dependency graph; the layering in section 2 is inferred from
  names and `ARCHITECTURE.md`. Expect `TileFeatures`, `HistoryHooks`, `World` and `Hydrography` to be the cyclic ones. Phase 3's
  report-mode test will answer this with data.
- **Reflection by name.** Stages are discovered by attribute (safe), but `AotYamlObjectFactory`, `[Preserve]` and the Jint
  binding resolve some types by CLR type or name (`typeof(TectonicPlate)`, `ResolveRead(...)`). Need to confirm nothing
  is keyed on `FullName` strings (script-side layer names, YAML DTOs, `PLUGINS.md` examples) before changing namespaces. The
  rename in 4.1 especially.
- **netstandard2.1 / Unity.** Namespaces are source-level and invisible to `link.xml` (assembly-scoped), but I have not
  run the AOT smoke or a Unity import after any move. Phase 0 baselines them.
- **`InternalsVisibleTo`** only grants `Core.Tests`. Several internals (`ChildGridBuilder`, `OceanCurrents`) are tested
  directly; keep them `internal` and keep tests compiling, rather than widening to public to make a move easier.
- **Plugin binary compatibility.** Out-of-tree plugins compiled against 1.x will fail to load stage types after namespace changes
  (type identity includes namespace). Unavoidable for 2.0; call it out in the migration doc.
- **Docs correctness.** Writing 751 summaries mechanically produces confident wrong text. Units/ranges in particular must be read off the
  algorithms, not guessed; this is the part I would not parallelise blindly.

## 9. Decisions I need from you

Defaults in bold; I will proceed with these unless told otherwise.

1. Namespaces match folders (option B) rather than staying flat: **yes**.
2. Which names stay in the root namespace for the "80 %" API? **`World`, `WorldBuilder`, `TileHit`, `PlanetParameters`,
   `QueryOptions`, `MapAddress` and friends** (so `Hierarchy` types addresses are an exception to namespace==folder), or **strict** (everything in its folder's namespace, and consumers add 2 to 4 usings)? Strict is cleaner; facade-in-root is friendlier.
3. Do the `LocalMap*` to `Biome*` rename and the other section 4 items: **yes, all, as separate droppable commits**.
4. Version: **2.0.0** with a migration doc.
5. Should this stay within the single assembly (**yes**) with architecture tests as the boundary?
