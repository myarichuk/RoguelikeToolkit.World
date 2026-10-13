# Settlement

Land capacity and use layers, and city-site scoring.

Namespace `RoguelikeToolkit.World.Core.Settlement`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **LandCapacityStage / LandUseResolveStage**: Stage orders 25 and 99.
- **CitySiteScorer, CitySiteFilter, CitySiteScore, SettlementScoring**: Site suitability queries.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- `CitySiteScorer` (Queries, Features, Integration) and `SettlementScoring` (Sites) reach upward: listed waivers.
