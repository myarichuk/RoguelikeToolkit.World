# History: generic entity, event and shock model (DRAFT)

Status: design draft, not implemented. Supersedes the "fixed stage set" assumptions in
`HISTORY_PLAN.md` P2c/P3/P4 where they conflict. Nothing here changes code until agreed.

Decisions so far (owner):
- Driver: turn-major (8, option A) with the snapshot/commit read rule (A1).
- M1 is a pure port of the existing stages onto the new kernel: no new features, history goldens
  re-baselined once.
- Small review fixes (null-safe `PeoplesStore` validation, `HistoryCatalog.Load` reference checks)
  landed ahead of M1; holy-site ownership staleness and pinned-capital handling are left to the new
  ownership model.

## 1. Why

An audit of the landed P2a–c code (History slice) found the model is closed:

- `Polity` is the only entity and has no end: no dissolve, absorb, split-into-successors or fade.
  Rebellion only adds a `LooseAssociation`; conquest is a culture-overlay string, no tiles move.
- Cultures and subcultures are string keys (`CultureKey`, `CultureOverlay`), not entities.
- Settlements are projected once, at order 91, one per polity (the capital). No operator sees them;
  `Active` only reflects map edits, not history.
- `HistoryEventKind` is a 17-value `byte` enum; `PolityForm` is 4 values; faith powers and villain
  agendas are fixed arrays; `Pressure` is the only state scalar.
- ~40 tuning `const`s live inside stage classes; rules are code.
- Stages run the whole span one after another, so no feedback loop (war -> economy -> war) can exist.

Goals this model must serve: Faerunian-style succession/crown wars, WoW Gurubashi-style empire
collapse into successor tribes with ruins, settlements as historical events (colonization, refugees,
sack, plague, disaster), an economy (taxes, estates, corruption, resources, law), natural disasters
from the physics layers, and a library user composing their own history (MapEditor as the
"game-lite" reference).

## 2. Principles (kept from P2)

- Determinism: every draw is a pure function of `(seed, stage, turn, entity, purpose)` (`HistoryRng`).
- The event log is tag-only; prose renders late from tags. Text providers never affect simulation.
- Aggregates, not individuals. Named actors exist only where they matter (rulers, founders,
  magistrates, villains), as P0 `Actor`s.
- Per-operator per-turn budgets bound cost and noise.
- Stages write no tile layer. History fills the catalog; physics goldens never move.
- New: every shock leaves a **scar** (tagged site / tile modifier). This is a roguelike toolkit:
  ruins, craters and battlefields are content a game can consume.

## 3. Entity model

One shape for every historical thing, so lifecycle, persistence, events and plugins share code.

```
HistoryEntity
  Id            int (index within its kind)
  Kind          interned string id  (polity, culture, settlement, site, route, faith, actor-ref)
  Tags          interned string ids
  FoundedTurn   int
  EndedTurn     int   (-1 = alive)
  EndCause      interned string id (collapse, absorbed, razed, abandoned, faded, ...)
  Parent        entity ref (-1 none)       // split / successor / colony-of
  State         named scalars (see 4)
```

| Kind | Lifecycle (beyond found/end) | Notes |
|---|---|---|
| polity | split into successors, absorbed, collapsed, vassalized, merged | `Form` becomes a data id, not an enum |
| culture / subculture | branch from parent, drift, fade, merge | replaces `CultureKey` strings; overlay = ownership of tiles/settlements by a culture entity |
| settlement | tier ladder (hamlet..city) up/down, role change, owner transfer, relocate, abandon, raze, rename | owner can differ from founder; ruins persist as sites |
| site | battlefield, holy site, wonder, ruin, scar | already exists as `HistorySite`/`HistoryHolySite`; unify |
| route | built, upgraded, cut, abandoned | connects settlements, not just capitals |
| actor | born, rules, dies | P0 `Actor`, referenced by id; rulers/founders/magistrates |

Population is a conserved quantity: settlement and polity populations are derived from, or reconcile
with, one demography ledger (see 7).

## 4. State scalars

Named, declared, clamped per entity kind: `pressure`, `legitimacy`, `wealth`, `unrest`,
`corruption`, `tax`, `faith-fervor`, `hazard-exposure`... A scalar is a (kind, name, min, max,
default) declaration. Today's `Pressure` is the first declared scalar; every stage that bumps it
keeps doing so through the same API. Plugins declare their own scalars; they are saved in the
plugin state bag (9). Estates are three scalars that sum to 1 (nobles, merchants, commons).

## 5. Event model

- `Kind` is an interned string id (`war`, `plague`, `settlement.founded`, `settlement.razed`,
  `polity.split`, `shock.earthquake`...). The enum goes away; the save format stores indices into a
  string table (schema bump).
- An event carries: turn, kind, **participants** (entity refs with a role: `actor`, `target`,
  `origin`, `destination`), a **cause** (ref to the event that triggered it, or none), location
  (tile), and tags. Cause links make "this war began because of that desecration" queryable.
- **Importance class** (founding / structural / minor). Compaction (log cap) drops minor first and
  never drops an event that a retained event cites as cause. This fixes the review finding that
  compaction erases War events that Conquest/Faith/battlefield lookups depend on.
- Derived indexes (by tile, by entity) are built at load/after-stage, replacing per-query log scans
  (`TryGetTileModifier`, `IsBattlefield`).

## 6. Shock model (disasters, plague, sack, route shift...)

A **shock** is an event that harms places. Anything can emit one; the core resolves the outcome.

```
Shock
  Kind        interned id (earthquake, plague, famine, sack, drought, flood, ...)
  Severity    0..1
  Footprint   tile | radius(tiles) | route-borne(route set, decay per hop) | polity | global
  Tags        e.g. magical, divine
```

Resolver (core, shared): for each settlement in the footprint, outcome =
f(severity, role, tier, terrain, defenses, shock-kind affinity, RNG stream per
`(seed, "shock", turn, settlement)`). Outcomes: unharmed, shrink one tier, raze, abandon, plus
population released. Plugins that define a new disaster supply only the hazard and the footprint.

**Hazard sources from physics layers** (hazard maps are deterministic from the seed; the per-turn
draw against return period is stochastic):

| Shock | Hazard from |
|---|---|
| earthquake, volcano | `Lithosphere` plate boundaries, hotspots |
| tsunami | coast tiles near quake/volcano footprint |
| flood | `Hydrology` floodplains, river size |
| drought, famine | `Climate` variability, capacity vs population |
| storm, wildfire | `Climate`, biome |
| plague | density (settlement tier) and trade-route connectivity; spreads route-borne |
| river shift, silted harbor | `Hydrology` (cosmetic tile modifier first) |
| fantasy (alchemical blast, rift, dragon, undead blight) | plugin-defined, no physics source |

**Scar:** each shock leaves a persistent tagged site or tile modifier (ruin, crater, scorched
field, plague pit, battlefield). Indexed, queryable, exported to games as points of interest.

**Displaced pool:** when a settlement shrinks, dies or is sacked, released people go into a pool per
(polity, culture) at the turn. A `Settle` operator absorbs the pool: into existing settlements
nearby or into a new founding. This single mechanism produces refugee towns (Venice), post-war
resettlement, colonization overflow and forced deportation, and keeps population conserved.

Budgets: disasters are epoch-scale at 10-year turns. Each shock kind has a return period and a
per-turn emergence budget; large ones are rare by construction.

## 7. Settlement lifecycle as history

Settlements become simulated entities, placed through events, not projected at the end.

- Placement: `CitySiteScorer`/`LandCapacity` become a **candidate generator** (habitability,
  water, defensibility). A `Settle` operator chooses among candidates each turn under an emergence
  budget, weighted by history (rivers, roads, resources, danger, pressure, faith). Output is a pure
  function of `(seed, turn, polity)`.
- Founding sources (operators or rules, each replaceable): colonization (pressure overflow into
  frontier), refugee settling (displaced pool), economic node (river crossing, harbor, mine, route
  toll), military (garrison/fort), religious (monastery, pilgrimage town), political (planned
  capital, deportation), nomad sedentarization, outlaw haven.
- Decline sources: shocks (6), conquest/sack, famine and climate shift, resource exhaustion, trade
  route shift (Palmyra), capital relocation, silting/erosion.
- Tier ladder with hysteresis, role (port, fortress, market, temple, mining, capital), owner
  transfer on conquest, layered names (the lineage naming rule already supports successive
  owners), `Active` split into physical availability vs historical status (`EndedTurn`/`EndCause`).
- Scale guard: per-polity settlement cap and per-turn budget.
- Roads connect settlements; `CityHistoryProject` (91) shrinks to a spatial resolve (positions,
  habitability, projection after map edits).

## 8. Driver (the feedback question)

Today each stage loops turns 1..N itself, so a later stage reads an earlier stage's full time
series. That forbids feedback. Options:

- **A. Turn-major driver.** The driver loops turns; each turn it runs the simulation operators in
  order. Operators become `Step(turn)` instead of a turn loop (mostly mechanical: each already
  keys draws and budgets per turn). Derivation passes (faith myths, spatial resolve, indexes) stay
  as post-passes over the finished log. Enables war -> economy -> war, plague on routes, refugees
  settling mid-span, collapse cascades.
- **B. Keep stage-major, add a two-pass band** (early economy at 31, fiscal at 65). Cheap, but
  every future loop needs another special band.

Recommendation: **A**. The README's "later stage reads earlier time series" rule must be rewritten
for operators (not for post-passes); goldens for history shift once and are re-baselined.

**Read rule (must be chosen; keyed draws alone do not give order-independence).** Today "operator
order never changes outcomes" holds because each stage runs the whole span before the next. With a
turn-major driver, operator B in turn t could read what operator A wrote in the same turn. Either:

- **A1, snapshot/commit (preferred):** every operator reads the end-of-turn t-1 state; writes are
  proposals applied at the end of turn t in a canonical order (entity id, then kind). Operator order
  stays irrelevant, plugins can be added without shifting existing outcomes, and the per-turn event
  cap is applied during the canonical commit, not by whoever ran first. Cost: operators cannot react
  within a turn (a war and its peace treaty need two turns).
- **A2, fixed order within a turn:** state is read live; the order is part of the contract, and a
  plugin inserted into a band changes outcomes on purpose. Simpler operators, but the determinism
  claim becomes "deterministic for a given plugin set and order", and the per-turn event cap favours
  whichever operator runs first.

The doc assumes A1 unless decided otherwise.

## 9. Rule layer and extension

Three tiers, narrowest first:

1. **Kernel (C#, fixed):** driver, `HistoryRng`, budgets, event log, entity store, shock resolver,
   displaced pool, persistence.
2. **Data (YAML, like `Peoples`):** declarations of scalars, polity forms, event/conflict types,
   hazards, roles, resources, policies, with declarative effects over a closed vocabulary:
   `adjust(scalar, entity, delta)`, `found(settlement, where)`, `end(entity, cause)`,
   `split(polity, into)`, `emit(shock)`, `tag(...)`, `transfer(settlement, to)`.
3. **Code and script:** C# plugins registered explicitly (MapEditor.Engine is the reference);
   Jint hooks later, as pure `precondition/weight/propose-effects` functions over a read-only
   snapshot (no `Math.random`/`Date`, step/memory limits, script hash in the save header).

Sketch (illustrative only):

```yaml
event: plague
  trigger:   { every: turn, hazard: plague, per: settlement }
  weight:    density(tier) * routeConnectivity * (1 - hygiene)
  footprint: route-borne(decay: 0.5)
  severity:  0.2 .. 0.8
  effects:   [ shock(plague), adjust(unrest, +0.1), tag(plague) ]
```

Extension requirements (before external plugins are viable):
- explicit registration (`WithHistory`/plugin list), not reflection inside Core only;
- every History type a plugin needs is public and in the PublicAPI baseline;
- a **plugin state bag**: keyed, versioned binary blobs saved with the catalog, surviving
  `Respatialize`; version mismatch = clear error or reset;
- reserved `StageOrder` band and ledger names for plugins;
- layer rule: editor/consumers depend only on public API (ArchitectureTests rank table).

The **economy (P2d) is built outside Core as the first proving plugin**, in
`src/RoguelikeToolkit.World.MapEditor.Engine` (a C# project that already holds `WorldExports`) or a
sample project beside `samples/DesertPlugin`. If it cannot be written against the public API, the
API is incomplete. Jint hooks would extend the existing `RoguelikeToolkit.World.Scripting.Jint`
project (`JintWorldStage`); its sandbox rules are to be audited and reused, not assumed.

## 10. Process catalog

Tier 0 = kernel. Tier 1 = reference content shipped (port of existing + minimum new). Tier 2 =
optional packs / plugins.

| Process | Tier | Affects | Notes |
|---|---|---|---|
| Founding, fork, site-lost | 0/1 | polity, settlement | exists |
| War, conquest, rebellion | 1 | polity, settlement | port; conquest transfers settlements, not only an overlay |
| Polity collapse / split into successors | 1 | polity, culture | new; Gurubashi |
| Succession (dynastic claim, crown wars, regency) | 1/2 | actor, polity | new; needs rulers as actors |
| Trade roads, exchange, route shifts | 1 | route, settlement | port; routes connect settlements |
| Treaty, grudge, khan ultimatum, tribute | 1 | polity | port; vassalage/alliance leagues as treaty kinds |
| Holy site, faith, myth | 1 | site, faith | port; add schism/heresy |
| Villain | 1 | polity | port |
| Settlement founding / abandonment / razing | 0/1 | settlement | new (7) |
| Displaced pool & Settle operator | 0 | all | new (6) |
| Shocks: quake, volcano, tsunami, flood, drought, storm, fire | 1 | settlement, site | from physics hazards |
| Plague / epidemic | 1 | settlement | route-borne; hits density |
| Famine | 1 | polity, settlement | capacity vs population |
| Climate epochs (cold/dry spells) | 2 | global hazard modifier | e.g. 1-3 turn spans |
| Economy: resources, tax, estates, corruption, law | 2 (P2d) | polity, settlement | plugin |
| Resource wars, tariffs, embargoes | 2 | polity, route | needs economy |
| Migration of peoples / nomad invasions | 2 | culture, polity | horde pressure generalised |
| Innovation diffusion (agriculture, iron, writing) | 2 | culture | rides trade routes; modifies capacity/yield |
| Golden age / decline | 2 | polity | derived from scalars |
| Coups, revolutions, civil war | 2 | polity, actor | from unrest/legitimacy |
| Banditry, piracy, mercenaries | 2 | route, settlement | route danger |
| Guilds / orders / charters | 2 | settlement | status, tax privileges |
| Megafauna, monsters, magical disasters | 2 | settlement, site | fantasy plugin |
| Environmental feedback (deforestation, salinization, silting) | 2 | tile modifier | human -> land |

## 11. Persistence

- Schema bump (v4+). Contents: string table, entity stores with lifecycle fields, state scalars,
  event log with participants/cause/importance, displaced pool, scars, plugin state bag.
- Catalog `Load` validates every cross-reference (polity/entity ids in range) and throws
  `InvalidDataException` (fixes the review finding).
- Old v3 saves: derived history (polities, events, settlements...) regenerates from the seed, but
  **`HistoryOverrides` (user renames, pinned tiles) is user-authored and is carried across the
  bump**, never regenerated. Only the derived parts are dropped.
- Pins and renames reference polity ids. Once polities can split or end, an override can point at a
  dead or renumbered entity; see the open question on resolving to a successor.

## 12. Port plan for landed stages

| Stage | Fate |
|---|---|
| CultureSeed, PolityForm | kernel formation step; forms from data |
| HistoryEvents (driver) | becomes the turn-major driver |
| TradeRoad, Exchange | operators over settlements |
| Population | demography ledger feeding polities and settlements |
| War, Conquest, Rebellion | operators; Rebellion becomes a polity.split effect |
| HolySite, Khan, Treaty, Villain | operators/rules; Khan horde check becomes a form tag, not an enum |
| Faith | post-pass over the log (myth slots from events) |
| CityHistoryProject (91) | spatial resolve only |

Order: kernel first with the existing tests as the safety net, then port one stage at a time.

## 13. Paper validation (to complete before implementing)

1. **Economy:** estates, tax, corruption as declared scalars; fiscal effects on pressure; resource
   conflict as war cause tags. Check it needs nothing the data/API layer lacks.
2. **Gurubashi collapse:** overextended empire (legitimacy, unrest) -> civil war -> `polity.split`
   into successor tribes sharing an ancestor culture -> capital razed -> displaced pool -> refugee
   settlements, ruins scar -> a faith myth about the fall.
3. **Crown wars:** ruler actor dies without clear heir -> claimants (actors) back different
   estates/polities -> multi-party war -> ends with one claim winning and a vassal treaty.
4. **Venice:** sack of a city -> displaced pool -> refugee settlement on a defensible lagoon site
   -> merchant-republic form -> trade node.

## 14. Scope guard

The ambition is breadth, not depth: aggregates at 10-year turns, small tiers. Suggested milestones:

- M1: entity lifecycle + string event kinds + turn-major driver + port existing stages (no new
  features; tests identical modulo re-baselined goldens).
- M2: settlement lifecycle + shocks + displaced pool + scars (tier 1 processes).
- M3: extension API + economy plugin in MapEditor.Engine.
- M4: data layer for event types; Jint hooks only if the data layer proves insufficient.
- P3/P4 resume on top of M2 (P4a shrinks, see 7).

## 15. Open questions

- Turn length for shocks: keep 10y, or add a sub-turn "year" tag for flavour.
- Per-settlement population vs per-polity only; tier ladder thresholds.
- Which review findings (holy-site ownership staleness, pinned capitals) are fixed now vs absorbed
  by the new ownership model.
- Event-log cap with importance classes: exact retention rules.
- Whether cultures need their own flood-fill/territory or are derived from settlement ownership.
- How a pin or rename resolves when its polity splits, merges or ends (successor by capital tile?
  by largest share of the pinned tiles? orphaned and surfaced to the user?).
- Driver read rule: decided, A1 (snapshot/commit), see 8. Open detail: the canonical commit order
  for conflicting proposals is decided: entity id, then proposal kind (ties by operator name, then
  proposal sequence). Step-level breakdown of M1–M4: `HISTORY_MILESTONES.md`.
