# Elevation

Elevation and erosion stages.

Namespace `RoguelikeToolkit.World.Core.Elevation`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **ElevationGenerationStage**: Stage order 15.
- **ErosionGenerationStage**: Stage order 17; `partial` with one file per process: `.Aeolian`, `.Glacial`, `.Fluvial`. The passes share the stage's tunables and the fluvial pass is hot, so they are not separate classes.
- **ElevationInfo, ElevationLayer**: Per-tile height.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
