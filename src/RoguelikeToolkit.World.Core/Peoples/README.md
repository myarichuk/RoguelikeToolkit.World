# Peoples

Folk and culture data: the "hardware" (folk biology/cosmology) and "software" (learned, driftable culture).

Namespace `RoguelikeToolkit.World.Core.Peoples`, layer rank 2 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **FolkDef**: sapience (`full` people / `instinct` titles-only / `setting`-dependent), embodiment, habitat affinity, lifespan/maturation, growth-`r`, personality priors, compat group. Lives in `Data/Folks/`.
- **CultureDef**: naming-table ref, subsistence archetype, lineage default, value axes (xenophobia / hierarchy-claim / collectivism / honor), faith form, trait-expression offsets, mixed-naming strategy + hypodescent threshold. Lives in `Data/Cultures/`.
- **CompatData**: pairwise offspring rules (`Data/compat.yaml`); unlisted pairs blend.
- **PeoplesStore**: loads and validates the tables (shipped embedded resources plus an optional override dir); `FolkKeys` is the generated folk index.
- **PeoplesDataException, UnknownPeoplesKeyException**: load and lookup failures.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Data only: `NamingTable` is a reference checked non-blank here and resolved late by `Names` (rank 5), never here.
- Key matches filename (case-insensitive); on-disk files override shipped entries with the same key.
