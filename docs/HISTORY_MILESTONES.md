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
- Done (post-passes): `FaithPostPass` and `CityHistoryProjectPostPass` (`IHistoryPostPass`, run once after the last
  turn; both no-op when their lists are already filled) replace `FaithStage` and `CityHistoryProjectStage`. Faith now
  also counts a war the polity only defended (event tag `defender:`), the absorbed review finding; participant
  roles replace the tag scan at M3. M1.3 is complete: the `WorldBuilder` pipeline is now CultureSeed, PolityForm,
  HistoryEventsStage and everything else waits for the driver wiring at M1.5.
- Full Core suite after M1.3: 750 passed, 3 red until M1.5 (`HistoryOperatorsTests.Population_TicksForEveryPolity`,
  `HistoryTests.HistoricalContext_ServesCapitalSites_NoModifiers`, `HistoryTests.Build_FoundsPolities_WithFoundingEventsAndProjections`:
  they need Population and the city post-pass wired through `WorldBuilder`). Other test projects not run yet.

**M1.4 progress.**

- Done (persistence): `HistoryCatalog.SchemaVersion` = 4. Adds the string table, declared scalars, next event id, per-polity
  lifecycle (`EndedTurn`, `EndCause`, `Parent`) and scalar values (all declared slots), event id / cause / importance /
  participants, and grudge `FirstTurn`/`LastTurn`. Load validates parents, end causes, event ids (unique, below next id),
  causes (earlier id), importance and participant ids/roles. v1-v3 files still load through the legacy readers
  (no regenerate-on-load: the catalog cannot re-run the pipeline); overrides were always persisted in every version.
  Tests: `HistoryPersistenceV4Tests`.


**M1.5 progress (switch):** `StandardHistory.Register` registers the ten operators and two post-passes; `WorldBuilder` now adds
a `HistoryDriver` carrying them (`WithHistoryDriver(Action<HistoryDriver>)` adds more; `HistoryDriver.Clear()` for bare rulesets,
used by the per-operator tests). `HistoryEventsStage` is deleted. All test projects green (Core 760, MapEditor.Engine 48,
Presentation 103, Scripting.Jint 27; Benchmarks and AotSmoke build). New `HistoryAcceptanceTests`: determinism + round trip on the
standard ruleset, and **prefix stability** (TurnCount 12 equals the turn < 12 events of TurnCount 30; the grudge leak is gone).
Corpus re-baselined (`docs/history-reference-corpus.md`, 25 worlds): roads 0 -> 12 (all severed), RoadBuilt/RoadCut 0 -> 3,
Exchange 0 -> 10 (Finding B fix, intended); War 587 -> 969, Conquest 46 -> 88, Desecration 33 -> 56, sites 356 -> 509, grudges 68 -> 81
(pressure is now the causal scalar from Population crowding plus war bumps, war battlefields are `AddSite` proposals, and grudges
are weighed turn by turn instead of end-state; wars are the largest shift and worth a second look at M2 tuning); villains 50 -> 49;
all other kinds identical. Remaining: size-7 perf check against the 5 s budget, `HISTORY_PLAN.md` P2 notes, compat-rule validation and
pinned-capital handling (move to M2 unless trivial), `StageOrder` constants stay (operators use them as `Order`).
Perf (Debug build, size 7, seed 42, one-off timing): full build 13.9 s vs 10.7 s without history, so the history band costs about 3.2 s, inside the 5 s budget (Release numbers via `LargeHistoryBenchmarks` still to record).

**Release perf (M1.5):** `LargeHistoryBenchmarks.History_Size7` = 400 ms mean (±87 ms CI), 147.6 MB allocated, against the 5 s budget.

### M2.1 progress (settlement as entity, fields only)

`HistorySettlement` gained `EndedTurn`, `EndCause`, `Tier` (`SettlementTier`), `Role` (`SettlementRole`) and `Parent`; `PolityId` is now settable so conquest can transfer a settlement. `Ended` (historical) is separate from `Active` (physical, re-resolved on edits). Schema v5; v1-v4 load with capital defaults. No behaviour change: capitals are still the only settlements, corpus unchanged. M2.2 note: `CitySiteScorer.Score` is already a pure ranked-candidate function of the map, so no new scorer is needed; the settle operator will call it with a history-aware filter. Capital-founding events are deferred to M2.3 (they would change the event log, so they ship with the settle operator and a corpus re-baseline). Core 763 green.

### M2.3 design (settle operator) — proposed

Findings that shape it: settlements are projected at order 91, after every turn, so operators cannot
see them; `CitySiteScorer.Score` scans the whole map per call (cache the ranked list per operator
instance, the map is static during history); `Polity.Population` is an index against tile capacity,
not a head count.

Slices, each its own commit:

- **A. Settlements exist during the turn loop (no behaviour change).** Capital settlements are
  created where polities are created: `PolityFormStage` (turn 0) and the `AddPolity` commit
  (rebellion). `CityHistoryProject` keeps the habitability/site projection but no longer creates
  settlements. Capitals emit no extra event (the polity `Founding` event already covers them), so
  the corpus does not move. `TurnView` gains `Settlements` (immutable `SettlementState` records).
- **B. `AddSettlement` / `EndSettlement` proposals** (kinds 16, 17, after `AddVillain`). The commit
  creates/ends the entity and appends `SettlementFounded` / `SettlementEnded` events itself
  (atomic: no event without an entity). Conflict key = tile; the commit rejects a tile that already
  holds a standing settlement, a polity over `MaxSettlementsPerPolity`, or a tile the polity does
  not own. Tests per rule.
- **C. `SettleOperator`, colonization source only.** Per polity with pressure over a threshold:
  take the cached candidate list, keep tiles the polity owns, at least `MinSettlementSpacing` hops
  from any standing settlement, rank by score + frontier distance, spend one emergence from the
  operator budget, propose `AddSettlement` (role Colony, tier Hamlet, parent = nearest own
  settlement) plus a pressure `AdjustScalar` relief. RNG tie-breaks via `HistoryRng(seed,
  "settle", turn, polity)`. Registered in `StandardHistory`; this is the first behaviour change, so
  the corpus is re-baselined and documented.
- **D+ (later milestones items).** Other sources (economic node, military post, religious, nomad)
  are further operators/rules on the same proposal; the displaced pool (M2.4) adds per-settlement
  population and the conserved ledger, and refugee settling becomes another source.

Open choices, defaulted unless you object: `MaxSettlementsPerPolity = 6`, spacing 3 hops, pressure
threshold 0.6, relief 0.15 per founding; capitals get no founding event.

### M2.3 progress (settle operator, colonization)

- **A** (08bf93c): capital settlements are created with their polity (formation step and `AddPolity`
  commit); `CityHistoryProject` projects only capital sites. Rebel capitals now carry their real
  founded turn. Also fixed the netstandard2.1 build (`Enum.IsDefined` overload from M2.1).
- **B** (a981af0): `AddSettlement` (16) and `EndSettlement` (17) proposals; the commit validates
  (owned tile, no standing settlement on it, per-polity cap `MaxSettlementsPerPolity` = 6, valid
  parent) and appends `SettlementFounded` (17) / `SettlementEnded` (18) itself. `TurnView.Settlements`.
- **C**: `SettleOperator` (order 34, `StageOrder.Settle`), registered in `StandardHistory`. A polity at
  pressure >= 1.0 and under the cap founds one hamlet colony per turn on an owned tile picked
  (RNG among the top 3) from `CitySiteScorer` candidates at least 4 degrees from any standing settlement;
  parent = nearest own settlement; sheds 0.4 pressure; one operator emergence per founding. Candidates
  come from rivers/water catalogs rebuilt via `HydrologyStage.PopulateCatalogs` once per run (turn 1).
  Standing settlements follow tile ownership on `ReassignTile` (stopgap until M2.9).
- **Tuning note:** the first cut (threshold 0.6, relief 0.15) filled every polity to the cap; pressure
  in default worlds is driven mostly by war bumps (population alone peaks under 1.0).
- **Corpus re-baseline** (25 worlds, sizes 3-4, before -> after): SettlementFounded 0 -> 531, War 969 -> 836,
  Conquest 88 -> 77, Desecration 56 -> 52, sites 509 -> 495. Colonization sheds pressure, which partly
  undoes the M1.5 war-count jump (587 -> 969). Core 776 green; MapEditor.Engine 48, Presentation 103,
  Scripting.Jint 27 green. Release perf not re-measured yet (re-check at M2 close).
- Not done: other founding sources (economic node, military post, religious, nomad), EndSettlement
  producers, tier growth, the displaced pool (M2.4). `CitiesOf` now returns colonies too.

## M2.4 design (stored settlement population, displaced pool)

Decision (per-settlement population vs per-polity only): **stored per-settlement population**, so
local effects (famine, plague, sack) act on one settlement and the macro picture is their sum.
`Polity.Population` stays as the polity's stock but is always the sum over its standing settlements
(re-summed at commit for every polity a proposal touched); a polity with no standing settlement
keeps the legacy single-stock path.

- **A (this slice):** `HistorySettlement.Population` (schema v6). `HistoryDemography.SettlementCapacities`
  gives each standing settlement its catchment capacity (a polity with one settlement gets the whole
  polity capacity; with several, each owned tile counts toward the nearest standing settlement, so
  the settlements sum to the polity). `PopulationOperator` steps each settlement logistically against
  its own capacity and emits `GrowSettlement` (kind 18, a delta so it commutes with colony seeding).
  A colony is seeded at commit with `ColonistShare` (15%) of its parent's population (moved, not created).
  Pressure stays polity-level (`PressureOf(sum pop, polity capacity)`, which is the capacity-weighted
  mean of the settlement pressures); a max over settlements was tried and cut colonization and wars
  too far (War 836 -> 656). Per-settlement pressure returns with the shock resolver (M2.5).
- **B:** the displaced pool ledger (`ReleasePopulation`, `AbsorbDisplaced`, persisted), `EndSettlement`
  auto-releases the settlement's people, pool decay (explicit loss sink).
- **C:** refugee settling as a `SettleOperator` source (Refuge role, defensible/coastal candidates).

### M2.4A progress

Corpus re-baseline (25 worlds, before -> after): SettlementFounded 531 -> 407, War 836 -> 759,
Conquest 77 -> 76, sites 495 -> 491. Colonies now split capacity and population, so wars no
longer leave populations untouched: `StarvedWar_LeavesRoadBuildingUntouched` pins
`MaxSettlementsPerPolity = 1` to compare demography. Known gap until 2.4B: an ended settlement's
people vanish (no producer ends settlements yet).

### M2.4B progress (displaced pool)

- `DisplacedGroup { PolityId, Culture, Turn, Amount, Origin, Cause }` on `HistoryCatalog.Displaced`, plus `PopulationLost` (the only non-conserving sink). Schema v7 (block written right after the settlements).
- Proposals `ReleasePopulation` (19: share of a standing settlement's people into the pool) and `AbsorbDisplaced` (20: pool into a standing settlement of the same polity; clamped to the group's remainder; cross-polity asylum waits for M2.9). Refusals count in `RejectedProposals`.
- `EndSettlement` commit auto-releases the settlement's whole population (group `Cause` = the `SettlementEnded` event id), so no producer can leak people. A polity that lost its last standing settlement gets stock 0.
- Pool decay in the commit (`HistoryContext.DisplacedDecayTurns`, default 3): stale groups settle into the origin polity's first standing settlement, else are recorded in `PopulationLost`. Drained groups are dropped at end of commit.
- `TurnView.DisplacedPool` / `DisplacedState`. Tests: `DisplacedPoolTests` (5). No corpus movement expected (nothing ends settlements yet). Core 785 green; netstandard2.1 build clean.
- Next: M2.4C refugee settling in `SettleOperator`.

### M2.4C progress (refugee settling)

- `Proposal.AddSettlement.FromGroup` (default -1): when it names a pool group of the founding polity with people left, the whole remainder seeds the new settlement (moved, not created) instead of the parent's `ColonistShare`. A bad or foreign group falls back to the colony path. The commit marks the polity dirty so the stock re-sums.
- `SettleOperator` now settles the pool first, per polity: a group of at least `RefugeFoundShare` (0.5) of the polity's mean standing-settlement population founds one `Refuge` hamlet per turn (under `MaxSettlementsPerPolity`; spends one emergence). Sites prefer CitySiteScorer candidates tagged `high` or `water` (defensible / coastal), RNG among the top 3; the parent is the group's origin settlement if standing, else the nearest. Every other group is `AbsorbDisplaced` into the standing same-polity settlement nearest its origin. A turn that founds a refuge does not also found a colony for that polity.
- Deviation from the design note: the threshold is relative to mean settlement population, not to catchment capacity, because capacity is not exposed to operators. Revisit with per-settlement pressure at M2.5.
- Same-polity only until M2.9. Nothing real ends settlements yet, so the corpus is unchanged (expected, verified by the suite). Tests: `RefugeSettlingTests` (3: large group founds a refuge and conserves, small group merges, determinism), driven by a synthetic releaser.

### M2.5A progress (shock resolver)

- `Shock` (kind, severity 0..1, footprint Tile / Radius / Polity / Global, casualty and displacement shares, tags) and the static `ShockResolver.Resolve(view, sink, shock)`. Any operator or plugin builds a `Shock`; the resolver emits proposals only.
- Per standing settlement in the footprint: intensity = severity x reach (linear falloff for Radius) x defence (tier: hamlet 1.2 .. city 0.7; role: capital 0.8, post 0.7, refuge 1.15). A roll from `Rng(settlement, "shock:" + kind)` (so `(seed, operator, turn, settlement, purpose)`) below the intensity strikes it.
- Struck: `ReleasePopulation` of `Displacement x intensity` plus the new `KillPopulation` (kind 21, commits after release so its share applies to those who stayed; the dead go to `PopulationLost`). At intensity >= 0.8 the settlement is abandoned: `EndSettlement` with the new `LostShare` (dead recorded as lost, the rest released into the pool, so the refuge flow of M2.4C takes over).
- One `HistoryEventKind.Shock` (19) event per shock that struck anyone: tags `shock:{kind}`, `severity:`, `struck:`; tile = epicentre.
- No producer yet (hazard maps are M2.6), so real runs and the corpus do not move. Conservation holds: the shock tests check standing + pool + lost before and after.
- Per-settlement pressure is the next slice (M2.5B).

### M2.5B progress (per-settlement pressure)

- `TurnView.SettlementCapacity(index)` and `SettlementPressure(index)` (people over catchment capacity), computed once per view from `HistoryDemography.SettlementCapacities`; 0 for ended or unknown settlements. Capacity is now visible to operators.
- Consumers: refugees merged by `SettleOperator` go to the nearest settlement still below `PressureThreshold` (nearest at all as a fallback); `ShockResolver` exposure rises by up to 30% for crowded settlements (+15% per unit pressure, capped at 2).
- Not changed: colony triggering stays on polity pressure, and the refuge founding threshold stays relative to mean settlement population (tuning both on per-settlement pressure waits for real shock producers in M2.6, to avoid moving the corpus blind). The pool is empty in real runs, so the corpus does not move.

### M2.6A progress (hazard maps and the Hazard operator)

- `HazardMaps` (pure function of terrain; seed only picks volcanic vents): earthquake (plate boundaries on land: convergent 1.0, transform 0.8, divergent 0.4, falling off over 6 rings), volcano (convergent arcs and rifts, thinned to `VentShare` 0.35), tsunami (coastal land beside seismic sea floor), flood (river tiles by sqrt discharge and low ground), drought (dry, rain-seasonal land). Missing layers contribute nothing.
- `HazardOperator` (order 36, `StageOrder.Hazard`): per kind and per standing settlement not already covered by a shock of that kind this turn, a stream per (seed, Hazard, turn, settlement, kind) is drawn against `HazardRate * hazard(tile) / ReturnPeriod`. Hits spend an operator emergence and hand a Radius `Shock` centred on the settlement to `ShockResolver`. Drawing from settlements instead of from the empty map was chosen after a first cut where 8 shocks in 10 worlds hit anything. Return periods (turns at hazard 1): earthquake 36, volcano 30, tsunami 40, flood 20, drought 20.
- `HistoryContext.HazardRate` (default 1, 0 = off) via `TurnView.HazardRate`.
- Re-baseline (25 worlds): SettlementFounded 407 -> 436, SettlementEnded 0 -> 35, Shock events 172 (about 7 per world), War 759 -> 753, Conquest 76 -> 75, sites 491 -> 490. Ten-seed probe: roughly 6 shocks and 1.5 abandoned settlements per world, refuges form.
- Test adjustments: the founding-state test pins `HazardRate = 0`; the colony spacing test ignores settlements abandoned before the colony was founded (resettling a ruin is allowed).
- Still to do in M2.6B: famine (polity pressure), plague (density and route connectivity), scars come in M2.7.

### M2.6B progress (famine and plague)

- `HazardOperator` now carries a hazard function per kind. Two state-driven kinds join the map-driven five: **famine** (return period 8, radius 2 degrees, casualty 0.30, displacement 0.40; hazard = settlement pressure above 1.0, clamped to 1) and **plague** (return period 30, radius 6, casualty 0.40, displacement 0.10; hazard = tier density hamlet 0.2 / village 0.4 / town 0.7 / city 1.0, scaled 0.5..1.0 by the polity's open roads up to 8). Route-borne spreading is deferred to M2.8 (roads between settlements). `HazardOperator.Kinds` lists all seven.
- Re-baseline (25 worlds): SettlementFounded 436 -> 462, SettlementEnded 35 -> 50, Shock events 172 -> 241 (about 10 per world), War 758, Conquest 74, sites 489.

### M2.7 progress (scars)

- `HistoryScar` {Tile, Polity, Turn, Kind, Cause, Position, Active} in `HistoryCatalog.Scars` (append-only, schema v8, tile-edit refresh like sites). Kinds: `ruin`, `crater`, `plague_pit`, `scorched_field`, `fissure`; plugins may use their own keys.
- `Proposal.AddScar` (kind 22, conflict per tile; a tile that already carries the same kind is left alone).
- Producers in `ShockResolver`: an abandoned settlement always leaves a `ruin` (cause = shock kind); a settlement hit at `ScarIntensity` 0.5 or more takes the shock's `Shock.Scar`. `HazardOperator` maps earthquake -> fissure, volcano -> crater, drought -> scorched_field, plague -> plague_pit; the rest leave none.
- Projection: `GetSitesNear` returns scars as `Ruin`-kind sites (`hist-scar-{tile}-{kind}`, metadata `scar`, `cause`, `polity`, `founded`); `TryGetTileModifier` adds `ScarDangerDelta` 1 and `ScarHabitabilityDelta` -0.25 for non-ruin scars (ruins are neutral).
- Tests: `HistoryScarTests` (4). Battlefield scars stay on the existing War/AddSite path; folding them in is left for M2.9.

## M2.8 progress (roads between settlements)

- `TradeRoadOperator` now has two phases. Trunks (capital to capital, surplus-complementary pairs) are unchanged. Spurs are new: a standing settlement with no route yet roads itself (`SpurChance` 0.25 per turn, `SpurRangeDegrees` 12, one emergence) to its parent, else its nearest standing neighbour of the same polity. Spur segments carry `PolityId` = the owner, and route endpoints are settlement tiles, so `RouteA`/`RouteB` are no longer always capitals.
- `ExchangeOperator` skips roads with `PolityId >= 0`, so only trunks diffuse culture and openness. Spurs are internal to one polity.
- `HazardOperator` plague: links are now the live routes touching the settlement's own tile (cap 4). The old check compared `road.PolityId` with the settlement's polity, and trunks carry -1, so it never counted any link.
- Spurs are not severed by war yet. That comes with M2.9, which severs routes by settlement.
- Corpus re-baselined. Core suite: 805 green.

## M2.9A progress (war reaches settlements)

- `WarOperator.Assault` runs after each raid: it takes the defender's standing settlement nearest the battlefield and rolls a dedicated `assault` stream. A stronger attacker (`ConquestAdvantage`, and under the settlement cap) may **capture** it (`CaptureChance` 0.2) or **raze** it (`RazeChance` 0.1); a capital is only ever sacked. Otherwise the settlement is **sacked** (`SackChance` 0.5): `SackCasualty` 0.08 die, `SackDisplacement` 0.15 flee into the displaced pool.
- Raze: `EndSettlement{razed, LostShare 0.3}`, a `ruin` scar with cause `war`, and every live route touching the tile is severed.
- Capture: new `Proposal.CaptureSettlement` (kind 23, conflicts per settlement; rejected for a dead captor, a capital, or a captor at `MaxSettlementsPerPolity`) plus a new `HistoryEventKind.SettlementCaptured` (20). Tile ownership is deliberately unchanged: ownership must stay equal to the flood of the capitals, or `Recompute` reports a fork. A captured settlement is an enclave.
- The owner-sync stopgap is replaced: `SyncSettlementOwners` now only moves settlements whose own tile was reassigned in that commit, so captures stick.
- The operator emits the `SettlementCaptured` event whether or not the commit later accepts the capture (a same-turn cap race); in practice rare.
- Tests changed on purpose: demography comparison dropped from `StarvedWar_...` (wars now change populations), parent-polity check skips captured settlements, founding-event count in the cap test counts colonies only, capitals test restricted to capitals.
- Corpus re-baselined: SettlementEnded 50→63, SettlementCaptured 8, War 764. Core suite 806 green.
- Still open for M2.9: cross-polity asylum, battlefield scars (kept on the AddSite path).

## M2.9B progress (cross-polity asylum)

- A polity with no standing settlement can no longer take its displaced people home. `SettleOperator.ProposeAsylum` sends each such group to the nearest standing settlement of any other living polity, preferring one under `PressureThreshold`.
- `TryAbsorb` accepts a foreign target only when the group's own polity has no standing settlement; a polity that still has one cannot send people abroad. The pool invariant is untouched (pool to standing).
- The corpus did not change: homeless polities are rare in the default worlds. Core suite: 807 green.
- Left for later: the settlement-level plague/war interplay is tuned only by the corpus; the strict full-run ledger test and the Gurubashi/Venice cases stay in the M2 exit.

### M2.10 progress (P4a shrink: history decides placement)

- `CitySiteScorer` stays a pure candidate generator (map only, no history input). `SettleOperator` no longer takes the first
  `PickAmong` spaced candidates: it scans up to `ScanLimit` (24) spaced candidates and `BestByHistory` ranks them by
  scorer score x history weight, then picks randomly among the best `PickAmong` (3) on the same rng stream as before.
- Weights: active non-ruin scar within `ScarRadiusDegrees` (3) x0.25; a ruin on the tile x1.5 (resettlement); a live
  (unsevered) route end within `RouteRadiusDegrees` (6) x1.3. Applies to colonies and refuges.
- `TurnView.Scars` / `ScarState` expose scars to operators.
- Corpus re-baselined (12 lines changed). Tests: `SettlePlacementTests` (2). Core suite 809 expected.
- Not done: weights for rivers/resources/faith (scorer reasons already cover water); tuning of weights.

### M2 exit: tuning check (corpus, 25 worlds)

Diagnostic `HistoryCoverageTests.TuningReport_CaptureWhenRequested` (`HISTORY_TUNING_OUT=<file>`) prints per-world wipe-outs, settlement end causes, captures, shocks, wars, scars, lost and pool population.

- Wipe-outs: 0 of 200 polities end homeless; asylum never fires in standard worlds (still covered by unit tests).
- Settlements per world: 16-44 standing, 0-6 ended (famine, earthquake, tsunami, flood, volcano, razed); no plague ends (plague kills population, it does not abandon).
- Captures: 0-2 per world (9 in 25 worlds); razes about 0.3 per world; sacks are the common war outcome. Reads as rare-but-present, no change.
- Scars: 1-6 per world. Colony/refuge thresholds left as they are; pool leftovers at end of run are at most a few thousand and decay in 3 turns.
- Verdict: no constant changes. Tuning stays data-driven for M3.

### M2 exit: paper cases (design doc §13)

`PaperCaseTests` (3 seeds each, scripted catastrophe then the real `SettleOperator`):

- **Gurubashi (collapse):** a razed capital leaves a ruin scar and a displaced pool; the pool then founds refuge towns on other tiles; people conserved.
- **Venice (sack):** a sacked city's survivors found a refuge in the same polity on a different tile; the city stays standing; people conserved.

Not covered here by design: `polity.split` into successors, the faith myth about the fall, merchant-republic form and trade node. Those are M3 (successors/forms), not M2 (people and places).

### M2 exit: determinism, round-trip, caps

`HistoryExitChecksTests` runs full standard histories at HazardRate 3: same seed gives identical `ToBytes` (3 seeds), different seeds differ, save/load is byte-for-byte with scars, ended settlements, refuges and the displaced pool intact, and the event log stays within `MaxEvents` (per-turn check allows 8 over `MaxEventsPerTurn` for priority appends). The `V4_RoundTripsKernelState_ByteForByte` failure seen mid-M2 was stale: it passes at HEAD.

### M2 closed: full regression

- Core 832/832, MapEditor.Engine 48/48, Presentation 103/103, Scripting.Jint 27/27, AotSmoke OK.
- `LargeHistoryBenchmarks.History_Size7` (Release): 721 ms mean, 249 MB allocated. Budget 5 s. It was about 400 ms at M2.3; the growth comes from M2.4–M2.10 (scars, refuges, placement scoring). No action needed, but this is the baseline for M3.
- Hand-off to M3: backlog above, including the RS0016 PublicAPI audit.

## M3 progress

### M3.1 battlefield sites folded into scars

- `WarOperator` now emits `AddScar{battlefield, cause war}` on the battlefield tile (skipped where a capital site already stands, as before). `Proposal.AddSite` has no producer left; it stays until the M3.10 API audit.
- `HistoryScar.Battlefield` projects as a `Battlefield`-kind site (`hist-battle-{tile}-{turn}`, metadata `battle`, `scar`, `cause`), danger `WarDangerDelta`, no habitability change. The event-based projection for capitals and holy tiles is unchanged.
- `AddScar.ConflictKey` is now per tile **and** kind (it was per tile), so a battlefield and a ruin on one tile in one turn both land.
- `SettleOperator` placement ignores battlefield scars: wars are frequent and must not shrink the placeable map. Only the shock scars (x0.25) and ruins (x1.5) weigh in.
- Corpus re-baselined (site and scar columns). Tests: `BattlefieldScarTests` (2); two war tests accept a scar as well as a site.

### M3.2 placement weights: rivers, fishing, faith
- Scorer (`SettlementScoring`, `CitySiteScorer`): `River` (+0.5, river on or beside the tile) and `Fishing` (+0.25, open sea or lake beside it, not seasonal ice) on top of `FreshWater`; reasons `river`, `fishing`. This changes every planet-scale site score, not only history runs.
- `SettleOperator.BestByHistory`: `HolySiteWeight` x1.2 within `HolySiteRadiusDegrees` (4) of an active holy site.
- Resources are deferred to the economy plugin (Core has no resource data). A river-mouth-on-trade-route term goes with M3.7.
- Corpus re-baselined; Core 836/836.

### M3.3 tier growth and decline with hysteresis
- `TierOperator` (order 35, `StageOrder.Tier`): a settlement's fill (people over catchment capacity) moves it one tier per turn. Up thresholds hamlet..city 0 / 0.25 / 0.5 / 0.8; down thresholds 0.12 / 0.3 / 0.5, well under the up ones, so a settlement near a boundary holds (hysteresis). Settlements younger than 10 turns keep their starting tier; capitals never fall below Town.
- New `Proposal.ChangeTier` (conflicts per settlement; commit refuses jumps, foreign owners and capitals below the floor) and `HistoryEventKind.SettlementTierChanged` (21) with tags `settlement:{i}`, `up|down`, `from:`, `to:`. No schema change: tier was already persisted.
- Behaviour change: colonies founded as hamlets can now reach City, and tier now feeds shock defence and plague density. Corpus: 542 tier events over 25 worlds (about 22 per world). Core 840 green.
- Not done: thresholds are stateless; a dwell-time rule would need a persisted last-change turn (schema bump).

### M3.4 other founding sources

- New `FoundingSources` (internal), called from `SettleOperator` after the refuge step so all sources share one candidate list. One special founding per polity per turn, each rolled with a pure `(seed, turn, polity, source)` draw and each spending one emergence.
  - **Post** (garrison): 4% per turn when a living grudge rival, not under a treaty window, has a settlement within 8°; prefers "high" ground; at most 2 per polity.
  - **Shrine** (pilgrimage): 15% per turn per active own holy site with no shrine within its 4° radius.
  - **Node** (route): 6% per turn; river or water site within 3° of an own live road end; at most 2 per polity.
  - **Sedentarization**: a Horde that still holds only its seat after 20 turns founds a Colony (10% per turn), preferring river or water.
- `Proposal.AddSettlement.Why` adds a `why:garrison|pilgrimage|route|sedentarized` tag to `SettlementFounded`. No schema change.
- Corpus (40 worlds, sizes 3 and 4): 123 pilgrimage, 20 garrison, 18 route, 0 sedentarized (hordes rarely sit at one settlement after 20 turns; the path is untested by full runs). Colonies 640, refuges 52. Re-baselined.
- Tier and shock defence already key off role, so posts and shrines pick up their weights with no further change.

### M3.5 pinned capitals and compat-rule validation

- **Pinned capitals:** `HistoryTerritory.AssignOwners` now ignores a pin on any polity's capital tile. Before, such a pin took the seed away from its polity, which stayed alive owning nothing. The capital wins; the pin is kept but inert. A pin on a tile of an ended polity, and what happens to pins when a polity splits (M3.6), are still open.
- **Compat rules:** the loader already rejected blank, self-paired, duplicate and unknown-rule rows. It now also reports a user-supplied (override-dir) row that names a folk with no definition, since a typo there silently left the pair blending. Shipped rows are exempt on purpose: they rule on demon and angel, which have no folk file yet.
- No generation output changes (no pins in generated worlds), so the corpus is untouched.

### M3.6 polity collapse and successors

- New `CollapseOperator` (order 72) and `Proposal.SplitPolity`. A polity with 20+ tiles, 4+ standing settlements and pressure 0.7+ collapses at 5% per turn. Its 2-3 outlying settlements (farthest from the old capital and from each other) become capitals of successor polities (`LooseAssociation`, same folk and culture, `Parent` set). The parent ends (`EndedTurn`, end cause `collapse`), its capital settlement is razed (30% die, the rest go to the displaced pool for refugee settlement), and ownership is re-flooded.
- New event `HistoryEventKind.PolityCollapsed` (22) with tags `collapse` and `successor:{id}`. New `HistoryContext.MaxSuccessors` (default 6): a separate budget from `MaxPolities`, because founding is capped at the living-polity count and collapse replaces a polity. No schema change: `EndedTurn`, `EndCause` and `Parent` were already persisted.
- **Pins and renames, decided:** a pin naming an ended polity stays stored but does nothing; its tile goes to whichever successor the flood reaches (`HistoryCatalog.FloodOwnership`, now shared by `Respatialize` and the commit). A rename stays on the dead polity's id and is not inherited by successors; they render as new polities. This answers the plan's open question with "successor by flood, rename not inherited".
- The razed seat leaves a `ruin` scar with cause `collapse`. Tests that assumed one owner per colony family, shrine-owns-a-holy-site, or war-only razings now allow for collapse.
- Standing settlements of the ended polity pass to the successor that owns their tile.
- Still open: a **merge** or absorb of one polity into another, and the faith myth about the fall (M3.8).


### M3.7 merchant-republic successors, trade nodes, river-mouth placement

- `PolityForm.MerchantRepublic` and `SettlementRole.Node` already existed (founding form; M3.4 route nodes), so this slice wires the Venice case rather than adding types. No schema change.
- Collapse successors take their form from the capital tile: touching open water makes a `MerchantRepublic`, else `LooseAssociation` (`PolityFormStage.IsCoastal`). A sacked state's coastal remnants become trading states.
- Republics found route nodes at twice `NodeChance` and hold one more node (`FoundingSources.RepublicNodeFactor`, `RoleCap + 1`).
- Placement: `SettleOperator.RiverMouthWeight` (x1.25) on top of `RouteWeight` for a site with river and fishing reasons on a live route (a river mouth). Applies to every history founding that ranks through `BestByHistory`.
- Form still never changes after founding; "the sack makes the republic" is expressed through which successors form, not a form-change proposal. A `ChangeForm` proposal stays out of scope.
- Test: successor form follows `IsCoastal` across 20 seeds. Corpus re-baselined.
