# History-first design: actors and lineage before names

## Goal

Invert the dependency (history first, names last) and land it in ordered slices: `Actors`/`Lineage` as the simulation building blocks, `Peoples` as folk/culture data, `History` as the pluggable sim, `Names` demoted to a leaf renderer, editor views consuming history only. No folk, polity, or event may depend on a name string to function.

## Success Criteria

- History simulates identically with `IMythTextProvider` and `INameTextProvider` stubbed to empty strings.
- `ArchitectureTests` (namespace == folder, rank rules) pass with `Names` depending on `Actors`/`Peoples`, never the reverse.
- A dwarf clan raised by humans drifts via deltas with zero code change; a 0.5/0.5 ancestry names via the generalized mixed rule with no `half_X` key; incompatible pairs reject from one YAML row.
- Same `(seed, stage set)` → same planet + history; editor selects a polity and shows its cities + a city timeline; marriages/fostering admit adults only and stay abstract.

## Approach

Layering (low → high): `Primitives` → `Actors` + `Lineage` → `Peoples` → `History` → `Names` (leaf) → `Settlement`/`Sites` (adapter) → `Queries`/`Integration` → MapEditor plugins.

Rank-table changes (`ArchitectureTests.Rank`, land in P0 so later steps compile against it):

- `Names` moves from rank 1 to above `Peoples`/`History` (today only `AotYamlObjectFactory` references it, so the move is cheap). `Actors`, `Lineage` = 1 (beside `Entities`); `Peoples` = 2; `History` = 4 (beside `Features`).
- `History` reads `Settlement`/`Hydrology`/`Climate` (rank 3) — intended downward dependency; cities are history's input.
- `Settlement` (rank 3) must never reference `History` (rank 4). `CitySiteScorer` consumes `IHistoricalContext` (rank 5, existing waiver); `History` implements it. This needs exactly one new waiver: `History->Integration` (catalog implements the hook).
- One history surface: `HistoryCatalog` implements `IHistoricalContext` (tile modifiers + historic sites) and extends it via new read interfaces in `Integration` (kept there so `Settlement` gains no new slice edge — already waived); a game-supplied implementation still replaces it. No second source of tile history.

Current seams: `FantasyRaces` + `RaceTables` bind folk to one table; `PersonNameBuilder:130-140` hardcodes two half-bloods; `CultureData` holds naming + `LineageDefault`; culture assignment is `hash2(seed,plate)%n` in editor `features.ts` / `Editor.nameRequestAt`; injectors emit opaque `NameSeed`; `CitySiteScorer` ranks geography only; `IHistoricalContext` never generates history; no owner layer, no `HistoryCatalog`; orders 26–98 free before `LandUse 99`.

Theory map (design vocabulary, non-normative — no success criterion depends on it):

- Folk vs culture: Barth (boundaries persist after subsistence change) → settled nomads keep clan names; Boyd/Richerson (vertical vs horizontal transmission) → drift weights; Berry (assimilation/integration/separation) → conquest vs trade outcomes.
- Lineage: hypodescent/hyperdescent vary by culture → dominant-share rule with culture-set threshold; ethnogenesis/creolization → hybrid naming (superstrate descriptor + substrate noun).
- Mind: Big Five (Costa/McCrae; MBTI rejected) + Mischel situationism → OCEAN traits as decision weights, never gates; culture filters expression. Roles = fit scores (king/advisor/general/champion/thinker); Park marginal-man → half-blood bridge roles.
- Culture axes: Hofstede/Triandis individualism–collectivism; Gelfand tight–loose; Leung/Cohen honor–face–dignity; Tajfel identity + Choi/Bowles parochial altruism → xenophobia and hierarchy-claim as driftable norms. Orcs: high parochialism + honor, mid supremacy (raid-and-scatter); drow-adjacent: high xenophobia + high supremacy + rigid hierarchy (domination).
- Faith: Tylor forms, Durkheim glue, Stark compensators; Long/Leeming templates (earth-diver, emergence, world-parents, craft) + trauma myth from real events.
- Demography: r/K (catfolk litters high-`r`, elves low-`r`) + Malthus push / Boserup intensify-or-move → pressure feeds migration/conquest/rebellion.
- Power: Weber (charismatic/traditional/rational) → kings, councils, liches differ in succession, not just titles.
- Institutions: Boehm feud ledgers (Grudges), Eliade axis mundi (Holy site), steppe ultimatum diplomacy (Khan variant); trade roads as surplus-complementary edges (gravity-lite) driving exchange volume.

Rejected: keeping `Names` as folk registry; alignment pinned to folk (essentialist, coarse — OCEAN priors instead); step derived from shortest lifespan (mayfly problem — fixed turn + per-folk generations instead); shipping tiny LM via LFS (size, WASM/IL2CPP, nondeterminism — provider interface instead); LSM indexes (thousands of events fit sorted vectors); editor reading YAML directly (breaks determinism + adult-safety).

## Steps

Scope tags: **[CORE]** = `RoguelikeToolkit.World.Core` library (and its tests); **[EDITOR]** = MapEditor (`MapEditor.Engine` C# exports, TS engine, worker, UI plugins). Core lands first and is usable without the editor; the editor only consumes Core through `WorldExports`.

| Step | Scope | Summary |
|---|---|---|
| P0 | CORE | `Actors`/`Peoples` slices, rank table, schemas |
| P1 | CORE | Drift deltas + generalized mixed naming |
| P2a–c | CORE | `History` runtime, operators, faith |
| P3 | CORE | `Names` demoted to leaf renderer |
| P4 | CORE + EDITOR | Settlement adapter (core), then exports + history panel plugin (editor) |

### P0 [CORE] — `Actors/` + `Peoples/` slices (new building blocks)

- New `src/.../Actors/` (rank 1–2, beside `Entities`): `ActorId`, `Actor` (5 OCEAN bytes rolled once from `Rng.Create(seed,actorId)` vs prior, house id, adult flag from folk maturation age, faith id), `Ancestry[{folk,share}]` (top 2–3 define implied status), `Compatibility` matrix + `blend|dominant|sterile` rule. Adult gate enforced here. No geography, no names.
- New `src/.../Peoples/` (data only): `FolkDef` (`sapience full|instinct|setting`, `embodiment`, habitat affinity, lifespan/maturation, `r`, personality prior as low/balanced/high weights, compat group) + `CultureDef` (`namingTable` ref to existing `CultureData` key, subsistence archetype, lineage default, values: xenophobia/hierarchy-claim/collectivism/honor, faith form, expression offsets, mixed-naming + hypodescent threshold). YAML under `Names/Data` migrated: one human + one non-human folk first; demons `instinct` (titles only), devils `full`, elementals flag-per-setting.
- Update `ArchitectureTests.Rank` per the rank-table changes above (new slices + `Names` move), plus exactly one new waiver (`History->Integration`).
- Prerequisite: land or park the uncommitted `Names/Data` YAML and `NameQualityTests` edits before migrating schemas.
- AOT/WASM: register `FolkDef`/`CultureDef` in `AotYamlObjectFactory` and verify trimming (`DynamicDependency`) for plugin discovery.
- Change `NameDataStore` validation for new schemas; deprecate `FantasyRaces.All` toward generated folk index (keep shims, no break).
- Validate: schema tests load two sample folks; ancestry 0.5/0.5 resolves implied status; incompatible pair rejects; determinism test same `(seed,actorId)` → same traits.

### P1 [CORE] — Drift + lineage naming rule

- Add `Integration/CultureDelta.cs`: `{turn,target,trait,from,to,cause}` (subsistence/language/values/faith shifts; e.g. pastoral→agrarian on settle). Ethnos = folk + currentCulture + deltas, resolved at query like `TileHistoryModifier`.
- Compat: `half_elf`/`half_orc` requests keep producing identical output through shims (golden tests) — removal only with a major bump (3.0.0 just shipped).
- Change `PersonNameBuilder`: replace `HalfElf`/`HalfOrc` branches with `RenderMixed(dominant, second)` (given from dominant, family/clan from second, existing clash-avoidance). Delete hardcoded branches after compat lands.
- Validate: settled-nomad scenario keeps clan names one generation then blends place-nouns; conquest city yields superstrate/substrate hybrid.

### P2a [CORE] — `History/` runtime, `PolityForm`, ownership spike

- Spike first: ownership-map shape (managed vs store field), with a perf measurement on a full-size planet.
- New `src/.../History/` (rank 4, beside `Features`): `HistoryCatalog` managed on `World` (polities, settlements + tile-ownership map, roads, actors roster refs, events append-only, faiths, institutions, deltas); implements `IHistoricalContext`. `IHistoryPlugin : IWorldGeneratorStage` with `Precondition/Apply`; `HistoryContext` (fixed turn default 10y configurable; per-turn budget N decisions / M emergences / K events in deterministic polity order).
- RNG scheme: every draw uses `Rng.Create(seed, hash(stage, turn, polityId|actorId, purpose))` — no shared stream, so operator order never changes outcomes. No floating-point accumulation across unordered iteration.
- Change `Generation/StageOrder.cs`: add `CultureSeed 26, PolityForm 27, HistoryEvents 28–90, CityHistoryProject 91`. `PolityForm` seeds kingdom / merchant-republic / loose-association / horde from capacity + water + deposits.
- `World.Recompute` policy (pinned): default `Recompute` re-runs orders 15–25 plus 99, skips history stages 26–91, keeps the old `HistoryCatalog` readable, and sets a `Stale` flag. A full resim is explicit via a separate call (e.g. `RegenerateHistory`, lazy or eager). A map edit forks the timeline: the event log stays append-only, `forkedAt` advances only when tile ownership actually changed (so null edits do not flicker the editor fork marker), and only the spatial projection is re-resolved (polity territory, city habitability, drowned/landlocked sites) — causes and dates are never rewritten. A user-override input layer beside `UserEditInfo` (rename polity, pin border; no stage writes it) survives both recompute and fork and is re-applied after territory re-resolution. Determinism: same seed, config, edits, and override layer produce the same catalog bytes; no-edit recompute is a no-op including history.
- Persistence: `HistoryCatalog` round-trips through `Storage` with a schema version; event log has a configurable cap (compaction beyond it).
- Perf budget: full history over the default span within a stated bound (set after the spike; must also hold under WASM).
- Validate: text providers stubbed → identical event log; same `(seed, stage set)` twice → byte-identical catalog; round-trip test.

### P2b [CORE] — Conflict operators (landed)

- Each a plugin: War, Conquest (culture overlay), Rebellion (distance + oppression + tightness), TradeRoad builder (surplus-complementary river/coast/low-danger edges; cut in war → pressure both sides), Exchange (road-volume diffusion: loanwords, openness drift).
- People/time: logistic growth `r` vs `LandCapacity` K → pressure; generations-per-folk in turns (human ~3, dwarf ~8, elf ~30); succession and trait drift tick in turns.
- Validate: budget test (noisy war starves nothing); no operator reads another's RNG draws; perf budget holds.
- Landed as: War (50), Conquest overlay (60), Rebellion (70), TradeRoad builder (30; cut in war → pressure both sides), Exchange (80), plus Population (32) for people/time — all looping turns 1..`TurnCount`-1 behind the driver (28). Annual-`r` compounded per turn vs K (biome-surplus fallback); generations human 3 / dwarf 8 via lifespan power law; successions per generation; trait drift via Exchange openness (±20). Per-operator per-turn ledgers (decisions: war/conquest; emergences: roads/rebellion; exchange: none). Catalog schema v2; battlefields project with danger +2. Tests: starved-war isolation, determinism, round-trip.

### P2c [CORE] — Treaty, faith, optionals

- Treaty (marriage/foster as abstract adult-only alliance contract → non-aggression window + blended heir + claim hook; refusal logs grudge weight).
- Optionals: Grudge ledger (non-decaying casus belli until repaid; decay in turns), Holy site (aura income + desecration tripwire → coalition), Villain (long-life actor, agenda, no succession, capped budget share), Khan ultimatum (submit-and-pay vs resist-stronger-siege).
- Faith sub-model: form per culture (animism/shamanism/ancestor/poly/heno/mono) + 2–7 powers with domains from pressures + myth slots; add `Integration/IMythTextProvider` (Core deterministic templates; game-side LLM optional, tags as contract). Extend `HistoricSiteKind` only if needed (`HolySite`, `TreatyStone`).
- Validate: villain cap test; adult-gate rejection test; stubbed-text determinism for faith.
- Landed as: HolySite (44), Khan (46), Treaty (48), Villain (76), Faith (84), each with its own per-turn ledger; grudges never decay by time (only repaid by treaty, tribute or conquest overlay); War reads grudges and trips desecration on holy tiles; villains need a 200y+ folk, freeze the polity's succession count at the rise turn and are capped by `MaxVillains` (default 2); `IMythTextProvider` + `TemplateMythTextProvider` in `Integration`. Catalog schema v3. Tests: `HistoryFaithTests`.

### P3 [CORE] — Demote `Names` to leaf renderer

- Change `NameGenerator`/`RaceTables`: folk→initial-culture→table resolution; `Names` gains `RenderPerson/RenderPlace/RenderMythSlots` taking `{actorId|polityId, tags}`; `PlaceContext` gains optional `cultureTags` (kind/context logic unchanged). `CultureData` stays the morpheme payload, owned by `CultureDef.namingTable`.
- Validate: all existing name tests pass unmodified; mixed-name test covers 0.75/0.25 and 0.5/0.5.

### P4 — Settlement adapter [CORE] + editor history plugin [EDITOR]

#### P4a [CORE] — Settlement adapter and exports

- Change `CitySiteScorer`/`SettlementInjector` via history adapter only (capital/polity bonus, road-access habitability, pressure danger) — no weight retuning in this plan.
- Change `MapEditor.Engine/WorldExports` (C#, the Core↔editor boundary): expose `polityAt/citiesOf/timelineOf/actorsOf` + naming requests carrying ids+tags rendered late via `NamePlaces`. Recompute path emits fork marker.
- Scripting: decide whether `JintWorldStage` scripts may read history layers (default: out of scope, documented).
- Validate: export layout tests (`WorldExportsLayoutTests`), WASM export smoke, reroll names stays stable.

#### P4b [EDITOR] — MapEditor plugins (TS/UI; consume exports only, never YAML or Core internals)

- Change `src/engine/world/protocol.ts` and `workers/world.worker.ts`: batch shapes for the new history queries.
- Change `features.ts` / `Editor.nameRequestAt`: drop plate-hash culture styles (hash stays as fallback); send ids+tags.
- New history panel plugin: select polity (territory overlay), list its cities, city timeline; fork-marker display after a map edit; user overrides (rename polity, pin border) via the Core override layer.
- Validate: Vitest for engine/protocol changes; Playwright E2E (`e2e/*.cjs`) generate → select polity → cities + timeline render.

## Next steps (agreed, unscoped — scope/effects to be pinned before implementation)

> Under review: `docs/HISTORY_GENERIC_MODEL.md` (draft) proposes a generic entity/event/shock model, settlement lifecycle and extension API that would supersede the fixed-stage assumptions below and in P3/P4. Not yet agreed.

- P2b+ point-wonder builder: temple/palace/granary/harbor as point sites with small modifiers + tags (same hook as battlefield sites). Triggers: faith form/holy status, war victories, treaty signings, famous actors. Famous = leaders, politicians, military officers, identified by role-fit scores + event participation. Wonders carry mechanical trait shifts that fade over time (e.g. a warlike monument pushes warlike up, then decays). Open: decay rate/shape, per-turn budget.
- P2b+ imperial-road upgrade: raise exchange chance + small endpoint habitability on existing routes. Open: upgrade cost/trigger, stacking with base roads.
- P2c+ treaty kinds: cooperation, tribute, others on top of the base P2c treaty (non-aggression/alliance). All kinds mechanical: culture propagation via travelers/trade plus cultural trait shifts (same fading-shift machinery as wonders). Open: effect + event/tag contract per kind.
- Deferred: wall-style linear infrastructure — needs a border-crossing step in war targeting (walled borders lower the chance) plus maintenance/gates; flavor-only until then.

## Validation Plan

- Pre-edit readbacks: pipeline cache/ordering, scorer history/materialized paths, worker batch shape, `extractFeatures` tiers.
- Stub-text determinism runs; schema + adult-gate + budget-cap unit tests per step.
- Highest risk: ownership-map shape — spike (first task of P2a) before operators.
- Rank-table and AOT checks run in CI from P0 onward.

## Risks / Open Questions

- Managed ownership vs store field if spatial queries go hot — start managed, measure.
- Per-folk lifespan/maturation and villain share need game tuning at execution.
- Event-log growth vs cap/compaction policy; WASM time budget for full history.
- Whether scripts (Jint) get history read access.
