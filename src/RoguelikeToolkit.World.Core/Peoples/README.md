# Peoples

Folk and culture data: the "hardware" (folk biology/cosmology) and "software" (learned, driftable culture), plus the
subsistence profiles and resources a people can live by (M5).

Namespace `RoguelikeToolkit.World.Core.Peoples`, layer rank 2 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **FolkDef**: sapience (`full` people / `instinct` titles-only / `setting`-dependent), embodiment, habitat affinity, lifespan/maturation, growth-`r`, personality priors, compat group, and the subsistence allow-list (empty means any). Lives in `Data/Folks/`.
- **CultureDef**: naming-table ref, `FormAffinity` (government-form weights, bound to `PolityForm` by `History.FormAffinity`), default subsistence profile key, lineage default, value axes (xenophobia / hierarchy-claim / collectivism / honor), faith form, trait-expression offsets, mixed-naming strategy + hypodescent threshold. Lives in `Data/Cultures/`.
- **SubsistenceDef / SubsistenceExploit**: one way of making a living:
  - whether the people are sedentary;
  - yield weights on the land-capacity channels;
  - terrain weights for defensibility, coast, river and road;
  - the resources it works.

  Lives in `Data/Subsistence/`.
- **ResourceDef / ResourceEra**: an exploitable resource. It is either a deposit (by name), a land yield, or a surface
  work. It carries its era, value and an optional lifetime (deposits only). Lives in `Data/Resources/`.
- **CompatData**: pairwise offspring rules (`Data/compat.yaml`); unlisted pairs blend.
- **PeoplesStore**: loads and validates the tables (shipped embedded resources plus an optional override dir); `FolkKeys` is the generated folk index. After every file loads, it checks cross-file references:
  - culture → profile;
  - folk → profile;
  - profile → resource;
  - one resource per deposit name.

  `ExploitsOf(profile, maxEra)` is the era gate. `PairingProblem(folk, culture)` checks a culture's default profile against its folk's allow-list. That check is per pair, because cultures don't name a folk.
- **PeoplesDataException, UnknownPeoplesKeyException**: load and lookup failures.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Data only: `NamingTable` is a reference checked non-blank here and resolved late by `Names` (rank 5), never here.
- `ResourceDef.Deposit` is a name, not a `DepositType`: Lithosphere is rank 3, so `History.ResourceDeposits` (rank 4) binds
  it and reports names that match no deposit type.
- Key matches filename (case-insensitive); on-disk files override shipped entries with the same key.
