# Tectonics

Plates, boundaries and the stage that partitions the sphere.

Namespace `RoguelikeToolkit.World.Core.Tectonics`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **TectonicPlateGenerationStage**: Stage order 10; writes `TectonicPlateLayer`.
- **IPlatePartitioner, VoronoiPlatePartitioner, NoisyFloodFillPlatePartitioner**: Pluggable plate partitioning.
- **TectonicPlate, PlateInfo, CrustType, PlateBoundaryType**: Per-tile plate data.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
