# Planet-scale implementation: phased todo

Companion to [PLANET_SCALE_BACKLOG.md](PLANET_SCALE_BACKLOG.md) (the why +
acceptance criteria). This file is the cross-session state: the user clears
the conversation between phases, so **this file is the memory**. Keep it
current and terse.

## Agent update instructions (follow every session)

1. **Session start:** read this file first, then the backlog doc sections for
   the current phase only (not the whole repo).
2. **One phase per session** unless the user explicitly says otherwise.
3. **Check boxes immediately** after each step is verified (`- [ ]` →
   `- [x]` via the edit tool). Never batch them at the end, never check
   unverified work.
4. **Log briefly** under the phase's `Log:` lines: date, files changed,
   key measurements, test command + result. Max ~10 lines per phase.
5. **Move the pointer:** update `Current phase` when a phase is fully done.
6. **Blocked?** Mark `- [!]` + one-line reason, stop the phase, report.
7. **Definition of done (every phase):** touched test project(s) run green
   in-session; boxes checked; log appended.
8. **Token discipline:** no re-research of settled verdicts — cite the
   backlog doc; keep replies short; probes go to `%TEMP%`, never the repo.

**Resume protocol:** user clears chat, then writes `phase N` (or
`continue`). Agent reads this file + the relevant backlog section and
proceeds. If the pointer says phase N is done, start N+1.

**Current phase:** 4 (Phase 3 done 2026-09-28)

## Phase 0 — Rain-shadow verification + "hectic climate" diagnosis

Goal: prove/disprove rain-shadow support with numbers; diagnose the
visualizer's hectic Climate mode. Diagnosis only — code fix (if any) becomes
a new story.

- [x] 0.1 Run existing orographic test (`OrographicEffect_*`) + re-read
  `ClimateStage` windward/leeward block.
- [x] 0.2 Empirical probe (seed matrix, sizes 3–5): leeward vs windward
  precip means; record numbers in log.
- [x] 0.3 Diagnose "hectic": `ClimatePalette` mapping, moisture noise
  frequency, (no) smoothing. Verdict: palette artifact, data noise, or both.
- [x] 0.4 Append verdict to backlog §E0; add follow-up story to backlog if a
  fix is warranted.

Acceptance: yes/no on rain shadow with numbers; hectic cause named.
Log: 2026-09-28. Docs only (this file + backlog §E0/E6); probes in %TEMP%\phase0probe, uncommitted.
  0.1 `OrographicEffect` passes. ClimateStage: dh>0 x(1+min(dh*1.2,.6)), dh<0 /(1+min(-dh*3,.65)), gate bestUp>0.15.
  0.2 Sizes 3-5 x seeds 42/7, all lee<wind: s3 (D.186/.094), s4 (D.174/.055), s5 (D.134/.040); glob mean ~.58-.60, min .17-.24, ~0 tiles <0.20.
  0.3 Hectic = data noise + palette gain: land mean|dP| .106, 25% pairs >.15, max .64; 1-pass blur -> .040; dG/dP=0.9; climate unsmoothed (biomes: 2 passes).
  Tests (Release): Core ClimateTests 3/3 green; Presentation suite 80/80 green.

## Phase 1 — E1: hydro-zero moisture fix (bug)

- [x] 1.1 `ClimateStage`: detect un-run hydro layer (all `Flow`/`Surface`
  zero, one O(n) scan) → fall back to elevation check.
- [x] 1.2 Test: registered-but-unrun ≡ unregistered moisture field.
- [x] 1.3 Full suite green (`dotnet test`); confirm orographic + determinism
  tests still pass.

Acceptance: per backlog E1.
Log: 2026-09-28. `ClimateStage.cs` (un-run scan + ordering-circularity doc); `ClimateTests.cs` (+equivalence test).
  Pre-fix: registered-unrun precip = unregistered + 0.06 exactly (WaterBodyId 0 fakes near-water everywhere).
  Post-fix: registered-unrun ≡ unregistered bit-for-bit; orographic + determinism still pass.
  Tests (Release): `dotnet test` Core 135/135 + Presentation 80/80 green.

## Phase 2 — C1+C2+C3: benchmarks + scale smoke tests + CI

- [x] 2.1 New `tests/RoguelikeToolkit.World.Benchmarks` (BenchmarkDotNet,
  `MemoryDiagnoser`): FullBuild 3–7, per-stage, `GetTileIndex`(/Exact),
  `DeriveRegionMap`/`DeriveLocalMap`, `SpatialIndex` + queries.
- [x] 2.2 Benchmarks README: baseline workflow (`results/` gitignored).
- [x] 2.3 CI: build benchmarks + one smoke benchmark (< +2 min CI time).
- [x] 2.4 xunit scale tests: size 6 in-suite; size 7 `Stress` trait.
- [x] 2.5 Record baseline numbers (sizes 3–7) in log.

Acceptance: per backlog C1–C3.
Log: 2026-09-28. New `tests/RoguelikeToolkit.World.Benchmarks` (BDN 0.15.8, MemoryDiagnoser;
  FullBuild 3–6, size-7 reduced job, per-stage, queries, CI-only Smoke) + README + `BenchmarkDotNet.Artifacts/` gitignored;
  slnx includes benchmarks (CI builds it); `ci.yml` excludes Stress + runs smoke; new `nightly.yml` runs Stress.
  `ScaleTests.cs`: size-6 in-suite, size-7 `Stress` (tile-count + ocean/land + 2x-build determinism).
  Baselines (Release, seed 42, %TEMP% probe): s3 ~0.03–0.06s, s4 ~0.09–0.15s, s5 ~0.2–0.4s, s6 ~0.7–0.8s, s7 ~1.7s
  (faster than backlog's older box: s7 was ~5s). BDN smoke: size-3 build 7.4ms/616KB, GetTileIndex 392ns/0 alloc.
  Tests (Release): `dotnet test` Core 137/137 + Presentation 80/80 green; `Category!=Stress` filter → 136 (excludes size-7, verified).

## Phase 3 — E2+E5: tundra rebalance + guardrails (blocked by 1, 2)

- [x] 3.1 Retune dry gate / snowTemp / tundra threshold against measured
  precip distribution.
- [x] 3.2 Biome-histogram invariants over seed matrix (Tundra > 0 always;
  Glacier confined; Desert within 0.5–2x or justified).
- [x] 3.3 Disambiguate ice/snow/rock + Tundra/Glacier hues; document
  glacier-before-mountain precedence in code.
- [x] 3.4 Full suite green.

Acceptance: per backlog E2, E5.
Log: 2026-09-28. `Glaciology.DryPolarGate` 0.20→0.40 (single lever; snowTemp/tundra-gate untouched).
  Pre (s3-s6 x 42/7): tun 0,0,1,0,5,3,7,4; cold-land pMean ~0.55, gate 0.20 below reachable range.
  Gate 0.35 left s3seed42 at 0 (its cool-dry tiles sit at p .365-.372; drier ones carry E4-clamped temp 0 → deep-freeze floor).
  Post: tun 2,2,10,3,46,28,206,59; gla −4–8% but >0 everywhere (wet-cold); des ratios 1.0 (s4seed7 1.02 via erosion knock-on).
  E5: BiomePalette Tundra → khaki-olive (0.62,0.60,0.45, dist .67 from ice); Terrain ice → deep blue (0.55,0.85,0.95);
  precedence comment in biome chain; distance guardrails in both palette test files.
  FLAG: E6 smoothing is in no phase — this retune is against the unsmoothed distribution; revisit after E6 is scheduled.
  Tests (Release): `dotnet test` Core 140/140 + Presentation 82/82 green (incl. 3 histogram + 2 palette tests).

## Phase 4 — B1: per-cell lon/lat API

- [ ] 4.1 `RegionHandle.CellCenter(int)` / `LocalMapHandle.TileCenter(int)`
  → `GeoCoord` + inverse lookup.
- [ ] 4.2 Round-trip + determinism tests; `GetTileFeatures` via region-cell
  coord works.
- [ ] 4.3 API docs (`docs/API.md`) updated.

Acceptance: per backlog B1.
Log:

## Phase 5 — B2: region/local "plain map" viewer (blocked by 4)

- [ ] 5.1 Visualizer: planet hex → region grid view → local grid view +
  back-navigation, existing palettes.
- [ ] 5.2 Inspector shows B1 coordinates per cell.
- [ ] 5.3 Presentation tests for new view-model/mapping code.

Acceptance: per backlog B2.
Log:

## Phase 6 — D1+D2: true relief (normals + two-sided hillshade)

- [ ] 6.1 Displaced per-face normals → `aNormal` (CPU, once per rebuild).
- [ ] 6.2 Two-sided hillshade in Elevation mode (signed relief vs
  neighbor mean).
- [ ] 6.3 Before/after screenshots attached to PR/description; palette
  tests green; GLES-safe (no shader-language upgrade).

Acceptance: per backlog D1, D2.
Log:

## Phase 7 — A1+A2+A3: scale contract + ceiling + guards (blocked by 2)

- [ ] 7.1 Scale table + tier→play-use mapping in `docs/ARCHITECTURE.md`.
- [ ] 7.2 Supported size ceiling from Phase 2 numbers; `WithSize` throws
  beyond it + tests.
- [ ] 7.3 Visualizer large-world warning (past size 6).

Acceptance: per backlog A1–A3.
Log:

## Phase 8 — Follow-ups: E3, E4, E6, B3, D3, D4

- [ ] 8.1 E3: hydro-aware climate refresh (or wontfix with reason).
- [ ] 8.2 E4: stale lapse-rate measurement note + fix/wontfix.
- [ ] 8.3 E6: smooth climate precip + soften orographic discontinuity
  (DOCUMENTED, was in no phase). 1–2 blur passes on precip (or upwind-aware
  smoothing that preserves the rain shadow) and/or a continuous orographic
  factor; optionally lower palette precip gain. NOTE: smoothing shifts the
  precip distribution `Glaciology.DryPolarGate` was calibrated against in
  Phase 3 — after E6, re-measure the biome matrix and confirm the histogram
  invariants hold (or retune the gate). Acceptance per backlog E6: land-neighbor
  mean|dP| < ~0.05 on the Phase 0 matrix; orographic test still passes (d > 0);
  determinism preserved.
- [ ] 8.4 B3: flat-arena design note + estimate (no code).
- [ ] 8.5 D3: depression exaggeration control.
- [ ] 8.6 D4: depression color cue.
- [ ] 8.7 Full suite green; final numbers in log.

Acceptance: per backlog; each item may close as done or wontfix-with-reason.
Log:
