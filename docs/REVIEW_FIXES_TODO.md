# Review fixes: cross-session state

Companion to [REVIEW_2026-10.md](REVIEW_2026-10.md) (the findings and their evidence). **This file is the
memory**: a session may be compacted or cleared between batches. Keep it current and terse.

## Agent instructions (every session)

1. **Start:** read this file, then only the review rows for the current batch. Do not re-review the repo.
2. **One batch per commit.** Check a box (`- [x]`) only after its test passes. Never batch checkmarks.
3. **Log** under the batch: files touched, test command and result, any re-baselined golden value. Max ~8 lines.
4. **Blocked:** mark `- [!]` with a one-line reason and move on to the next item.
5. **Done for a batch:** `dotnet test` for every touched test project is green, the boxes are checked, the log is
   written, and the commit is pushed to the branch below.
6. **Before stopping** (end of a batch, context getting long, or user asks), rewrite the
   **Rehydration** block below so a fresh session can resume from it alone.

Branch: `claude/gallant-knuth-1ctv0z`. Build/test: `dotnet build RoguelikeToolkit.World.slnx -c Release`,
`dotnet test tests/<Project> -c Release --no-build`. netstandard check: `-p:CoreTargetFramework=netstandard2.1`.

## Rehydration

> Batches A-E (review section 1) done and pushed. User asked (2026-10-25) for: **F** history defects, **G** hydrology
> defects (review section 2), then **H** 2D-map/history-overlay issues (diagnosis delegated, results go in H), then the
> history expansion (battles, logistics events, advisors/merchant princes/rebels/outlaws; design in
> `docs/HISTORY_EXPANSION_PLAN.md` once written). Current batch: **H** (start with H1 cost-weighted territory).

## Batch A — determinism

- [x] A1 `ClimateStage` `Math.Exp` → `DetMath.Exp` (`:364,414,416`); `ShockResolver` `Math.Acos` → `DetMath.Acos`; `GeoWkt` trig → `DetMath`
- [x] A2 Architecture test banning platform transcendental `Math.*` in Core
- [x] A3 Maritime-air priority queue tie-break by tile index
- [x] A4 Jint: seeded `Math.random`, frozen `Date`, UTC
- [x] A5 Culture-sensitive string ops (`NameText`, `NameGenerator`, `OrderBy` on load order in Peoples/NameData/Locale stores)
- [x] A6 Re-baseline golden values / history corpus that moved (none moved: DetMath.Exp is bit-identical to this libm on these inputs)

Log:
- 2026-10-25. ClimateStage (DetMath.Exp x3; maritime PQ priority (−air, tile)), ShockResolver/GeoWkt (DetMath), JintWorldStage
  (prelude: seeded non-writable Math.random, Date getter throws), Ordinal string ops in NameText/NameGenerator/PeoplesStore/
  NameDataStore/LocaleStore. Tests: ArchitectureTests.Transcendentals_GoThroughDetMath, CultureInvarianceTests (cs-CZ fails
  without the fix: "Chinese" sorts after "Eastern"), Jint MathRandom_IsSeeded/Date_IsUnavailable/MathRandom_CannotBeReplaced.
  Core 907/907 (net10), 905/905 (netstandard2.1); Jint 31/31. Golden hashes unchanged.

## Batch B — history integrity

- [x] B1 `HistoryCatalog.Reset` clears `PluginState` and rebuilds `Strings` (+ regenerate ≡ fresh test)
- [x] B2 Null-edit `Recompute` must not fork after absorption (+ multi-seed test)
- [x] B3 String table load remaps old saves instead of requiring the built-in prefix

Log:
- 2026-10-25. B1: StringTable.Clear/PluginStateBag.Clear called from HistoryCatalog.Reset (test fails without: probe read 7, want -1).
  B2: Polity.AbsorbedInto (schema 12; <12 recovered from PolityAbsorbed "into:" tags); FloodOwnership seeds an absorbed capital
  for its living heir (LivingHeir follows chains; never claims a living capital tile); driver no longer forces tiles.
  Changes history on absorption worlds (absorbed land no longer drifts to neighbours on later re-floods).
  StarvedWar_LeavesRoadBuildingUntouched narrowed to founding-capital routes (a mid-run rebellion differs between the two
  runs: wars change demography). B3: Load remaps string ids by name (EndCause, participant Role), rejects duplicates.
  Tests: RecomputeNullEditTests (30 seeds + absorber keeps land), HistoryStringTableLoadTests, PluginEventAndState regen test.
  Core 942/942 (net10), 940/940 (netstandard2.1); MapEditor.Engine 73/73. Not covered by a test: the <v12 absorber recovery.

## Batch C — hierarchy and picking

- [x] C1 `ChildContextDeriver.FitGradient` centres E/N (+ planar-input test)
- [x] C2 `HexPicker` plane picking for flat projections; displaced-surface picking for terrain (+ round-trip tests)

Log:
- 2026-10-25. C1: FitGradient centres E/N on the sample centroid (GradientFitTests, 4 offsets exact to 1e-9). Golden hashes
  re-pinned (local maps move: downhill aspect changes); identical on net10 and netstandard2.1.
  C2: HexPicker flat = z=0 plane + inverse projection (0.05 deg edge slack, clamp); new overload with surfaceRadius walks
  down from the outer sphere onto each candidate tile's radius (<=6 rounds). GlControl passes TileTerrainRadius in Terrain
  mode and puts the flat-map selection highlight in plane space. FlatAndTerrainPickTests: equirect 499, mercator 471,
  gnomonic 195 tiles exact; terrain 49/49 (fixed 1.42 sphere: 7/49). Presentation 108/108, Core 946/946 (+944 ns2.1),
  MapEditor.Engine 73/73. App not run (no display); only compiled.

## Batch D — hydrology

- [x] D1 Closed lakes do not leak over the pour point
- [x] D2 Lake supply counts the unflooded basin's runoff

Log:
- 2026-10-25. D1: LakeSolution.BasinReceiver/BasinDepth = priority flood (by bed) from a closed lake over its dry basin;
  HydrologyStage overrides receivers/depth with it (the basin plateau sits exactly at the spill level, so depth ordering
  stays donors-first). D2: LakeSolver supply = external + every basin tile's runoff less channel loss (whole runoff once
  ponded). ClosedBasinTests: synthetic notched funnel; asserts distance-only routing leaks there, the tree never does,
  supply >= basin runoff. Only the legacy (7, 4) golden moved (re-pinned); closed lakes are rare in default worlds.
  Known gap: Hydrography.ReceiverAt (single-tile queries) does not see the override. Core 947 (+945 ns2.1), others green.

## Batch E — robustness

- [x] E1 `WorldBuilder.Build` / `WorldDataStore.Allocate` release native memory on failure
- [x] E2 Jint `ResolveRead` enforces declared reads
- [x] E3 Map editor: failed generate / failed boot recover

Log:
- 2026-10-25. E1: WorldBuilder.Build disposes the map on any failure; WorldDataStore.Allocate disposes a half-built mapping
  so a retry throws again (BuildFailureCleanupTests: both fail on the old code). E2: Jint ResolveRead requires the layer in
  Reads/ReadsOptional/Writes even when registered (+2 tests); PLUGINS.md updated (also documents seeded Math.random, Date).
  E3: worker clears `loaded` before (re)generating and drops a rejected boot promise; WorldService fails the worker on a
  rejected init (+vitest). The worker `loaded` path has no unit test (needs the .NET runtime; covered only by e2e).
  Core 949 (+947 ns2.1), Jint 33, Presentation 108, MapEditor.Engine 73, editor vitest 399/399, tsc clean.

## Batch F — history defects (review section 2)

- [x] F1 Displaced people of a collapsed/absorbed polity re-owned to successor/absorber instead of written off
- [x] F2 Collapse successor's seat becomes Capital (role + tier floor)
- [x] F3 `SuccessorCount` counts only collapse successors (not rebels)
- [x] F4 Narrative events of rejected structural proposals are not committed (capture, rebellion, villain)
- [x] F5 `AddRoad.ConflictKey` per route so same-turn routes keep shared segments
- [x] F6 Treaty: no foster refusal for pairs married the same turn
- [x] F7 Conquest overlay requires a living victor
- [x] F8 `RulerGeneration` counts from `FoundedTurn`
- [x] F9 `PopulationGrid.Step` tolerates duplicate cells / guards underflow
- [x] F10 Myths use the priority (unbudgeted) append

Log:
- 2026-10-25. F1 RehomeDisplaced (driver, after HandOver/absorb; DisplacedGroup.PolityId now settable). F2 seat Role=Capital,
  Tier>=CapitalFloor. F3 SuccessorCount = parent ended on the founding turn (TurnView.SuccessorCount feeds CollapseOperator).
  F4 Proposal.Requires + ProposalSink.EventIfApplied; driver commits dependents after independents and only if their
  requirement applied (capture event, raze ruin, rebellion event, villain event + pressure). F5 AddRoad conflict key mixes
  the route in. F6 treaty skips pairs married this turn. F7 dead victors skipped. F8 generations from FoundedTurn (both
  operators). F9 PopulationGrid subset dedupe bitset + off-grid neighbours ignored. F10 myths via AppendPriorityEvent.
  Tests: HistoryIntegrityTests (4, all fail on old code), PopulationGrid (+2, fail on old), Requires plugin test.
  F5/F6/F7/F10 have no dedicated test. Core 956 (+954 ns2.1), Jint 33, MapEditor.Engine 73. No golden moved.

## Batch G — hydrology defects (review section 2)

- [x] G1 Below-sea-level inland basins are not ocean (connectivity from the world ocean)
- [x] G2 Open-lake evaporation charged on the whole outflow, not only the outlet path
- [!] G3 Glacier melt scales with ablation, not a flat +2: deferred. Any runoff cut (tried ablation^2 and 0.3+0.7*ablation)
  moves ~7-12 seed-calibrated tests (river/lake counts, road and founding facts of seed-42 histories). Belongs with the
  runoff recalibration of WEATHER_PLAN W0 / Climate v2, not a bug batch.
- [x] G4 Rivers that dry out mid-course are not reported as `Sink` when they drain to the sea

Log:
- 2026-10-25. G1 (scoped): marine groups other than the ocean of at most n/40 tiles (HydrologyStage.IsLandlockedMarineSize,
  shared with the catalog's Lake/Sea rule) are endorheic, and rivers ending in them are Lake-terminal. Biomes/climate still
  treat them as water (by design: they are lakes). G2: open lakes route every tile through the outlet (LakeSolver
  RouteLakeToOutlet); HydrologyStage.LevelDepths recomputes level-ground drain depth from the final receivers.
  G4: RiverTerminal.DriesOut (=3) for channels ending on exorheic dry land; editor features.ts handles it.
  Tests: HydrologyIntegrityTests (terminal vs ground, 3 worlds), DesertCrossing test updated (now DriesOut). No golden moved.
  Core 959 (+957 ns2.1), MapEditor.Engine 73, vitest 399, tsc clean. G2 has no dedicated test.

## Batch H — 2D map / history overlay (user report 2026-10-25)

Issues: names repeat; labels overlap (worst in history overlay); no personas, ruins, construction or culture visible;
borders too straight; some cultures over-represented (orcs, lizardfolk). Diagnosis (2026-10-25) root causes:
names = non-human name tables are 3-4 x 3-4 words, own words weighted x3 (PlaceNameBuilder.cs:39-55), uniqueness gives up
after 4 rerolls and realm/place batches are de-duplicated separately (uniqueNames.ts, world.worker.ts:199,295);
overlap = realm labels drawn by RealmLayer.ts outside the placeLabels collision pass; personas = none exist (only villain
records, unnamed, not exported); ruins/holy/battlefields exported but minZoom 1.6, lowest priority, Realms preset hides
stamps; faiths/culture not exported; borders = unweighted BFS from capitals (Voronoi) + coarse 4px RealmGrid; culture skew =
rebellion gated on Collectivism >= 60, so lizardfolk/orc/dogfolk breakaways multiply (founding itself is uniform).

- [x] H1 Cost-weighted territory flood (terrain/rivers/ranges; deterministic PQ) so borders follow geography
- [x] H2 Rebellion gate and breakaway culture: no collectivism-driven multiplication of a few peoples; founders drawn without replacement
- [ ] H3 Name variety: bigger non-human tables, lower own-word weight, joint realm+place uniqueness with fallback
- [ ] H4 Realm labels join collision avoidance (candidate cells, place labels yield)
- [ ] H5 Surface ruins/holy sites/battlefields (Realms preset stamps, minZoom), richer event lines (villain agenda, myths)
- [ ] H6 Export villains/faiths/culture traits; notable-figures + culture card in the history window (personas proper come with the expansion)

Log:
- 2026-10-25. H1: HistoryTerritory.AssignOwners is a Dijkstra (key cost, owner, tile) with StepCost = 1 + 6*climb +
  4*(height-0.35)+ + 1 onto a river, x biome hostility (glacier 2.5, deserts 1.8, swamp/tundra/mountain/canyon 1.6,
  jungle 1.4, forest 1.15). Size 6 build ~1.2 s. TerritoryCostTests (wall hands land to the far side). No golden moved
  (goldens carry no history). H2: founders drawn without replacement (bag); rebellion open to every culture at 0.7x chance
  below collectivism 60 (RebellionOperator.LooseRebelShare). PeoplesBalanceTests (12-people roster, 24 worlds): max/mean
  1.54 (old 1.73, fails), spread 35..13 (old 39..11). Test fallout, re-scoped (assumptions held by chance): Wars_* pressure
  now over all wars at the war turn (>=80%); Shrines_* skips captured shrines. Core 962 (+960 ns2.1), Engine 73.
  Render-side border smoothing (RealmGrid sampling) still open, goes with H4.

