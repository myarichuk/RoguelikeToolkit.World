# Lithosphere

Geology: crustal provinces, mineral deposits and island chains.

Namespace `RoguelikeToolkit.World.Core.Lithosphere`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **DepositCatalogBuilder, DepositCatalog, Deposit**: Deposits seeded after the build from finished tectonics, climate and hydrology.
- **Geology, CrustalProvince**: Province model.
- **IslandChainStage, IslandChain**: Detection of volcanic island chains.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
