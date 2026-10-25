# Peoples

Folk and culture data: the "hardware" (folk biology/cosmology) and "software" (learned, driftable culture), plus the
subsistence profiles and resources a people can live by (M5).

Namespace `RoguelikeToolkit.World.Core.Peoples`, layer rank 2 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **FolkDef**: sapience (`full` people / `instinct` titles-only / `setting`-dependent), embodiment, habitat affinity, lifespan/maturation, growth-`r`, personality priors, compat group, and the subsistence allow-list (empty means any). Lives in `Data/Folks/`.
- **CultureDef**: naming-table ref, architecture style key (resolved by the map editor's content pack), `FormAffinity` (government-form weights, bound to `PolityForm` by `History.FormAffinity`), default subsistence profile key, lineage default, value axes (xenophobia / hierarchy-claim / collectivism / honor), faith form, trait-expression offsets, mixed-naming strategy + hypodescent threshold. Lives in `Data/Cultures/`.
- **SubsistenceDef / SubsistenceExploit**: one way of making a living:
  - whether the people are sedentary;
  - yield weights on the land-capacity channels;
  - terrain weights for defensibility, coast, river and road;
  - the resources it works.

  Lives in `Data/Subsistence/`.
- **ResourceDef / ResourceEra**: an exploitable resource. It is either a deposit (by name), a land yield, or a surface
  work. It carries its era, value and an optional lifetime (deposits only). Lives in `Data/Resources/`.
- **CourtDef / CourtOfficeDef**: how courts work (offices per government form, death hazard, ages, favouritism and
  rivalry thresholds, skill formulas). Lives in `Data/Court/`; the history reads `default`.
- **TraitTableDef / EpithetDef**: epithets and the trait/skill bounds that earn them. Lives in `Data/Traits/`.
- **TacticDef**: one way of fighting (arm, terrain and season fit, counters, beats). Lives in `Data/Tactics/`.
- **WarfareDef / WarfareUnitDef**: a culture's way of war (doctrine weights over tactics, signature units, mobility,
  discipline, siege skill, forage, cold tolerance, levy, campaign seasons, customs). Lives in `Data/Warfare/`, one file per
  culture key plus `default.yaml` for the rest.
- **BattleDef / BattleBeatDef / BattleEpithetDef / BattleCustomDef**: the battle model, every term of it (levies, muster,
  reach and supply, morale, terrain classes, initiative, margin bands, losses, commanders' fates, beats, epithets, customs,
  aftermath). Lives in `Data/Battle/`; the history reads `default`.
- **FortificationDef / FortKindDef**: walls and forts (fort kinds, when and where forts are built, road guarding, garrisons
  and watch in battle, settlement walls per tier). Lives in `Data/Fortification/`; the history reads `default`.
- **SiegeDef**: the siege month loop (garrisons, food, supply and disease, storming, hunger, betrayal, relief, tribute).
  Lives in `Data/Siege/`; the history reads `default`.
- **CompatData**: pairwise offspring rules (`Data/compat.yaml`); unlisted pairs blend.
- **PeoplesStore**: loads and validates the tables (shipped embedded resources plus an optional override dir); `FolkKeys` is the generated folk index. After every file loads, it checks cross-file references:
  - culture → profile;
  - folk → profile;
  - profile → resource;
  - one resource per deposit name;
  - way of war → culture (or `default`), doctrine → tactic, custom → the battle model's customs, fort kind → the
    fortification model's kinds;
  - tactic counters → tactic.

  `ExploitsOf(profile, maxEra)` is the era gate. `PairingProblem(folk, culture)` checks a culture's default profile against its folk's allow-list. That check is per pair, because cultures don't name a folk.
- **PeoplesDataException, UnknownPeoplesKeyException**: load and lookup failures.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Data only: `NamingTable` is a reference checked non-blank here and resolved late by `Names` (rank 5), never here.
- `ResourceDef.Deposit` is a name, not a `DepositType`: Lithosphere is rank 3, so `History.ResourceDeposits` (rank 4) binds
  it and reports names that match no deposit type.
- Key matches filename (case-insensitive); on-disk files override shipped entries with the same key.
