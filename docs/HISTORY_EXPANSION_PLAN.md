# History expansion: battles, logistics, named persons

Status: **in progress** (X0–X6 done; progress in [REVIEW_FIXES_TODO.md](REVIEW_FIXES_TODO.md), section Expansion X). Requested 2026-10-25. Builds on `Proposal.Requires` (batch F of
[REVIEW_FIXES_TODO.md](REVIEW_FIXES_TODO.md), landed in `d7492ac`). Phases are **X0–X9** (§10).

## 0. Facts that shape the design

- **A turn is 10 years** (`HistoryContext.TurnYears`). A battle or siege lives inside one turn: each campaign draws a year
  offset (0..TurnYears−1) and a season and records them. Nothing carries across turns except the persisted state below.
- **War today is a coin flip.**
  - `WarOperator.Propose` uses one `raid` stream.
  - `BattlefieldTile` scans all tiles and returns the lowest-index defender tile next to the attacker: the same tile for a
    pair every time (repetitive) and O(tiles) per war (slow at size 7).
  - `Assault` picks capture / raze / sack from flat constants; `ConquestOperator` uses a 1.5x population ratio.
- **Event budget.** `HistoryCatalog.AppendLinked` enforces `MaxEventsPerTurn` (64) catalog-wide in commit order (entity id
  first). Structural events written by the commit (SettlementFounded, TierChanged, SettlementEnded) share the 64.
  Detail events must get their own quota or they crowd out structure and move goldens. `MaxEvents` (4096) compacts Minor
  events first.
- **Event participants.** `EventParticipant(EntityId, Role)` has no callers. `ValidateReferences` treats every participant
  id as a polity.
- **Ids are predicted by the emitter.** `AddPolity` works only because the emitter predicts the next id and the commit
  rejects anything else. Persons created by several operators in one turn need pending handles (§1.3).
- **`ActorTraits.Roll` is keyed by id.** If person ids came from commit order, adding a plugin would change Core persons'
  traits. Person draws use stable keys (§1.1).
- **Layering.** Peoples is rank 2, History 4, Integration/Names 5 (`ArchitectureTests.Rank`). The Peoples loader routes by
  folder (`PeoplesStore.LoadOne`); new YAML types must be registered in `Internal/Compat/AotYamlObjectFactory.cs`. Core
  targets `net10.0;netstandard2.1`.
- **Schema.** `HistoryCatalog.SchemaVersion = 12`. Load branches on `version >= N`, then `ValidateReferences` and
  `Timeline.Validate`.
- **Editor.** C# exports in `MapEditor.Engine/WorldExports.History.cs`, strides pinned in `WorldExportsLayoutTests`.
  Editor TS `engine/world/history/actors.ts` already uses "Actor" for realm/culture groups, so new TS code says **Person**.
- **Logistics inputs that already exist:**
  - routes of road segments (`HistoryRoad`);
  - `SettlementRole.Post` and `AddSettlement.Why` (`garrison`, `route`);
  - the displaced pool;
  - `ClimateInfo.SeasonalRange`, `RainSeasonality`, `RainPhase`;
  - `HydrologyInfo.IsRiver`, `LandCapacityInfo.Timber`, `BiomeInfo.DangerLevel`.

  There is no defensibility layer yet (M5.2); sieges fall back to tier, height and coast.

## 1. Data model (Core `History/`, one type per file)

### 1.1 `HistoryPerson` (catalog list `Persons`, id = index)

| Field | Type / notes |
|---|---|
| `Id` | int |
| `Kind` | `PersonKind : byte`: Ruler, Heir, Advisor, General, Spymaster, MerchantPrince, RebelLeader, Outlaw, Privateer, Prophet |
| `Folk`, `Culture` | interned string ids |
| `PolityId` | allegiance; −1 for a free outlaw |
| `SeatSettlement` | −1 for none |
| `HouseId`, `Parent` | person id or −1 |
| `Feminine` | bool |
| `BornTurn`, `BornYear` | year is the offset inside the turn |
| `DiedTurn` | −1 = alive |
| `DeathCause` | interned: age, battle, executed, assassinated, plague, hanged, lost_at_sea, outlaws |
| `Traits` | `ActorTraits` (OCEAN) |
| Skills | `Martial`, `Intrigue`, `Stewardship`, `Charisma` (bytes 0..100) |
| `Epithets` | 0..3 interned trait ids from data |
| `Renown` | int |
| `Wealth` | double |
| `Loyalty` | sbyte −100..100, to the ruler |
| `Pledged` | double, merchant war funding consumed by the next campaign |
| `StableKey` | int, see below |

- **`StableKey`** = `HistoryRng.StableHash($"{operator}|{turn}|{originEntity}|{slot}")`, fixed when proposed. Every draw about
  the person uses `sink.Rng(StableKey, purpose)`, never the id; traits are rolled by the proposer and carried in the
  proposal. Ids may shift when plugins add persons; outcomes do not.
- **Skills** = OCEAN + culture offsets + one draw:
  - Martial = 0.4(100−A) + 0.3C + 0.3·roll
  - Intrigue = 0.4O + 0.3(100−A) + 0.3·roll
  - Stewardship = 0.6C + 0.4·roll
  - Charisma = 0.6E + 0.4·roll
- **Epithets** come from `Peoples/Data/Traits/*.yaml` (OCEAN predicates, effect weights): cautious, reckless, cruel, pious,
  greedy, beloved, cunning, craven, ...

### 1.2 Other catalog lists

- **`HistoryRelation { A, B, Kind, FromTurn, ToTurn, Weight }`.** Kinds: parent, spouse, patron, rival, nemesis, mentor,
  betrayer, sworn, sponsor. List sorted by (A, B, Kind, FromTurn); at most 8 open relations per person.
- **`HistoryTenure { Person, Polity, Office, FromTurn, ToTurn }`.** Offices: ruler, chancellor, marshal, spymaster, treasurer,
  high_priest, council. At most one open tenure per (polity, office).
- **`HistoryBattle`.** A record, not an event, so it survives compaction; one War or Siege event points at it.
  - Identity and place: `Id, Turn, Year, Season, Kind` (field / ambush / siege / relief / naval), `Tile, Terrain`,
    `Settlement` (−1 unless a siege).
  - Sides: `Attacker, Defender, GeneralA, GeneralD`; per side strength, loss share, supply, morale, tactic, unit.
  - Result: `Outcome` (AttackerDecisive, AttackerVictory, AttackerPyrrhic, Stalemate, DefenderVictory, DefenderDecisive,
    Averted), `Beats` (interned beat tags), `SiegeMonths, SiegeEnd`, `Parent` (a relief battle points at its siege), `EventId`.
- **`HistoryRouteState`.** Sorted by (RouteA, RouteB). Fields: `Safety, Traffic` (0..1), `Forts, Waystations`,
  `OutlawBand` (person), `Sponsor` (polity), `SabotagedTurn, LastIncidentTurn`. The timeline gains route samples.
- **`HistoryContext` additions:**
  - `HistoryFeatures` flags: Persons, Battles, Sieges, Logistics, Commerce, Outlaws, Intrigue. **Default None until X9.**
  - Caps: `MaxPersons` 4096, `MaxLivingPersonsPerPolity` 6, `MaxAppointmentsPerTurn` 12, `MaxOutlawBands` 16,
    `MaxDetailEventsPerTurn` 32, `MaxSiegeMonths` 18.

### 1.3 Proposals (appended after `SetPluginState = 27`; numeric order = commit order within an entity)

- **`AddPerson` (28)**, applied in a structural pre-pass after `AddPolities`.
  - The emitter gets a pending id from `sink.ProposePerson(...)` (`−(2+n)`, scoped per operator).
  - The commit assigns real ids in canonical order and rewrites same-operator references (`person:{pending}` tags,
    participants, later proposals) through the map.
  - Caps apply; rulers are exempt.
- **`EndPerson` (29)**, conflict per person.
- **`SetTenure` (30)**, conflict per polity·64 + office.
- **`AddRelation` / `EndRelation` (31/32).**
- **`AdjustPerson` (33)**: summed deltas, clamped once, like `AdjustScalar`.
- **`AddBattle` (34)**: the commit appends the record, then the War/Siege event tagged `battle:{index}`, and sets `EventId`.
- **`AdjustRoute` (35)** and **`SetRouteFlag` (36)**.

Outcome-dependent proposals ("capture only if the siege committed") use `Requires`.

### 1.4 Events

- **New `HistoryEventKind` values:** Siege 25, PersonRose 26, PersonDied 27, Intrigue 28 (plot / assassination / bribe /
  sabotage / sponsorship_exposed), CaravanRaided 29, OutlawRose 30, OutlawEnded 31, Smuggling 32, Privateering 33,
  Campaign 34 (winter_quarters / supply_failed / foraging / forced_march), Prophecy 35.
- **War stays the field battle**, gaining `battle:{id}` and `outcome:{o}` tags.
- **Participant roles are prefixed by entity** (`person:commander`, `settlement:besieged`); a bare role means a polity.
  `ValidateReferences` dispatches on the prefix.
- **Detail quota.** Kinds 25+ and Minor kinds count against `MaxDetailEventsPerTurn`, separate from the 64. At most 2
  detail events per polity per operator.

### 1.5 TurnView

- New: `Persons` (living), `PersonsOf`, `OfficeHolder`, `RelationsOf`, `RouteStates`, `BattlesOf`.
- Lazy caches: `LogisticsGraph`, `TerrainClass(tile)`, and `BorderIndex` (polity pair → border tiles, one O(tiles·6) pass).
  `BorderIndex` replaces the `BattlefieldTile` scan; its first entry reproduces today's choice, so legacy mode stays
  byte-identical.

### 1.6 Persistence: schema 13

- Saved after the timeline: persons, relations, tenures, battles, route states, route samples.
- Load validates references, interned ids, `BornTurn ≤ DiedTurn`, one open tenure per office, loss shares in [0, 1] with
  finite strengths, and sorted relations.
- Files older than v13 load with empty lists.

## 2. Peoples data (YAML, culture-keyed, validated)

- **`Tactics/*.yaml`.** `Key`, `Arm` (foot/horse/missile/siege/beast/naval), `TerrainFit` (0.5..1.5), `Counters`
  (0.7..1.3), `Seasons`, `Beats`. Starter set: shieldwall, pike_square, heavy_charge, horse_archers, skirmish, ambush,
  massed_archery, berserk_rush, war_beasts, siege_engines.
- **`Warfare/<culture>.yaml`**, with `default.yaml` as the fallback:

```yaml
Key: orcish_horde
Doctrine: { berserk_rush: 40, horse_archers: 30, ambush: 20, shieldwall: 10 }
Units: [ { Key: wolf_riders, Arm: horse, Weight: 3 }, { Key: boar_tuskers, Arm: beast, Weight: 1 } ]
Mobility: 80, Discipline: 35, SiegeSkill: 25, Forage: 90, ColdTolerance: 60, Levy: 1.4
CampaignSeasons: { spring: 2, summer: 3, autumn: 2, winter: 1 }
Customs: [take_heads, no_quarter, duel_of_champions]   # change model terms, unlock beats
```

- **`Lexicon/<culture>.yaml`** (prose only, never read by the simulation): cries, unit names (sing./pl.), oaths, verbs by
  register, office titles, epithet words, siege words.
- **`Traits/*.yaml`** for epithets.
- **Loader work:** new folders in `PeoplesStore.LoadOne`, cross-checks (doctrine → tactics, arms valid, culture exists),
  AOT factory registration, and a lint over all 12 shipped cultures.

## 3. Battle resolution: `BattleResolver` (pure static, like `ShockResolver`)

Called inside `WarOperator.Propose`. Draws use `sink.Rng(battleKey, "battle:<phase>")` with
`battleKey = StableHash($"{attacker}|{defender}|{turn}|{n}")`, independent of the legacy `raid` stream, so the decision to go
to war is unchanged. `DetMath` only; sums of uniforms stand in for normals.

**Forces**
- Levy: `L = Pop × m_form × Levy_c × (1 + (Collectivism−50)/200)`, with m_form Horde .20, Loose .10, Kingdom .07, MerchantRepublic .04.
- Mercenaries: `+ min(ΣPledged×0.02, 0.5L)`.
- Attacker: `F_a = L_a·reach`, `reach = Exp(−d/R)`; d is the logistics cost from the nearest own settlement (§4) and
  `R = R0(0.5 + Mobility/100)`.
- Defender: `F_d = L_d(0.6 + 0.4·warned)`; warned is a draw < 0.5 + 0.004·(spy Intrigue difference).

**Supply, morale, terrain, season**
- Supply: `σ = clamp(reach·seasonF + (Forage/100)·cap(target)·(1−reach) − 0.2·sabotaged, .25, 1)`.
- Morale: `μ = clamp(.5 + .003·Honor + .1·home + .1·holy + .05·tanh(renown/10) − .3·max(0, pressure−.5) − .2(1−σ), .2, 1.2)`.
- Terrain of the battlefield tile: open/forest/hills/mountain/marsh/desert/snow plus a river-crossing flag.
  Defender bonus δ: forest .10, hills .20, mountain .35, marsh .15, river crossing +.25.
- Season from CampaignSeasons. Winter penalty `w = norm(SeasonalRange)·[winter]·(1−ColdTolerance/100)`; mud from
  RainSeasonality / RainPhase.

**Phases**
1. **Manoeuvre.**
   - Initiative: `I = Mobility/100 + Martial/100 + .3·Intrigue/100 + noise`.
   - Attacker wins: picks ground (ambush ×1.3 with δ halved in forest/hills if the doctrine allows), or bypasses the river.
   - Defender wins: holds the best border tile (full δ), or declines into a siege when `F_d < .6F_a` and a walled settlement
     is in reach.
   - If `σ_a < .35`: outcome Averted plus `Campaign:supply_failed`.
   - The battlefield is drawn from the top-k `BorderIndex` tiles along the path, weighted by the manoeuvre winner's
     TerrainFit. This also fixes the same-tile repetition.
2. **Engagement.**
   - Each side draws a tactic, weighted by Doctrine × TerrainFit × epithet bias.
   - Power: `P = F·μ·σ·Fit·Counter·(1 + .006(Martial−50))·(1−w)`, times `(1+δ)` for the defender.
   - Margin: `m = 2·Log(P_a/P_d) + spread·Log(u/(1−u))`, with spread = 1 + .5·reckless − .3·cautious.
     P(attacker wins) ≈ r²/(1+r²) (Lanchester square).
   - Bands: m > 2 decisive; .6..2 victory; |m| < .6 pyrrhic or stalemate; mirrored for the defender.
3. **Rout and pursuit.**
   - Loser loss: `ℓ_l = clamp(.10 + .08|m|, .05, .6)`. Winner loss: `ℓ_w = clamp(.12/(1+|m|), .02, .3)`.
   - Decisive adds `.15·Mobility_w/100·(1−Discipline_l/200)`, ×1.5 under `no_quarter`.
   - General death: p = .03 + .10·lost + .15·decisive-lost + .08·reckless; under `ransom`, part of that becomes capture.
   - Turning-point beat: a weighted draw over beats whose preconditions hold — flank, feigned_retreat,
     rain_on_bowstrings, river_ford_held, night_attack, stampede, betrayal (advisor Loyalty < −30), champion_duel,
     general_fell.

**What the outcome does** (replaces the flat constants in `Assault`)
- Casualties: KillPopulation on the nearest settlements, capped at 5%.
- Decisive win against a non-capital: siege, or direct capture if the target is unwalled.
- Victory: sack.
- Defender decisive: grudge +2 and deserters (§5).
- Renown, and a nemesis relation after two meetings.
- `ConquestOperator` (behind the flag) overlays only on AttackerDecisive or a stormed capital.

### 3.1 Sieges: `SiegeResolver` (month loop, up to `MaxSiegeMonths`)

- **Setup**
  - Garrison: pop × {village .05, town .08, city .10}; hamlets cannot be besieged.
  - Walls: tier + defensibility (or a height/coast proxy) + .3 for a fort.
  - Food in months: `Fm = 6 + 6[city] + 4[river/coast resupply not blockaded] − 3[autumn] − 3[granary sabotaged]`.
  - The blockade is complete only if the attacker controls the sea lane.
- **Each month**
  - Attacker supply σ_m = σ − .03m (×2 in winter without quarters), plus disease .02/month (×2 marsh or summer).
  - Storm attempt when `SiegeSkill·P_a/(P_d(1+W)) > 1.2` and a draw < .15·SiegeSkill/100; success at r²/(1+r²).
  - Starvation after Fm: defender morale −.15/month, surrender p = .1(m−Fm).
  - Betrayal p = .01 + .03[spymaster Intrigue > 70] + .04·starving + .05·[foreign garrison or disloyal commander].
  - Relief: if the defender's free levy ≥ .5P_a, a relief battle at month ⌈d/speed⌉, recorded with Parent = the siege.
  - The siege lifts when σ_m < .3.
- **Ends and their proposals**
  - stormed → capture, kill .1–.3, or raze under `salt_fields`;
  - surrendered → capture;
  - betrayed → capture plus a relation;
  - relieved / lifted / starved_out → pressure and a grudge;
  - capital stormed → sack plus an `AddWindow` tribute.

## 4. Logistics: a graph over settlements, not tiles

- **`LogisticsGraph`**, built lazily per TurnView (about 150 nodes or fewer).
  - Edges:
    - unsevered routes: segments × (1 + 1.5(1−Safety));
    - river links: both IsRiver, within 8°, angle·0.7;
    - sea lanes for coastal settlements of maritime or merchant polities, within 20°: angle·0.8;
    - overland within 10°: angle × mean terrain factor × 3.
  - Multi-source Dijkstra, `SortedSet<(cost, index)>`.
- **Military use:** reach, σ, foraging, winter quarters (σ < .6 in winter → Campaign:winter_quarters, attrition, campaign
  slips a year). Severed or sabotaged edges, or a third party at war, add cut-supply cost; cut supply is the main reason
  sieges lift.
- **`LogisticsOperator` (order 40): projection of power.**
  - Route safety drifts .3 per turn toward
    `Σ_endpoints formStrength·(1 − dist_capital/R_proj) + .15·Forts + .05·Waystations − .25[outlaws] − .3[crosses belligerents]`.
  - On long, unsafe routes it founds a `Post` settlement (`why:fort` / `why:waystation`) on an owned midpoint tile.
- **`CommerceOperator` (order 82): peaceful flows.**
  - Traffic = complementarity × (.5 + merchant wealth) × Safety. Needs `TradeRoadOperator.SurplusProfiles` moved into
    `HistoryDemography`.
  - Raids: p = (1−Safety)·.3·Traffic → `CaravanRaided`; the merchant loses wealth, the outlaw gains renown.
  - A sponsored band is exposed on a draw against the victim's spymaster → `Intrigue:sponsorship_exposed` + grudge, a
    casus belli.
  - A severed route with merchants at both ends → `Smuggling`.
  - A maritime polity with grudge ≥ 2 and a sea lane commissions a Privateer (`sponsor` relation).
  - Sabotage (`IntrigueOperator`): an enemy route or granary before an expected campaign; it takes effect next turn.

## 5. Persons: operators (built-ins below 100; the plugin band is untouched)

- **`CourtOperator` (38)**
  - **Rulers:** one per living polity. Succession is tied to `PopulationOperator`'s generation tick: the ruler dies of age
    and the heir or a kinsman takes over (`parent` relation).
  - **Heirs:** treaty heirs become persons; marriage gives `spouse`.
  - **Offices by form:** Kingdom 3; MerchantRepublic a council of merchant princes; Horde 1–2 war-chiefs; Loose
    Association elders. A vacancy draws 3 candidates and takes the best OCEAN fit; low-A rulers pick favourites
    (`patron`). Two holders with high E and low A become `rival`s.
  - **Death hazard:** `1 − Exp(−TurnYears·a·Exp(b·age/lifespan))`, lifespan ±25%, plus plague or shock in the seat.
- **War (50):** the marshal leads, else the ruler, else an ad-hoc captain via a pending handle.
- **Rebellion (70):** creates the RebelLeader, who becomes ruler of the new polity. A failed uprising gives a leader who is
  executed or turns outlaw.
- **`OutlawOperator` (74)**
  - Sources: displaced groups unabsorbed for 2+ turns near a route; deserters after a decisive loss; failed rebels;
    ruined merchants.
  - Haven: forest or hills within 6° of a route.
  - Ends: hanged, pardoned (becomes a captain, `patron`), age, or a haven settlement (`Refuge`, `why:outlaw_haven`).
- **`MerchantOperator` (78)**
  - Merchant princes in Town+ Node or port settlements on active routes, at most 2 per settlement.
  - Wealth grows with traffic. Wealth plus ambition buys a council seat. `Pledged` funds mercenaries.
- **`CourtInfluence` helper (flag Intrigue)**
  - Hawk = Σ office weight × (100−A+E)/200.
  - War chance × (.7 + .6·Hawk); treaty acceptance × (.7 + .6·Dove); merchant councils avoid wars that cut their routes.
- **`ProphetOperator` (86, optional):** after plague or famine; a Prophecy raises pressure and can seed a holy-war grudge.

## 6. Flavour text: late render, never touches simulation bytes

- **Where:** a new `Chronicle` slice (rank 5): `IChronicleTextProvider` / `TemplateChronicleTextProvider`, the pattern of
  `Integration/TemplateMythTextProvider`.
- **Inputs:** catalog, event, battle or person, a lexicon, and a host `INameResolver` (honours `Overrides.Renames`).
- **Grammar** in YAML (`Chronicle/Data/Grammar/common.yaml` plus per-culture overlays):

```yaml
battle.intro:
  - { id: bi01, when: [season=winter], w: 2, cd: 8, t: "In the bitter winter of {year}, {att.general} led {att.unit.pl} against {def.realm}." }
  - { id: bi02, when: [beat=ambush], t: "{def.realm|poss} host never saw the {terrain.noun} close around it." }
```

- **Grammar features:** slots from tags, records and the lexicon; modifiers `|cap |pl |a |poss |ord`; predicates;
  nested `{#symbol}`.
- **Composition:** intro + engagement (tactic pair) + turning point + rout + aftermath/custom + optional cry. With 6–10
  variants per pool × 8 terrains × 4 seasons × 10 beats × culture overlays, that is more than 10⁴ combinations per culture.
- **Repetition control (deterministic):**
  - Render the log once in event-id order, keeping a per-symbol LRU of template ids.
  - A template within its `cd` window gets weight 0; if all are cooling, take the least recently used.
  - Draws come from `HistoryRng.Create(seed, "Chronicle", turn, eventId, symbolPath)`.
  - A used-word set per event stops lexicon repeats.
  - Output is a pure function of (log, records, grammar hash); nothing is persisted.

## 7. Editor exports (`WorldExports.History.cs`, strides pinned)

- `HistoryInfo` grows from 11 to 16 ints: persons, relations, tenures, battles, route states.
- **`HistoryPersons()`**, stride 24: `[id, kind, polity, seat, folkStr, cultureStr, feminine, house, parent, bornTurn,
  bornYear, diedTurn, deathCauseStr, renown, wealth×100, loyalty, martial, intrigue, stewardship, charisma, O, C, E, A]`.
  Plus `HistoryPersonEpithets()` and `HistoryPersonKindNames()`.
- **`HistoryRelations()`**, stride 6; **`HistoryTenures()`**, stride 5.
- **`HistoryBattles()`**, stride 24, plus `HistoryBattleText()` (tactics, units, siege end, beats).
- **`HistoryRouteStates()`**, stride 8, plus `HistoryRouteSeries()`.
- **`HistoryChronicle(start, count)`**: rendered prose per event.
- **TS** (`persons.ts`, `battles.ts`, `routes.ts`):
  - person card: name via `NameRequest(seed, personId, folk, culture, feminine)`, epithets, lifespan bar, tenures,
    relations, battles;
  - map overlay at the scrub turn: battle markers by outcome, siege rings, route-safety heat, outlaw havens, raid pins;
  - chronicle pane;
  - `involved()` reads participants by role prefix.

## 8. Budgets

| What | Budget |
|---|---|
| Battles | War's decisions (≤8/turn); sieges and relief battles are free |
| Rulers | structural, unbudgeted |
| Appointments | ≤ `MaxAppointmentsPerTurn` |
| Persons | `MaxPersons`, `MaxLivingPersonsPerPolity` |
| Outlaws, merchants, forts | 4 emergences per operator; outlaws also `MaxOutlawBands` |
| Route state | free |
| Detail events | the separate quota |

About 3k persons expected at 300 turns.

## 9. Determinism checklist

- Draws use `sink.Rng(stableKey, purpose)`; persons key on `StableKey`, never id.
- `DetMath` only.
- Lists sorted by id or `SortedDictionary` with ordinal comparers. Never `HashCode.Combine` or `string.GetHashCode`.
- Caches are keyed by catalog instance, never static or stale across `RegenerateHistory`.
- netstandard2.1: no `Enum.GetValues<T>`, no `CollectionsMarshal`.

## 10. Phases

| Phase | Work | Acceptance |
|---|---|---|
| X0 | Legacy catalog hash fixtures (sizes 3/4 × seeds 1–5) plus a legacy event projection; `BorderIndex` speed-up | bytes identical; size-7 history ≥20% faster. **Done:** profiling showed the border scans were minor; the win came from lazy deposit noise and an exact faster water-radius test (~1.5 s → ~0.8 s) |
| X1 | Kernel: types, proposals 28–36, pending handles, detail quota, participant prefixes, schema 13, TurnView accessors, `HistoryFeatures` | flags off: legacy projection identical (hash re-captured once for the empty sections); round trip; corrupt-reference rejection. **Done** (see §12) |
| X2 | `CourtOperator`, persons, relations, tenures (observer only) | flag on: the existing kinds' projection equals flag off; one ruler per polity per turn; no participation after death; age at death ≤ 1.5× lifespan; traits stable when a plugin adds persons. **Done** (see §12) |
| X3 | Tactics/Warfare/Traits YAML, `BattleResolver`, generals; War/Conquest behind the flag | statistical tests (§11); flag off identical. **Done** (see §12) |
| X4 | `SiegeResolver`, relief battles; forts (pulled forward from X5) and per-culture map stamps | siege invariants; resupply lengthens sieges; cut supply raises the lifted share. **Done** (see §12) |
| X5 | `LogisticsGraph`, `LogisticsOperator`, forts/waystations, winter quarters, route samples | safety in [0,1]; forts raise it; 300 turns at size 7 < 5 s. **Done** (see §12) |
| X6 | Commerce, Outlaw, Merchant, Intrigue; `CourtInfluence` | corpus bands; an exposed sponsorship leads to a war in ≥1/25 worlds. **Done** (see §12) |
| X7 | Lexicon, Chronicle grammar, renderer | catalog bytes unchanged by rendering; repetition metrics; every template resolves for all 12 cultures. **Done** (see §12) |
| X8 | Exports, TS overlay, person panel | layout tests, vitest, a Playwright path |
| X9 | `HistoryFeatures` default All | one deliberate corpus re-baseline; PR listing every shift; milestone entry. **Done** (see §12 and HISTORY_MILESTONES.md) |

## 11. Testing

- **Invariants** over 25 corpus worlds: references resolve; nothing after death; tenures do not overlap; losses in [0,1];
  captures agree with battle records (`Requires`); caps hold; detail never drops structure.
- **Determinism:** identical bytes; save/load round trip; prefix stability (12 turns ⊂ 30); shuffled registration;
  plugin-inserted persons leave Core persons' traits and battles intact.
- **Statistics** (`BattleResolver`, 10k synthetic keys, shared draws):
  - equal forces: attacker 0.38–0.50;
  - 2:1: ≥ .75;
  - river crossing: −10pp;
  - cavalry in open vs forest: ≥ 15pp gap;
  - winter raises attrition;
  - Martial +30: +8pp;
  - monotone in strength.
- **Corpus bands:** decisive 15–35%; sieges 20–40% of wars; 3–7 living persons per polity; 2–10 outlaw bands per 30 turns;
  raids under 30% of route-turns.
- **Chronicle:** the most frequent full sentence under 2%; trigram repeats under 15% in a 20-event window; lint for every
  symbol, slot and predicate; every template reachable.

## 12. As built (deviations and decisions)

- **X0.** The size-7 profile put border scans under 100 ms. The cost was deposit noise and the city-site water radius;
  both are fixed exactly (catalog bytes unchanged). `BorderIndex` landed anyway: it is the battlefield source for X3.
- **Open vocabularies are interned names, not enums.** Offices, relation kinds, death causes, epithets, terrain classes,
  tactics, units, beats and siege ends are strings in proposals, interned at commit. Data can add any of them without
  code. Enums remain only where code dispatches: `PersonKind`, `BattleKind`, `BattleOutcome`, `HistorySeason`,
  `RouteFlag`.
- **Persons reference by pending handle in every person field**, tags (`person:-2`) and participants. Each sink has its
  own handle space. A proposal that names a refused person is refused too; `AddPerson` house/parent fields fall back to
  −1 instead.
- **Detail events never touch the legacy log.**
  - Only kinds 25–35 draw on the detail budget. Legacy Minor kinds stay on the 64 they always shared.
  - Compaction removes detail events first and does not count them in `Compactions`, so flags-on leaves the legacy
    projection intact.
  - Detail drops are counted in `DroppedDetailEvents`.
  - The per-polity cap is `MaxDetailEventsPerPolityOperator` (2).
- **Participant roles are interned by the sink** (`ProposalSink.Participant`). This runs in canonical operator order, so
  ids stay deterministic; interned names in the projection make it id-independent either way.
- **No `EndPerson` conflict key.** A second end of the same person is refused because the person is already dead, which
  also works for pending handles. Pending handles from different operators would otherwise collide on a raw key.
- **Lifecycle the kernel enforces:**
  - an ended person's open tenures close;
  - an ended polity's offices close at the next commit;
  - the dead cannot be appointed or related;
  - a ruler is exempt from the per-polity person cap and from the appointment budget.
- **Route state** is created on first write for a route that has road segments, at `HistoryRouteState.NewRouteSafety`
  (0.5). It is sampled at the end of every recorded turn.
- **Schema 13** appends the kernel after the timeline. A v12 file is byte-identical to a v13 file with an empty kernel
  minus the trailing 32 bytes (tested).
- **X2 court, all tuning in data.**
  - `Peoples/Data/Court/default.yaml` holds:
    - offices per government form, each with its holder kind and judging skill;
    - the Gompertz hazard (`a`, `b`) and the 1.5-lifespan hard cap;
    - ruler death-age and office-age bands, and early-death causes (with a women-only pool);
    - candidates per vacancy, the favouritism and rivalry thresholds;
    - the skill formulas, as linear weights on OCEAN plus a roll and an offset.
  - `Peoples/Data/Traits/epithets.yaml` holds 15 epithets with min/max bounds on traits and skills.
  - `CourtProfile` compiles both and lists any name it cannot place; the shipped data has none.
  - Culture OCEAN offsets apply to rolled traits. Trait bytes are 0–255; the data reads them on a 0–100 scale.
- **Successions observe the legacy tick without bookkeeping.** A reign ends when `(turn − founded)` is a positive
  multiple of `GenerationTurns`, which is exactly when `PopulationOperator` raises `RulerGeneration`. There is no tick
  while a villain rules. A risen villain overthrows the ruler and takes the throne (`villain`, `agenda:` tags). A new
  ruler is born so that they die at the next tick, aged in the data's band.
- **Taking the `ruler` office makes the holder's kind `Ruler`** (an heir or a kinsman on accession).
- **Treaties:**
  - a marriage turns both treaty heirs into persons and betroths them;
  - fostering binds the elder party's heir to the other ruler (`fostered_by`).
- **The people of an ended polity leave the story** with cause `exile`; persons have no allegiance-change proposal
  yet.
- **Loyalty stays 0.** It matters from X6 (intrigue, betrayal), where its model belongs.
- **Corpus (30 turns, size 3):**
  - about 300 persons per world and about 32 living over about 10 polities;
  - about 40% of deaths are old age;
  - favouritism produces many `patron` bonds; rivalry is rare under the default thresholds.
- **NativeAOT:** the smoke test checks the court and epithet tables and a Persons-enabled history; the JIT and native
  checksums match.
- **X3 battles, all tuning in data.**
  - `Tactics/*.yaml` (10), `Warfare/<culture>.yaml` (all 12 shipped cultures plus `default`) and `Battle/default.yaml`,
    which holds every term of the model: levies per form, muster, reach and supply, morale, terrain classes and the
    biome→terrain map, initiative, margin bands, losses, commanders' fates, beats with conditions, epithet and custom
    effects, and the aftermath of each outcome. `WarfareProfile` compiles them and lists what it cannot place; the shipped
    data has nothing unplaced.
  - Terrain classes, beats, customs, units and aftermath actions are names; beat conditions are a small token language
    (`season=`, `terrain=`, `winner=`, `outcome=`, `winner_arm=`, `custom=`, `river`, `declined`, `general_fell`...), so new
    beats and customs need no code. A beat a tactic lists is drawn only for a winner fighting that tactic.
  - `BattleResolver` is internal and pure: data, two sides, candidate fields and four named draw streams (season, manoeuvre,
    engagement, rout), each drawing a fixed count, so one phase never shifts another's draws.
- **The decision to go to war is the legacy one.** War still draws its `raid` stream; with `Battles` on it then fights the
  war instead of rolling `Assault`'s flat chances (tested: the first war turn's pairs equal the flag-off pairs). Every war
  is one `HistoryBattle` narrated by one event (`War`, or `Campaign` when it never reached the field) tagged
  `outcome:`, with the generals as `person:attacker_general` / `person:defender_general` participants.
- **Deviations from §3, found on generated worlds, not synthetic ones.** The first corpus runs had 84% of campaigns
  averted and 82% of fought battles decisive; the synthetic statistics were already in band. Causes and fixes:
  - **Reach.** A size-3 tile is ~8° across, so `ReachDegrees` 12 starved every campaign. It is 40.
  - **Muster is local.** Whole-realm levies made a raid on a large neighbour meet its entire host (30:1). A settlement `d`
    degrees from the field sends `exp(-d/M)` of its levy, `M = MusterDegrees(0.5 + Mobility/100)`, with a floor
    (`MusterFloor` 0.5) a realm always brings. The attacker gathers at its settlement nearest the field and marches from
    there (the march is the reach term).
  - **The attacker's handicaps stacked.** Crowding pressure lowered the morale of exactly the polities that attack, and
    supply counted in strength, power and morale. Pressure no longer touches morale (`MoralePressure` 0, kept as a
    term); supply scales power by `1 − SupplyPower(1 − σ)` with `SupplyPower` 0.3, and hunger costs 0.1 morale.
  - **Refusal.** A side that won the manoeuvre declines a field where its power is below `DeclineBelow` (0.4) of the
    enemy's: a defender lets the raid through (attacker victory, then a sack), an attacker turns back (`Averted`,
    tagged `declined`). Only a side caught without the initiative can be routed from a hopeless field.
  - **Bands.** Logistic noise alone gives |m| > 2 a quarter of the time at even odds, and local force ratios spread
    widely (σ ≈ 1.3 in log space). `DecisiveMargin` is 3.25 (a rout needs about 5:1 effective power, or luck);
    `MartialPower` is 0.008 so 30 martial points are worth the +8pp §11 asks; `MoraleHome` is 0.12.
  - Corpus (12 seeds, 30 turns, size 3): about 26 wars per world; 31% averted (11% supply, 20% declined); 29% of fought
    battles decisive; ambushes about 1 in 20.
- **Generals.** The holder of the form's first `General`-kind office (marshal, war chief, war leader, admiral), else the
  ruler, else none. There is no ad-hoc captain: a pending captain could be refused by the per-polity cap, and the kernel
  refuses a battle naming a refused person, which would lose the war. A fallen general dies (`battle`). A ruler who falls
  leaves the realm without a ruler for the rest of that turn; the court crowns the heir the next turn (tested).
  Under `ransom` a would-be death becomes `general_captured`. Winners gain renown and losers lose some; two generals who
  have met `NemesisMeetings` times become `nemesis`.
- **Aftermath** follows `Battle/default.yaml`: decisive → capture (a capital or an over-cap captor sacks instead;
  `salt_fields` razes), victory → sack, a rout of the attacker → grudge. Each side's dead fall on its settlement nearest
  the field, capped at 5%. `ConquestOperator` with `Battles` on overlays only after an `AttackerDecisive` battle (no
  population ratio). Captures and conquests always follow a decisive battle (tested).
- **Not in X3:** sieges and the defender's retreat into walls (X4), the logistics graph replacing straight-line
  distance (X5), betrayal and mercenaries (X6, they need loyalty and `Pledged`), and the culture lexicon (X7; it is prose
  only and never read by the simulation).
- **NativeAOT:** the smoke test checks the tactic, warfare and battle tables and a Persons + Battles history; the JIT and
  native checksums match.
- **X4 sieges, forts and architecture, all tuning in data.** Requested 2026-10-25 with forts as defence and power projection
  and per-culture map stamps for settlements, forts and ruins.
  - `Siege/default.yaml` (`SiegeDef`): garrison shares per tier, food (base, city, resupply, season), the besiegers' supply
    decay and disease, storming, hunger and surrender, betrayal, relief, tribute and grudges.
  - `Fortification/default.yaml` (`FortificationDef`): 11 fort kinds (walls, garrison, watch), when and where forts are built,
    how they guard roads and defences, and settlement walls per tier.
  - Each way of war names its appetite for forts (`Fortify` 0–100) and the kind it builds (`FortKind`). Dwarves 90
    (`rock_hold`), gnomes 75 (`bastion`), high elves 65, humans 60 (`stone_keep`), dark elves 55, dragonfolk 50 (`aerie`),
    dogfolk 40, catfolk and halflings 25, lizardfolk 20, wood elves 15 (`thorn_ward`), orcs 10 (`stake_fort`).
  - Each culture names its `Architecture` (a style key the editor's content pack resolves; the simulation never reads it).
- **Sieges** (flag `Sieges`) follow a field battle the attacker won (`Besiege: [AttackerDecisive, AttackerVictory]` in the
  battle data), or a raid the defender refused to meet (`BesiegeDeclined`), when the defender's settlement nearest the field
  has walls of at least `SiegeWalls`; otherwise the outcome's own aftermath applies. With the flag off the battle data is
  read exactly as in X3, and Persons + Battles worlds are byte-identical to X3 (four pinned hashes).
  - Walls = (tier + capital + high ground) × (0.4 + 0.8 × Fortify/100) of the holder's people, plus a fort's own walls.
  - `SiegeResolver` is pure and runs a month loop with a fixed four draws a month: supply decay (double in winter), disease
    (double in a marsh camp or summer), a relief army arriving after its march, storms, hunger and surrender, betrayal
    (a cunning enemy spymaster, a starving town). It ends stormed, surrendered, betrayed, relieved, lifted or abandoned.
  - A relief army is the defender's levy less the field's dead and the garrison. It marches from its nearest other
    settlement when it is at least `ReliefShare` (0.35) of the besiegers, and fights through `BattleResolver` with kind
    `Relief`. Both new beats and tactics honour the kind (`Kinds` in a tactic).
  - Records: the siege is a `HistoryBattle` (kind `Siege`, months, end, the besieged settlement, `Parent` = the field
    battle) narrated by a `Siege` event; a relief battle is another (`Parent` = the siege). Sieges are told only by beats
    that ask for one (`kind=Siege` or the new `end=` condition), and battles never by those.
  - Consequences wait on the siege record (`Requires`). A fall takes the place (capture; a razing custom razes; a capital
    is sacked and pays tribute as a non-aggression window). A siege that held leaves the besiegers a grudge. The garrison's
    dead and a stormed town's toll fall unconditionally, so they land before any change of owner.
  - `ConquestOperator` overlays after a decisive field battle or a fallen capital; a relief battle never counts.
  - **Commit change:** deferred proposals now resolve pass after pass in canonical order, so a chain (a capture event that
    waits on a capture that waits on a siege) applies. Single-level dependencies, the only kind before X4, resolve in the
    first pass exactly as before (the legacy and X3 fixtures are unchanged).
- **Forts** (`FortificationOperator`, order 42, flag `Sieges`) are garrison posts (`Role = Post`, `why:fort`).
  - Need = a bordering polity held in grudge + an invasion within `RecentTurns` + an own road no post guards. Chance per
    turn = `FortChance × Fortify/100 × need`.
  - A polity holds at most `ceil(MaxForts × Fortify/100)` posts, and builds none before it holds `MinSettlements` (2)
    other settlements: the first runs gave half of all settlements to forts.
  - The site is own land facing the threat or on the unguarded road, preferring high ground, a river and the road itself,
    `SpacingDegrees` from any settlement.
  - In a field battle the defender's posts add their garrison (`exp(-d/GarrisonDegrees)` of it, standing troops, always
    ready) and their watch (a better chance to be warned). In a siege a fort holds with its own walls and garrison.
  - Road safety: every route drifts toward 0.5 + `FortSafety` per guarding post (at most `SafetyForts`), less
    `SeveredSafety` when severed. The route state records the guarding forts. Outlaws (X6) will read it; X5's
    `LogisticsOperator` should take over this drift and add projection.
- **Corpus (12 seeds, 30 turns, size 3, all three flags):**
  - sieges follow 22% of wars;
  - ends: surrendered 29%, lifted 40%, betrayed 13%, stormed 13%, abandoned 4%;
  - mean siege 10 months;
  - forts per 100 polity-turns: dwarves 1.50, humans 1.08 (the default roster has only these two peoples);
  - guarded routes average 0.62 safety.

  Relief battles are rare in the corpus because most besieged places are the capitals of one-city realms. The synthetic
  tests exercise them.
- **Map editor:** a `fort` feature kind (`Role = Post` draws as a fort, named as a `hold`) and per-culture stamps for
  settlements, forts and ruins. `HistoryPolityKeys` gains a seventh field, the founding culture's `Architecture`.
- **X5 logistics, all tuning in data** (`Logistics/default.yaml`, `LogisticsDef`; flag `Logistics`). Requested 2026-10-25.
  - **`LogisticsGraph`** (built once per turn view and profile): standing settlements are the nodes. Links: the road between
    two capitals (its segments' length × `RoadCost` × (1 + `RoadUnsafe` × (1 − safety)), × `SeveredCost` when severed, less
    `WaystationRelief` per waystation); a river between two river settlements within `RiverDegrees`; a sea lane between two
    coastal settlements within `SeaDegrees`; open country within `OverlandDegrees` at `OverlandCost` × the mean ground factor
    (`TerrainCost` per battle terrain class). Costs are road-degrees.
  - Per mover (cached): a Dijkstra seeded at every own settlement with its `SourceCost` (by tier; a waystation
    `WaystationSource`). Only a seafaring mover sails (`Seafaring ≥ SeaFrom` in its way of war, or a form in `SeaForms`). A link
    touching a third party's land costs `ForeignCost` more, one touching the land of a polity the mover fought within `WarTurns`
    `HostileCost` more; the host's target is exempt (its land is where the host is going). The cost to a tile adds an overland
    last leg from the cheapest node.
  - Seafaring: dogfolk 70, lizardfolk 65, high elves 60, catfolk 55, humans 50, gnomes 40, default and halflings 30, wood and
    dark elves and orcs 20, dragonfolk 15, dwarves 10. Merchant republics sail whatever their people.
- **War with logistics on.**
  - A field's distance is the attacker's network cost to it, against `ReachCost` 140 instead of `ReachDegrees` 40. The fields
    of the corpus lie a median 20° from the attacker's nearest settlement and cost 3.65× that on the network; 140 ≈ 40 × 3.65
    keeps the X3 calibration while roads, rivers, sea lanes, ground and hostile land move individual campaigns. (A first run
    at 60 averted 71% of campaigns.)
  - **Winter quarters:** a winter host whose supply is under `WinterQuartersBelow` (0.6) winters in quarters: it loses
    `WinterQuartersLoss` (0.12) × (1 − ColdTolerance/100) of itself, and fights in the spring of the next year (within the
    turn) with spring supply and no winter penalty. No extra draws. The war or campaign event is tagged `winter_quarters` and
    told by the always-beat `winter_quarters` (condition `wintered`).
  - **Sieges:** the relief army marches on the defender's network (the besieged place left out), so `ReliefDegreesPerMonth`
    reads road-degrees; only a seafaring besieger closes the sea to a coastal town.
- **`LogisticsOperator`** (order 40) owns the route-safety drift with logistics on (`FortificationOperator` keeps building
  forts). Every route drifts (`SafetyDrift`) toward `BaseSafety` + `ProjectionSafety` × hold + `FortSafety` × forts +
  `WaystationSafety` × waystations − `SeveredSafety` (severed) − `BelligerentSafety` (its keepers, or a keeper and a polity it
  crosses, fought within `WarTurns`) − `OutlawSafety` (a band works it, X6), clamped to 0..1. The hold is the mean over the
  route's two keepers of `FormProjection` × max(0, 1 − network cost to the route's middle / `ProjectionCost`).
  - **Waystations** (`SettlementRole.Waystation`, `why:waystation`, a hamlet): a keeper builds one on a route at least
    `WaystationLength` long, under `WaystationBelow` safety and with fewer than one per `WaystationSpacing` degrees, at the
    owned road tile farthest from any settlement (at least `WaystationClearance`). Chance `WaystationChance` × (0.5 +
    Stewardship/100) of the first held office in `WaystationOffices`; at most `MaxWaystations` each.
  - Every route now carries a state (X4 wrote one only once a route had a fort, since an unguarded route's drift was zero), and
    the timeline samples them every turn.
- **Corpus (12 seeds, 30 turns, size 3, Persons + Battles + Sieges + Logistics):** averted 26% (X4 31%), decisive 27% of fought
  (27%), sieges 27% of wars (22%), 38 hosts wintered in quarters; route safety spans 0.11–0.84, guarded routes 0.56 against
  0.49 unguarded; 21 waystations.
- **Performance:** 300 turns at size 7, history only: 3.5 s with X4's flags, 3.9 s with logistics as well (acceptance < 5 s).
- **Not in X5:** traffic and caravans (X6 commerce), outlaws reading safety (X6), sabotage (X6 intrigue), a dedicated waystation
  map stamp (the editor draws one as a village and names its role; X8).
- **X6 commerce, merchants, outlaws and intrigue, all tuning in data** (`Commerce/`, `Outlaws/`, `Intrigue/default.yaml`;
  `CommerceDef`, `OutlawDef`, `IntrigueDef`; flags `Commerce`, `Outlaws`, `Intrigue`, each runs on its own). The court's
  person roll moved to `CourtProfile.RollPerson` (same draws), shared by every operator that creates persons.
  - **`MerchantOperator` (78):** merchant princes in towns and cities on an active road or the coast (≤2 per settlement and
    per polity, `CourtReserve` places left to the court). Wealth grows with the traffic of the busiest road near the seat ×
    (0.5 + Stewardship/100). While the polity nurses a grudge, a merchant pledges `PledgeShare` of its wealth above
    `PledgeFloor`; the next war spends every pledge of both sides on mercenaries (`MercenaryPerWealth` per unit, at most
    `MercenaryCap` of the levy; the event is tagged `mercenaries`). A trading merchant (never a court office holder: merchant
    republics appoint merchant princes with no wealth) under `RuinBelow` is ruined.
  - **`CommerceOperator` (82):** route traffic drifts toward (TrafficBase + TrafficWealth × merchants' wealth) × safety (×
    `SeveredTraffic` when severed); smuggling across severed roads with merchants at both ends; a seafaring polity commissions
    a privateer against a coastal rival it holds a heavy grudge against, and takes prizes from that rival's richest merchant.
  - **`OutlawOperator` (74):** bands rise from stale displaced groups, deserters of a host routed (`DeserterChance`) or beaten
    (`DefeatChance`) last turn, the dispossessed of a settlement captured or razed last turn, and ruined merchants. A band
    takes the nearest unworked road within `RouteDegrees` (a severed road included: the lawless kind) and a haven of
    `HavenTerrains` ground beside it; its leader is a free person (no polity). It raids with (1 − safety) × `RaidRate` ×
    (`TrafficFloor` + traffic); it ends hanged (base + safety + forts), pardoned by an agreeable ruler (the leader lives on,
    `pardoned` relation) or by age. Nameless brigands raid unworked roads at `BrigandShare` with commerce on.
  - **`IntrigueOperator` (75):** loyalty of office holders drifts toward base + patron − rivalries + agreeableness. The
    holder of the first of `PlotOffices` (spymaster, council, seer, elder, chancellor, treasurer) plots against the
    bordering rival its polity holds the heaviest grudge against. **Every covert act has a sponsor and an operative who can be
    caught later** (requested 2026-10-26: sponsors for sabotage and spy operations, as for outlaw bands):
    - `sponsorship` pays the band on a road touching the rival (or, with outlaws on, hires one onto it). The band raids the
      end of the road its sponsor hates most and, with `SponsoredSabotage` a turn, cuts the road on the sponsor's account. The
      sponsor is named when a raid is traced (`SponsorExpose*`) or the hanged leader confesses (`ConfessChance`).
    - `spy_network` plants an agent (`PersonKind.Agent`: serves the sponsor, seated in the rival's capital, `agent_of` the
      plotter; at most `MaxAgents`). Each turn it runs an operation with `AgentOpChance` × (0.5 + Intrigue/100), drawn from
      `AgentOps`: sabotage (the road costs as severed on the logistics network for `Logistics.SabotagedTurns`), a bribe
      (−`BribeLoyalty` to the rival's general), an assassination (`AssassinBase` + `AssassinSpy` × (agent − rival's spy)), a
      theft (`TheftShare` of the rival's richest merchant's wealth, commerce on). Each operation risks capture (`ExposeBase` +
      `ExposeSpy` × (rival's spy − agent's Intrigue)/100); counter-intelligence uncovers an idle agent with `AgentDiscovery` ×
      spy. A caught agent is executed and names its sponsor: `ExposedGrudge`, a casus belli. The capture's grudge and event
      wait on the execution (`Requires`), a killing's event on the victim's death, so a person who dies otherwise that turn
      is never also caught or murdered. An agent whose seat falls to its sponsor or ends is recalled.
    - Intrigue events always carry `actor:` (the sponsor) and, for an operation, `agent:` or `band:` (the operative), plus
      `secret` or `exposed`/`caught`.
  - **The court's sway** (intrigue on): war chance × (`WarBase` + `WarHawk` × hawk), hawk = the court's mean
    (100 − A + E)/200 with the ruler weighted `RulerWeight`; × `MerchantPeace` when the attacker's merchants trade on a road to
    the defender; treaty acceptance × (`TreatyBase` + `TreatyDove` × dove). A grudge of at least `CasusBelliGrudge` against
    a neighbour is a casus belli: the attacker marches on it (the random-neighbour draw is still made) with
    `CasusBelliChance` added. The first runs had `CasusBelliChance` 0.25, which doubled the wars and pushed sieges under 20%
    of wars; 0.1 keeps the X3–X5 bands.
  - **Betrayal:** a commander with loyalty under `BetrayalBelow` (−30, battle data) costs his host `BetrayalPower` (0.25) of
    its power and the battle gets the always-beat `turncoat` (condition `betrayed`). Synthetic: a bought general's side wins
    37% of even fights against 50% loyal.
  - **Kernel changes:** a ruler is now exempt from the world person cap too (with X6, 300 turns at size 7 reach the cap, and a
    polity must keep its throne filled); X6 operators stop creating persons within `PersonReserve` (512) of the cap. A
    sponsor flag on a road with no band is refused (a band ended or refused this turn). `TurnView.PersonsOf` is indexed once
    per turn.
  - **Calibration history:** the first corpus had no intrigue at all (the default worlds have no kingdoms, the only form with
    a spymaster office: hence `PlotOffices`), merchants "ruined" by the court's wealthless councillors, and bands only from
    those; deserters rarely found a road (the median decisive battle lies 33° from one), hence the defeat and dispossessed
    sources and `RouteDegrees` 20; sponsorships landed on bands ending the same turn, hence hiring and confession.
- **Corpus (25 seeds, 30 turns, size 3, all flags):** 3.7 living persons per polity; 4.5 outlaw bands per world; raids 3.4%
  of route-turns; an exposed sponsorship led to a war in 3 of 25 worlds; averted 31%, decisive 32% of fought, sieges 22% of
  wars. With agents (25 worlds): 335 spy networks; operations bribe 190, assassination 99, sabotage 28 (by agents and
  sponsored bands), theft 15; 87 sponsorships (15 exposed); an exposed sponsorship led to a war in 8 of 25 worlds; 3.8 living
  persons per polity; averted 29%, decisive 30%, sieges 21.5%.
- **X7 chronicle** (slice `Chronicle/`, rank 5; read-only over the catalog). Requested 2026-10-25.
  - **Public surface:** `IChronicleTextProvider` (`Render(catalog, eventId)`, `RenderAll(catalog, start, count)`),
    `TemplateChronicleTextProvider`, `ChronicleOptions` (`TurnYears`, `FirstYear`, `Names`, `DataDirectory`) and
    `IChronicleNameResolver` (realm, person and settlement names). Everything else is internal.
  - **Data:** `Chronicle/Data/Grammar/common.yaml` (about 560 rules over 60 symbols) and `Chronicle/Data/Lexicon/common.yaml`,
    plus a grammar and a lexicon overlay for each of the 12 cultures (15–22 rules each: intros, aftermaths, foundings,
    rulers' rises and deaths, grudges, treaties, settlements, conquests, a few siege ends; host words, cries, ruler titles,
    seasons, terrains, songs, goods). An overlay rule with an existing id replaces it, any other joins the pool; `name!`
    replaces a symbol's pool or a lexicon list (overlays replace `cry` and `office.ruler`). Rules are `{ id, when, w, cd, t }`
    read with a lower-case naming convention (`AotYamlObjectFactory.CreateChronicleDeserializer`, `ChronicleRuleDef`
    registered for AOT).
  - **Grammar:** slots `{att.general}`, `{terrain.noun}`...; modifiers `|cap |lower |pl |a |poss |ord |num |the`; nested
    `{#symbol}` and optional `{#symbol?}`; predicates `k=v`, `k=a|b`, `k!=v`, `k`, `!k` over facts (`beat` and `tag` are
    multi-valued). A rule is a candidate only when every slot it names resolves; a rule whose nested symbol fails is backed out
    and the next candidate tried, so templates never render a hole. Every kind has `event.<Kind>`; `event.generic` catches the
    rest. Battles compose intro + engagement (tactic per side) + turning point (beat) + fate (fallen or captured general,
    heads, turncoat) + rout (winner and margin) + aftermath (outcome, winter quarters, mercenaries) + optional cry (the
    winner's culture); sieges compose intro + course (beats, span) + end (six pools) + aftermath. A slot backed by the lexicon
    (`terrain.noun` → `terrain.forest`) can name another culture's list (a cry is the winner's).
  - **Facts** (`ChronicleFactBuilder`, keys declared in `ChronicleVocabulary`): the event's tags and participants, its
    `HistoryBattle` (sides, generals with epithet and office at the time, units, tactics, loss bands, beats, season, terrain,
    siege settlement, months, end, the relief's besieged town), persons (name, pronouns, age, epithet, office, parent, reign),
    settlements, routes (the settlements at the two route tiles), faiths, myths. The X6 vocabulary is read as the operators
    write it: `plot:` with `secret`/`exposed`/`caught`, `actor:`/`target:`, `person:` (the band of a sponsorship, the agent
    of a spy network, else the victim), `agent:`, `band:`, `hired`, `raid`/`confession`, `killed`/`failed`; `commission` vs
    `prize`; two `merchant:` tags on smuggling; `settlement:` on a merchant's rise. It reads only what was true when the event
    happened, so appending to the log never changes how earlier events read.
  - **Repetition control:** the log renders once in event order. `ChronicleMemory` keeps, per symbol, when each rule was last
    drawn, ranked among the candidates actually competing: a rule cools while fewer than its `cd` (default half the
    candidates) of the others were drawn since; if all are cooling the least recently used wins. Words of a lexicon list cool
    the same way, and a word is used once per event. Draws come from `HistoryRng.Create(seed, "Chronicle", turn, eventId,
    symbolPath)`. Catch-all rules carry low weights (0.25 with slots, 0.05 without) so they only fill gaps.
  - **Names:** a host `IChronicleNameResolver`, else internal `DefaultChronicleNames` (shipped name tables, Latin script, keyed
    on the world seed and `StableKey`; a realm is its people's title for its form, all realms named together and never
    sharing a name; a settlement is named in its founder's tongue; `Overrides.Renames` win). A realm said in full is said by its
    short name later in the same event and in the next six events (prose does this, and multiword titles otherwise dominate
    trigram counts).
  - **Caching:** `Render` needs the whole log (each text depends on the ones before), so the provider caches the last
    catalog's texts, keyed by event count, first and last event id, seed and the rename table.
  - **Measured** (`ChronicleTests.Corpus_TextStaysVaried`: seeds 1–6 with the default roster and 7–8 with orcs, wood elves and
    halflings; size 3, 30 turns, all flags including X6): 5473 events, 6928 sentences; the most frequent sentence is 0.30%
    (acceptance < 2%); word-trigram repeats in 20-event windows max 11.5%, mean 2.7% (acceptance < 15%); with one-word host
    names max 8.2%, mean 1.5%. Before the short-name window the max was 18.9% (realm titles), and an end-of-history burst of
    myths reached 18.4% until the myth pool grew. Rendering costs about 240 ms per size-3 world (~680 events), most of it name
    generation.
  - **Tests** (`ChronicleTests`, 32): the shipped data loads clean with an overlay for each of the 12 cultures; the lint
    (`ChronicleLint`: every root, symbol, slot and lexicon list exists, every predicate names a declared fact and an allowed
    value, no rule contradicts itself, every symbol is reachable) is clean for all 13 grammars and catches seeded faults;
    every rule renders through every culture with full facts; a hand-built catalog (`ChronicleSynthetic`: one polity per
    culture, every kind and tag variant including X6's) renders every kind for every culture with no empty text or stray
    brace and draws 91% of all rules; caught agents name their sponsor; determinism; `ToBytes()` unchanged by rendering;
    `Render` by id agrees with `RenderAll` and its slices; host names and renames; dates; modifiers, tidy, predicates and the
    memory.
  - **NativeAOT:** the smoke test renders a full-feature history and mixes the texts into the checksum; JIT and native agree.
  - **Not in X7:** an editor panel for the text (X8), gendered titles (titles are neutral), and other languages.
- **X9: `HistoryFeatures` defaults to `All`.** `None` stays the legacy simulation (byte-identical; the legacy fixtures set it
  explicitly). The re-baseline and every shift are listed in `HISTORY_MILESTONES.md` ("X0–X9"). Rulers being exempt from the
  person cap (X6) matters here: 300-turn default worlds now pass `MaxPersons`.
