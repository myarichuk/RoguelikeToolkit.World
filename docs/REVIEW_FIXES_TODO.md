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

> Batch A done and pushed. Current batch: **B** (history integrity), nothing started. Start with B1.

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

- [ ] B1 `HistoryCatalog.Reset` clears `PluginState` and rebuilds `Strings` (+ regenerate ≡ fresh test)
- [ ] B2 Null-edit `Recompute` must not fork after absorption (+ multi-seed test)
- [ ] B3 String table load remaps old saves instead of requiring the built-in prefix

Log:

## Batch C — hierarchy and picking

- [ ] C1 `ChildContextDeriver.FitGradient` centres E/N (+ planar-input test)
- [ ] C2 `HexPicker` plane picking for flat projections; displaced-surface picking for terrain (+ round-trip tests)

Log:

## Batch D — hydrology

- [ ] D1 Closed lakes do not leak over the pour point
- [ ] D2 Lake supply counts the unflooded basin's runoff

Log:

## Batch E — robustness

- [ ] E1 `WorldBuilder.Build` / `WorldDataStore.Allocate` release native memory on failure
- [ ] E2 Jint `ResolveRead` enforces declared reads
- [ ] E3 Map editor: failed generate / failed boot recover

Log:
