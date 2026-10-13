# Entities

A sparse entity store keyed to tiles/cells: columns, adjacency and population grids.

Namespace `RoguelikeToolkit.World.Core.Entities`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **EntityStore**: `partial`: lifecycle (`EntityStore.cs`), snapshot and delta formats (`.Persistence.cs`), typed columns (`.Columns.cs`).
- **EntityId, CellIndex, ICellOf<T>**: Handles and cell addressing.
- **IAdjacency, IPopulationPolicy, PopulationGrid**: Neighbourhood and placement policy.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Persistence uses `StableTypeId` for column type ids (snapshot version 4, delta version 2).
