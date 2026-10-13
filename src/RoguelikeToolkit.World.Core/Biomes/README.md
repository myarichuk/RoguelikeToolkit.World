# Biomes

Biome classification from elevation, climate and hydrology.

Namespace `RoguelikeToolkit.World.Core.Biomes`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **BiomeGenerationStage**: Stage order 20.
- **BiomeClassifier**: Pure classification rules and thresholds.
- **BiomeInfo, BiomeLayer, BiomeType, LandCover**: Per-tile output.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
