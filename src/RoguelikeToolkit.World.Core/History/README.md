# History

The pluggable history simulation: polity formation, tile ownership, turn driver, city
projection, event log, and the user-override layer. Managed on `World` as `HistoryCatalog`.

Namespace `RoguelikeToolkit.World.Core.History`, layer rank 4 (see `docs/ARCHITECTURE.md` and
`ArchitectureTests`). Reads physics slices (`Elevation`, `Hydrology`, `Climate` via capacity,
`Settlement`, `Lithosphere`, `Features`) downward; the single upward edge is the catalog
implementing `IHistoricalContext` (one waiver: `History->Integration:HistoryCatalog`).

## Contents

- **HistoryCatalog**: managed state (polities, ownership map, roster, settlements, sites, roads,
  events, overrides, stale/fork flags); implements `IHistoricalContext`; binary `Save`/`Load`
  with `SchemaVersion`; `Respatialize` re-resolves the projection after map edits.
- **Polity** / **PolityForm**: polity identity (id = index) and government form
  (kingdom / merchant-republic / loose-association / horde).
- **HistoryEvent** / **HistoryEventKind**: tag-only append-only log (founding / fork / site-lost
  in P2a; war / conquest / rebellion / road-built / road-cut / exchange in P2b); no rendered
  prose, so stubbed text providers cannot change it.
- **HistoryPeople**: one (folk, culture) roster pair.
- **HistorySettlement** / **HistorySite** / **HistoryRoad**: projected places (capital sites plus
  war battlefields; road segments grouped into capital-to-capital routes).
- **HistoryOverrides**: user renames + pinned tiles; no stage writes it; survives recompute and resim.
- **HistoryContext**: turn length (default 10y), span, per-turn budgets (N decisions / M emergences /
  K events), per-operator per-turn ledgers (a noisy war exhausts only itself), log cap, polity cap,
  peoples roster.
- **HistoryDemography**: shared people-and-time math (carrying capacity, logistic growth, crowding
  pressure, folk generation length, route tags, biome fallback surplus).
- **HistoryRng**: every draw derives from `(seed, stage, turn, entity, purpose)` via FNV-1a salts;
  operator order never changes outcomes.
- **HistoryTerritory**: the one deterministic ownership function (nearest-capital flood fill, pins win).
- **IHistoryPlugin** / **HistoryPluginBase**: history stages as pipeline stages with
  `Precondition`/`Apply`; missing attachments make a stage a silent no-op.
- **CultureSeedStage** (26) / **PolityFormStage** (27) / **HistoryDriver** (28): the turn-major driver.
  Per turn it snapshots the catalog into a `TurnView`, runs every `IHistoryOperator`, and commits the
  proposals in canonical order (entity, kind, operator, sequence), so registration order never changes
  bytes. After the last turn it runs the `IHistoryPostPass`es.
- **PluginStateBag** / **HistoryNaming**: keyed versioned blobs saved with the catalog, and the `namespace/name` and order-band rules for plugins.
- **StandardHistory** registers the built-in ruleset: Population (32), TradeRoad (30), HolySite (44),
  Khan (46), Treaty (48), War (50), Conquest (60), Rebellion (70), Villain (76), Exchange (80), and the
  post-passes Faith and CityHistoryProject. `WorldBuilder.WithHistoryDriver` adds to it; `HistoryDriver.Clear`
  starts from an empty ruleset.

## Writing a plugin

A plugin is a set of operators, optional scalars and optional post-passes, all outside Core and built on the
public API. Two samples: `samples/HistoryPlugin` (one operator, one scalar) and `samples/EconomyPlugin`
(five scalars, two operators, plugin events, a cumulative state-bag ledger).

1. **Name everything `namespace/name`** (`HistoryNaming`): operators, scalars, event kinds, state keys.
   Operators run at orders 100..899 (built-ins sit below 100). Order only decides when `Propose` runs, never outcomes.
2. **Declare scalars** with `ScalarRegistry.DeclarePlugin` through `WorldBuilder.WithHistoryScalars`, and add
   operators with `HistoryDriver.AddPluginOperator` through `WorldBuilder.WithHistoryDriver`. Declaration order fixes
   scalar ids, and the catalog saves the definitions.
3. **Read the `TurnView`, emit proposals.** The view is the end of the previous turn and read-only. Emit through the
   `ProposalSink`: `Adjust` (scalar deltas, summed then clamped once), `Event`, `PluginEvent` (your own kind, in your
   namespace), `SetState` (a blob in the plugin state bag, in your namespace), `Rng(entity, purpose)` for draws and
   `TrySpendDecision`/`TrySpendEmergence` for budgets. Nothing you propose is visible until the next turn.
4. **Persist private data** in the state bag (`ProposalSink.SetState`, `TurnView.ReadPluginState`, or
   `HistoryCatalog.PluginState` from a post-pass). Blobs are versioned; a mismatch reads as `VersionMismatch` and a
   plugin may `Reset` with a recorded note. Blobs survive `Respatialize` and save/load.
5. **Be inert when your scalars are missing.** Resolve scalars with `view.Scalars.TryGet` and return early, so a
   host without the plugin generates bytes identical to the base preset (both samples have a test for this).
6. **Hosts** register plugins before generating: `MapEditor.Engine.HistoryPluginHost.Register(operators, scalars)`.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- Stages write no tile layer: they fill the attached catalog, so physics goldens never move.
- `World.Recompute` runs orders 15..25 then 26+ except history plugins; the catalog is kept,
  re-resolved, and marked stale. `World.RegenerateHistory` re-runs only the history band.
- Event tags are the rendering contract, never prose (P2c/P3 render late from tags).
- Turn discipline: the driver (28) advances the clock; every operator loops turns 1..`TurnCount`-1
  in deterministic polity order with its own `HistoryRng` streams (`seed, stage, turn, entity,
  purpose`), so operator order never changes outcomes and no operator reads another's draws.
- Budget discipline: wars/conquests spend decisions, roads/rebellions spend emergences, each from
  the spending operator's own per-turn ledger; exchange spends nothing (the road network is the
  throttle). The catalog's per-turn event cap and log cap bind everybody.
- Causality across stages: stages run the whole span one after another, so a later stage reads an
  earlier stage's full time series through turn-tagged state (road found/cut turns, war turns for
  pressure bumps, overlays). The demography base (capacity, trajectories) is recomputed from the
  same pure helpers everywhere, never copied.

## Operators (P2b)

- **TradeRoad** (30): surplus-complementary capital pairs (grain vs herd/timber) within 30° of arc
  connect along the cheapest land path (river/coast/low-danger preferred, 600 tiles max). One
  emergence per route; re-running is idempotent (connected pairs are skipped).
- **Population** (32): logistic growth of an abstract population index against territory capacity
  (annual folk `GrowthR` compounded over the turn), crowding pressure from density, one succession
  per folk generation. No RNG, no budgets, no events.
- **War** (50): crowded polities raid bordering polities (pressure + founding-culture xenophobia);
  each war logs a battlefield site, severs the pair's trade routes (pressure both sides), and bumps
  both pressures. Raids only — no tile changes hands.
- **Conquest** (60): a war becomes a culture overlay when the attacker outpopulates the defender
  1.5×; the loser renders through the winner's culture (late, in `Names`). Overlay only, no tiles.
- **Rebellion** (70): a large, tight-culture (collectivism 60+), oppressed (pressure 0.35+) polity
  may lose its most distant province (12°+ away) as a loose association; ownership re-floods from
  all capitals. Bounded by the polity cap; new rebels start calm.
- **Exchange** (80): active routes diffuse loanwords plus one openness drift per polity per turn
  (capped at ±20). No budget; the network throttles it.

## Treaty, faith, optionals (P2c)

- **HolySite** (44): each founded polity may raise a shrine on its best non-capital tile (shelters it,
  relieves pressure); a war on that tile logs a desecration and raises grudges plus a coalition.
- **Khan** (46): horde polities press weaker neighbors once per pair; submit (tribute + window) or
  resist (grudge + pressure).
- **Treaty** (48): bordering pairs marry, then foster once heirs are adults (adult gate); refusal logs
  a grudge. Success opens a non-aggression window and repays grudges.
- **Villain** (76): long-life folk polities under pressure may gain a villain (capped, agenda,
  succession frozen).
- **Faith** (84): form from culture, 2-7 powers from lived events, myth slots; text renders late via
  `Integration.IMythTextProvider`.
- Grudges (`GrudgeEntry`) never decay by time; only repayment clears them.

## Ownership-map spike (P2a)

Decision: the ownership map is a managed `int[]` on the catalog (tile to polity id, -1 unclaimed),
not a store field. Rationale: even a size-7 planet needs ~640 KB; lookups are point queries
(`OwnerOf`), never hot loops; a store field would cost a layer slot plus recompute-clearing
complexity for no measured gain. Revisit only if spatial queries go hot (see history plan risks).

Measured (`HistoryBenchmarks`, BenchmarkDotNet, Release, Hyper-V VM):

- Size 6 (~41k tiles): full history band `History_Size6` ≈ 235 ms / 6.9 MB; no-op
  `Respatialize_Size6` ≈ 0.5 ms / 164 KB (one ownership array plus pins).
- Size 7 (~164k tiles): full history band `History_Size7` ≈ 0.8–1.2 s / 26 MB
  (dominated by the local deposit view inside `PolityForm`, which the build recomputes anyway).

Perf budget (P2a): the full history band over the default 30-turn span must stay under 5 s at
size 7 on desktop (two orders of magnitude above measured); WASM is bounded by the same
algorithm (linear scans + one flood fill, no per-tile allocation in the hot loop).
