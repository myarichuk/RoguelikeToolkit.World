# Queries

Nearest-feature queries without scanning the map.

Namespace `RoguelikeToolkit.World.Core.Queries`, layer rank 5 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **SpatialIndex**: Bucketed planet-tier index with exact early stop; lower-tier addresses resolve to their parent planet tile.
- **FeatureKind**: What can be searched for.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
