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

> Batches A-D done and pushed. Current batch: **E** (robustness), nothing started. Start with E1 (`WorldBuilder.Build` ~L244-316, `WorldDataStore.Allocate` ~L159-191).

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

- [ ] E1 `WorldBuilder.Build` / `WorldDataStore.Allocate` release native memory on failure
- [ ] E2 Jint `ResolveRead` enforces declared reads
- [ ] E3 Map editor: failed generate / failed boot recover

Log:
