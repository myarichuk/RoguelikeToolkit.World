# History: milestone breakdowns M1–M4 (DRAFT)

Companion to `HISTORY_GENERIC_MODEL.md` (the design) and `HISTORY_PLAN.md` (P0–P4). This file
turns the model's milestones into ordered, testable steps. Sizes are relative (S/M/L), not time.

Decided: turn-major driver, snapshot/commit read rule (A1), canonical commit order **entity id, then
proposal kind**, M1 is a pure port (no new features; history goldens re-baselined once). Conquest
reacting to a same-turn war one turn later is accepted. There are no known external users, so M1
replaces the legacy engine in place: no dual-engine switch, and a broken intermediate state is
fine as long as it is fixed before M1 closes.

## 0. Facts about the current code that shape the plan

Verified in the repo, not assumed:

- `WorldBuilder` registers history stages **explicitly** (a hand-written `AddStage` list). The five
  P2c stages (HolySite, Khan, Treaty, Villain, Faith) were missing from it; they are registered now
  and `HistoryFaithTests` covers them. The registration list is therefore the first thing the
  kernel's plugin registration must replace.
- The "driver" (`HistoryEventsStage`, 28) is only a clock: it calls `context.BeginTurn(t)`. Every
  operator loops turns `1..TurnCount-1` itself, stage after stage.
- Operators mutate live catalog state mid-loop (pressure, grudges, windows, road `Severed`, sites)
  and read the past by recomputing pure demography (`PopulationAtTurn`, `PressureAtTurn`) or by
  scanning `history.Events` (`EventPressureBonus`).
- **Time-travel leak.** `GrudgeWeight(a, b)` has no turn stamp. Treaty (48) and Khan (46) run the
  whole span before War (50), so a war at turn 2 is weighted by grudges accrued at turn 28.
  `IsWindowed` is turn-aware; grudges are not. This is a real causality bug of stage-major
  execution, and M1 removes it.
- The per-turn event cap (`MaxEventsPerTurn`) is a counter keyed on the last-seen turn in
  `AppendEvent`, so under stage-major it resets per stage, not globally.
- Compaction (`EnforceCap`) drops the oldest non-`Founding` event, regardless of what cites it.
- `TryGetTileModifier` and the battlefield/bordering helpers scan the whole log or the whole map.
- Per-operator budgets are `(operator name, turn)` dictionaries on `HistoryContext`, which already
  suits A1.
- History stages are separated from physics by `s is IHistoryPlugin` in `World.Recompute` and
  `World.RegenerateHistory`; that filter must survive.

## M1 — kernel and pure port (L)

Goal: same simulation concepts, new engine. No new processes. After M1 the code is turn-major,
proposal/commit, string-keyed, and extensible, and all current behaviour lives in registered
operators.

### M1.0 Safety net before touching anything (S)

1. Register the missing stages (done) and keep `HistoryFaithTests`.
2. Add coverage for what is currently untested end to end: Khan ultimatum/tribute path (needs a
   horde; add a test hook to force a polity form or build a catalog directly), Treaty marry/foster
   outcomes, HolySite placement and aura, Rebellion split and re-flood, Faith power derivation.
   No seed 1–40 at size 3 produced an `Ultimatum`, so Khan is only unit-tested at
   `DecideOutcome` today.
3. Capture a **reference corpus**: for a fixed seed × size grid, record per-seed aggregates (event
   counts by kind, polity count, roads built/severed, villains, grudges, treaty windows). It is a
   before/after sanity reference, not a byte golden and not a dual-run comparison, because outcomes
   will legitimately shift.
4. List tests coupled to stage-major structure (e.g. `StarvedWar_LeavesRoadBuildingUntouched`,
   `OperatorBudgets_ArePerOperatorPerTurn`); decide per test whether it still holds under A1.

Exit: tests green, corpus committed, coupling list written.

**M1.0 result (done).**

- New `HistoryCoverageTests` (6 tests): Khan end to end (forced all-horde catalog; Tribute and
  Ultimatum both fire, window/grudge consequences hold, each ordered pair pressed once), Treaty
  (marriage, foster and refusal all occur across seeds 1-12), HolySite (one per polity, owned,
  off-capital), Rebellion (fires with `MaxPolities = 16`), and the corpus capture.
- Reference corpus: `docs/history-reference-corpus.md` (size 3 seeds 1-20, size 4 seeds 1-5).
  Regenerate with `HISTORY_CORPUS_OUT=<path> dotnet test --filter ReferenceCorpus`.
- **Finding A: default worlds never get hordes or rebellions.** Polity count always sits at the
  default cap (8), so Rebellion cannot run, and no capital meets the Horde pasture rule. Khan and
  Rebellion therefore need forced fixtures (as above); the ported tests must keep doing so.
- **Finding B: trade roads never appear.** 0 roads and 0 Exchange events across 150 size-3 worlds,
  so `Roads_AreLandBound_AndCutsMatchSevers`, `Exchange_OnlyRunsOnActiveRoutes` and the road half
  of `StarvedWar_LeavesRoadBuildingUntouched` pass vacuously. Cause (probed on seeds 1-30):
  `AreComplementary` compares polity-wide summed surplus profiles, and a sum blends grain with
  range land so the 1.25x gate is never met; nearby pairs do have land paths. This is a P2
  defect, not an M1 regression. Decision for M1.3: port TradeRoad faithfully, then fix the gate
  (compare per-region or normalised profiles) as its own small commit with a forced-route test, so
  the port stays reviewable.
- **Coupling list (tests that depend on stage-major structure):**
  - `HistoryCoverageTests.Khan_*`: call `new KhanOperator().Apply(...)` directly; rewrite to drive
    the Khan operator through the driver in M1.3.
  - `OperatorBudgets_ArePerOperatorPerTurn`: tests the `HistoryContext` budget API
    (`ResetOperatorBudget`, `ClearOperatorBudgets`); keep the API or retarget at the sink budgets.
  - `StarvedWar_LeavesRoadBuildingUntouched`: relies on per-stage independent RNG and budgets;
    holds under A1 if budgets stay per operator and streams stay keyed by (stage, turn, entity).
  - `StageOrders_*` (Operators, Faith, History tests): assert `StageOrder` constants; the
    operators keep their order values, only the driver changes.
  - `SameSeed_SameOperatorBytes`, `RoundTrip_PreservesOperatorState`,
    `P2cState_IsDeterministic_AndRoundTrips`: byte equality; survive, but re-baseline on the
    schema v4 bump.
  - `Conquests_OnlyOverlayCultures`, `Wars_HaveBattlefields_AndPressureBothSides`: outcome-shaped,
    expected to hold; re-check after the Conquest one-turn delay.

**M1.3 progress (TradeRoad):** `TradeRoadOperator` (order 30) replaces `TradeRoadStage`; reads the ownership snapshot per turn, emits one `AddRoad` per segment plus a `RoadBuilt` event. Behaviour unchanged. Finding B is fixed on purpose in the following commit: `AreComplementary` now compares grain shares (gap >= `ComplementShareGap` 0.12) because polity-wide sums were all range-heavy (ratios 0.2-0.8) and never met the 1.25x gate; default worlds now open routes. Goldens move at the M1.5 re-baseline.

### M1.1 Additive kernel types (M)

No behaviour change; old stages still run.

- **StringTable / `EventKind`:** interned string ids; built-ins as static members
  (`EventKinds.War`...). The `HistoryEventKind` enum stays during transition and maps to ids.
- **`HistoryEvent`:** add participants (entity ref + role), cause (event index or none) and
  importance class (founding / structural / minor).
- **Entity lifecycle fields on `Polity`:** `EndedTurn` (-1 alive), `EndCause`, `Parent`. Persisted;
  unused by ported operators in M1.
- **Scalars:** a declared-scalar store with `pressure` as the first scalar (kind, name, min, max,
  default). `Polity.Pressure` becomes a view over it.
- **Indexes:** per-tile and per-entity event indexes built after the driver and on load; replace
  the log scans in `TryGetTileModifier`/`IsBattlefield`.
- **Compaction by importance:** minor first; never drop an event that a retained event cites as
  cause. Fixes the review finding where compaction erased War events.

Exit: new types unit-tested; old pipeline unchanged; PublicAPI baseline updated for new types.

**M1.1 progress.**

- Done (commit "M1.1a"): `StringTable` (seeded so built-in ids equal the enum values), `EventKind` /
  `EventKinds`, `EventImportance` / `EventImportanceMap`, `EventParticipant`; `HistoryEvent` gains
  stable `Id`, `Cause` (an event **Id**, not a list index, so compaction cannot shift references),
  `Importance`, `Participants`; `Polity` gains `EndedTurn`, `EndCause`, `Parent`, `Alive`; compaction
  is by importance and protects cited causes; `HistoryCatalog.Strings` and internal `AppendLinked`.
  New fields are in-memory only until the M1.4 save; loads renumber ids in log order.
- Done (commit "M1.1b"): `ScalarDef` / `ScalarRegistry` (`pressure` = id 0, clamped 0..`MaxPressure`);
  `Polity.Pressure` is a view over `GetScalar` / `SetScalar` (unset reads as the declared default;
  `HistoryCatalog.Scalars` holds declared extras); lazily built per-tile and per-polity event indexes
  (`EventsAtTile`, `EventsOf`) invalidated on append, compaction and reset, now used by
  `TryGetTileModifier` / `IsBattlefield`. M1.1 is complete; scalars are not persisted until M1.4.
- PublicAPI: the repo already carries a large RS0016 warning backlog (not an error); the new public
  types are not added to `PublicAPI.Unshipped.txt` yet. Clear it in the M3 audit, or earlier if wanted.

**M1.2 progress.**

- Done (commit "M1.2"): `IHistoryOperator`, `IHistoryPostPass`, `Proposal` (nested typed proposals:
  ReassignTile, AddRoad, SeverRoute, AddHolySite, AddGrudge, RepayGrudge, AddWindow, AppendEvent,
  AdjustScalar) with `ProposalKind` (its numeric order is the commit order within an entity),
  `TurnView` (copies polities/scalars/ownership/grudges/windows/roads/sites; events via index queries),
  `ProposalSink` (emit, operator-scoped `Rng(entity, purpose)`, budgets), `HistoryDriver` (order 28,
  not yet wired; `AddOperator` / `AddPostPass`, `RunTurn`, `RejectedProposals`). Commit sorts by
  (entity, kind, operator name, sequence), rejects a second proposal on the same structural resource
  (tile / road tile pair / route), appends events in canonical order so the per-turn cap is global, and
  sums scalar deltas then clamps once.
- Deferred to the ports that need them: AddPolity (Rebellion), set-overlay, mark-ended, grudge turn
  stamps (War). Event `Cause` in a proposal must be an event from an earlier turn. `WithHistory`
  registration and `StandardHistory` land in M1.5 with the switch-over.


### M1.2 Driver and operator contract (M)

- **`IHistoryOperator`:** `Name`, `Order` (determines registration/evaluation order only; outcomes
  must not depend on it), `Propose(TurnView view, ProposalSink sink)`.
- **`TurnView`:** read-only snapshot of end-of-turn `t-1` state (polity scalars, grudges with turn
  stamps, windows, roads, sites, ownership, event indexes). Built once per turn.
- **`ProposalSink`:** typed proposals: append event, adjust scalar (delta), add/repay grudge,
  add window, sever route, add site, set overlay, add polity (rebellion), reassign ownership,
  mark entity ended (reserved).
- **`HistoryDriver`:** replaces the clock at order 28. Per turn: build snapshot; run every operator's
  `Propose` with its own `HistoryRng` streams and its own budgets; **commit**:
  1. sort proposals by `(entity id, proposal kind)`; ties by operator name then proposal sequence;
  2. apply scalar deltas by summing and clamping once (commutative, so order cannot matter);
  3. apply the global per-turn event cap during this canonical pass, not at emission time;
  4. reject a second conflicting structural proposal on the same entity in the turn (recorded in a
     rejected counter, not an exception).
- **Setup and post-passes:** `CultureSeed` and `PolityForm` remain stages at 26/27. `Faith` and
  `CityHistoryProject` become post-passes over the finished log (`IHistoryPostPass`).
- **Registration:** `WithHistory(configure)` exposes `AddOperator`/`AddPostPass`; a
  `StandardHistory` preset registers the reference set. `WorldBuilder`'s hand-written stage list
  goes away. The driver implements `IHistoryPlugin` so the recompute/regenerate filters keep
  working.

Exit: a trivial test operator runs under the driver; commit order test (reordering registration
does not change bytes); per-turn event cap test (global).

### M1.3 Port the operators (L, one step each)

Each port keeps the operator's constants and RNG purposes (`HistoryRng` keyed by stage name, turn,
entity, purpose), changes it to read from `TurnView` and emit proposals, and keeps its tests. The
legacy stage is deleted as each operator is ported (no switch, no dual run). Tests that depend on
not-yet-ported operators may be red between ports; the suite must be green again after step 10.

Order (dependency first):
1. **Population** (pure; no RNG, no budgets).
2. **TradeRoad** (reads capacity and surplus; emits road proposals).
3. **HolySite**.
4. **War** (reads grudges, windows, holy sites; emits war, desecration, severs, pressure deltas).
   Grudges now carry the turn they were added, so the time-travel leak disappears.
5. **Conquest** (reads the war events of earlier turns; the same-turn war becomes visible next
   turn — an intended one-turn delay under A1).
6. **Khan**.
7. **Treaty**.
8. **Rebellion** (needs the add-polity and reassign-ownership proposals; the polity cap is checked
   at commit; ownership re-flood and holy-site owner sync happen in the commit, which absorbs the
   review finding on stale holy-site owners).
9. **Villain**.
10. **Exchange**.
Post-passes: **Faith** (faith powers count events where a polity is attacker *or* defender using the
new participant roles, absorbing that review finding) and **CityHistoryProject** (spatial resolve
only).

Behaviour that changes on purpose: no time-travel grudges; global event cap; one-turn delay for
same-turn cross-operator reactions; borders cached and invalidated only when ownership changes
(performance, outcome-neutral).

**M1.3 progress.**

- Done (Population): `PopulationOperator` replaces `PopulationStage`. Map access: `HistoryDriver.RunTurn`,
  `IHistoryPostPass.Run` and `TurnView.Map` now carry the `WorldMap` (terrain and capacity layers are
  immutable during history; ownership still comes from the snapshot). New `SetPopulation` proposal
  (absolute population + ruler generation); pressure moves by the *change* in crowding via `AdjustScalar`,
  so war bumps stack on top. Behaviour changes on purpose: capacity is recomputed per turn from current
  ownership (legacy used ownership at order 32 for the whole span); unseeded polities seed and step in
  their first turn; ended polities are not grown; Villain/Faith now read causal pressure, not
  final-crowding-plus-all-bumps. Closed-form equality with `PopulationAtTurn` holds for static ownership
  (tested). Rebellion port must still set the rebel's founding population (legacy `RebellionOperator.cs:151`).
- The legacy stage list in `WorldBuilder` no longer runs Population; Population-dependent legacy tests are
  red until M1.5 wires the driver.

### M1.4 Persistence, schema v4 (M)

- Save: string table; events with participants, cause, importance; polity lifecycle fields;
  scalar store; grudge turn stamps.
- Load validates every reference (extends the checks added with `HistoryCatalog.Load`).
- v1–v3 saves: derived history regenerates from the seed. **`HistoryOverrides` (renames, pins) are
  carried across** and re-validated; a pin to a polity that no longer exists is dropped with a
  recorded note.
- Round-trip and corrupt-file tests.

### M1.5 Switch, re-baseline, clean up (M)

- Remove any leftover legacy loops and the hand-written stage list in `WorldBuilder`.
- Re-baseline history goldens once; compare against the reference corpus and explain the deltas by
  the intended changes above.
- Update `History/README.md`, `HISTORY_PLAN.md` (P2 "Landed as" notes) and PublicAPI.
- Resolve the remaining review findings that the kernel did not absorb (compat-rule validation,
  pinned-capital handling) or move them to M2.

### M1 acceptance (all must hold)

- Determinism: same seed, same bytes; save/load round-trips.
- **Prefix stability:** a run with `TurnCount = k` produces exactly the events of turns `< k` of a
  run with a larger `TurnCount` (impossible under stage-major because of the grudge leak; it is the
  test that proves the leak is gone).
- Registration order does not change bytes.
- Per-operator budget isolation still holds (a starved operator does not starve another).
- Reference corpus: event counts per kind within stated tolerances, deltas explained.
- Perf: full history band at size 7 stays under the 5 s budget; per-turn cost no worse than 1.5x the
  legacy band (snapshots and indexes are the risk).
- Physics goldens unchanged.

### M1 risks

- Snapshot cost per turn at large sizes: keep the snapshot to compact arrays and indexes; ownership
  is shared read-only between commits.
- Drift read as a regression: mitigate by stating the intended deltas up front (above).
- Red intermediate states between ports: acceptable (no users), but port in the listed order and
  do not add features, so the suite is green again at step 10.

## M2 — settlement lifecycle, shocks, displaced pool, scars (L)

Depends on M1 (entities with lifecycle, participants/cause, driver). Adds the first new processes.

1. **Settlement as entity (M):** `HistorySettlement` gains `EndedTurn`, `EndCause`, tier, role,
   owner, `Parent`; physical `Available` is split from historical status. Capital settlements are
   founded by the formation step as events.
2. **Candidate generator (S):** `CitySiteScorer`/`LandCapacity` expose ranked candidate tiles
   (habitability, water, defensibility); pure function of the map.
3. **Settle operator (M):** founds, grows and shrinks settlements under an emergence budget;
   sources: colonization (pressure overflow), economic node, military post, religious, nomad
   sedentarization. Per-polity cap.
4. **Displaced pool (M):** conserved population released by shrinking/dying settlements and
   absorbed by the settle operator (refugees, resettlement). Ledger persisted.
5. **Shock resolver (M):** shock kinds, severity, footprint (tile / radius / route-borne / polity);
   outcome by role, tier, terrain, defenses; RNG per `(seed, "shock", turn, settlement)`.
6. **Hazard maps (M):** quake and volcano from `Lithosphere`; tsunami from coast plus those;
   flood from `Hydrology`; drought/famine from `Climate` and capacity; plague from density and
   route connectivity. Return periods and per-turn emergence budgets keep disasters epoch-scale.
7. **Scars (S):** persistent tagged sites/tile modifiers (ruin, crater, plague pit); exported.
8. **Roads between settlements (M):** TradeRoad connects settlements, not only capitals.
9. **War/Conquest effects (M):** sack, raze, transfer individual settlements; conquest moves
   ownership instead of only overlaying a culture.
10. **P4a shrink (S):** the scorer exposes candidates; history decides placement.

Exit: Gurubashi and Venice paper cases (see the design doc §13) run as tests: collapse leaves
ruins and refugee towns; a sacked coastal city founds a lagoon refuge; population is conserved
across the pool; determinism, round-trip, caps and perf budgets hold.

Risks: event-log volume (importance classes, per-polity settlement cap); tuning disaster
frequency; keeping goldens stable by gating new processes behind configuration defaults during
rollout.

## M3 — extension API and economy plugin (M–L)

Depends on M1.2 (registration) and ideally M2 (settlements to hang an economy on).

1. **Public surface audit (S):** list the History types a plugin needs (`HistoryDemography`,
   `HistoryNeighbors`, `HistoryTerritory`, `PeoplesLoader`...), make them public, and clear the
   PublicAPI backlog for them. Layer rules in `ArchitectureTests`: host projects depend on the
   public surface only.
2. **Plugin state bag (M):** keyed, versioned binary blobs saved with the catalog; survive
   `Respatialize`; mismatch = clear error or reset with a recorded note.
3. **Reserved ordering and ledger names (S):** documented band for plugin operators.
4. **Reference plugin host (S):** `MapEditor.Engine` (C#) registers extra operators through the
   builder; `samples/` gets a minimal history plugin next to `DesertPlugin`.
5. **Economy plugin (L):** declared scalars (wealth, tax, corruption, unrest, estates); an
   economy operator (resources, scarcity, baseline tax) and a fiscal operator (war/cut shocks,
   tax and enforcement decisions, unrest into `pressure` for Rebellion). Resource-war cause tags;
   trade volume by resource. Built only against the public API.
6. **Gaps found become API work (S–M):** anything the plugin cannot do is a defect in step 1–3.

Exit: the economy plugin lives outside Core, round-trips through save/load, is deterministic, and a
host without it generates identical history to the base preset.

Risks: API freeze too early (keep the new types marked unstable until the economy plugin ships);
state-bag migration.

## M4 — data layer and script hooks (M–L, conditional)

Starts only if M3 shows what the C# API cannot express cheaply.

1. **Data definitions (M):** YAML for scalars, polity forms, event/conflict kinds, hazards, roles,
   resources, policies; declarative effects over a closed vocabulary (`adjust`, `found`, `end`,
   `split`, `emit(shock)`, `tag`, `transfer`); a generic rule-runner operator; validation with
   aggregated errors like `PeoplesStore`.
2. **Retrofit (M):** express War/Rebellion thresholds and faith tables as data; Khan's horde check
   becomes a form tag rather than an enum test; `PolityForm` becomes a data id.
3. **Jint hooks (M):** extend `RoguelikeToolkit.World.Scripting.Jint` (audit `JintWorldStage`'s
   sandbox first): pure `precondition/weight/propose` functions over a read-only snapshot; no
   `Math.random`/`Date`; step/memory limits; script hash in the save header and cache key.
4. **Docs and samples (S).**

Exit: a Gurubashi-style collapse and a crown-wars succession are defined mostly in data/script and
run deterministically.

## Cross-cutting

- **Process:** one PR per step; each ends green; no new features in M1; every behaviour change is
  listed in the PR description.
- **Docs:** `HISTORY_PLAN.md` P3/P4 are re-pointed to M2/M3 once M1 lands (P3 naming rendering reads
  ids and tags and is unaffected by the kernel; P4b editor panel consumes exports only).
- **Open questions carried over:** pin/rename resolution when a polity splits or ends; turn length
  for shocks; per-settlement population vs per-polity only; whether cultures get their own
  territory or derive from settlement ownership.
- Done (HolySite): `HolySiteOperator` replaces `HolySiteStage`. It fires only on the first simulated turn (turn 1; legacy
  used turn 0, so `FoundedTurn` and the `HolyFound` event turn are now 1) while no site exists. Aura relief is a
  `-0.1` pressure delta via `AdjustScalar` (clamped at 0 once at commit). Ended polities do not raise sites.
  `HolySiteOperator.FaithFormOf` stays the shared faith-form lookup. `HistoryFaithTests.Operators_EmitTheirEventKinds_AcrossSeeds`
  is red until M1.5 (it needs the full pipeline).
- Done (War): `WarOperator` replaces `WarStage`. Reads pressure, grudges and windows from the snapshot, so the
  time-travel leak (legacy mixed closed-form pressure with later-stage grudges) is gone by construction; pressure
  is the causal scalar (Population's crowding + war bumps), not `PressureAtTurn + EventPressureBonus`. New
  `AddSite` proposal (battlefield sites; `TurnView.HasSite`), `GrudgeEntry.FirstTurn`/`LastTurn` stamps (set by the
  driver commit, not yet persisted: M1.4), war and desecration pressure bumps via `AdjustScalar` (clamped once).
  RoadCut is emitted once per pair per turn and only when an unsevered route exists in the snapshot.
  Desecration `Cause` is left unset (the war event of the same turn has no stable id yet).
- Done (Conquest): `ConquestOperator` replaces `ConquestStage`. Resolves wars logged on the *previous* turn
  (accepted one-turn lag), compares snapshot populations (legacy used closed-form `PopulationAtTurn` at the war
  turn), and cites the war event as `Cause` (first event with a cause). New `SetOverlay` proposal;
  `TurnView.PolityState.CultureOverlay`. Repay-grudge and event ride along in the same commit.
- Done (Khan): `KhanOperator` replaces `KhanStage`. Populations come from the snapshot (legacy used closed-form
  `PopulationAtTurn`); "pressed once per ordered pair" is derived from the event log (`Tribute from:` /
  `Ultimatum weak:`) instead of a local set, so the operator holds no per-run state. Submission emits
  RepayGrudge + AddWindow + pressure deltas; resistance emits AddGrudge + pressure deltas.
  `PolityState.FoundedTurn` added. `HistoryCoverageTests.Khan_*` now drive the operator through the driver
  (with `PopulationOperator`); the grudge-after-ultimatum check allows a later tribute in the reverse direction to repay it.
- Done (Treaty): `TreatyOperator` replaces `TreatyStage`. New `SetHeir` proposal and `PolityState.HeirBornTurn`;
  a polity marries at most once per turn (local set replaces the legacy sequential heir check). Refusal grudge
  weight tag is snapshot weight plus the refusal. `IsHeirAdult` takes the heir birth turn. The Treaty coverage
  test now runs the operator through the driver on the built world.
- Done (Rebellion): `RebellionOperator` replaces `RebellionStage`. New `AddPolity` proposal, applied in a structural
  pre-pass of the commit (ids must be the next free id, the polity cap is enforced there and counted in
  `RejectedProposals`), followed by one ownership re-flood from all capitals (user pins win) and a holy-site owner
  sync (`HistoryHolySite.PolityId` is now settable). `HistoryDriver.Commit` now takes the map and context;
  `TurnView.MaxPolities`. Behaviour changes on purpose: the rebel is born with population 0 and pressure at its
  default, and `PopulationOperator` seeds it on the next turn (legacy seeded `InitialShare` of capacity
  immediately); pressure is the causal scalar; several rebellions in one turn all read the same snapshot ownership.
  The two legacy rebellion tests (coverage + cap) are replaced by `RebellionOperatorTests` with a forced tight-culture fixture.
- Done (Villain): `VillainOperator` replaces `VillainStage`. New `AddVillain` proposal (kind 14, so it commits after
  `SetPopulation` and its ruler generation wins); the commit enforces `MaxVillains` and one villain per polity.
  `PopulationOperator` now keeps `RulerGeneration` frozen for a polity with a villain (legacy relied on stage
  order). `TurnView` gains `MaxVillains`, `VillainCount`, `PolityState.HasVillain`/`RulerGeneration`.
- Done (Exchange): `ExchangeOperator` replaces `ExchangeStage`. New `DriftOpenness` proposal (bounded +1 per polity per
  turn, deduped in the operator). Active routes come from the snapshot, so an exchange can occur on the very turn a
  war severs the route (simultaneous under A1); route RNG key is `endA*100003+endB` (stable as routes accrue, legacy
  used the index in the end-of-run route list). Remaining in M1.3: Faith and CityHistoryProject as post-passes.
