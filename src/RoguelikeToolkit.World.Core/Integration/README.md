# Integration

The boundary to game-owned state: history, overrides and location bindings, passed per query.

Namespace `RoguelikeToolkit.World.Core.Integration`, layer rank 5 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **QueryOptions**: Carries `History` and `Materialized`; null means pure geography.
- **IHistoricalContext, TileHistoryModifier, HistoricSite**: Game events projected onto tiles and sites.
- **IMaterializationStore, MaterializationStore, TileOverride**: Overrides (replacement, not additive) and `MapAddress` to location-id bindings.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- The library stores no game state.
