# Generation

The stage pipeline: stage contracts, attribute-driven discovery, ordering and plugin loading.

Namespace `RoguelikeToolkit.World.Core.Generation`, layer rank 2 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **IWorldGeneratorStage, WorldGeneratorStageAttribute**: A stage and its order/Reads/Writes declaration.
- **StageOrder**: Every built-in stage order in one place (10 tectonics, 15 elevation, 16 climate, 17 erosion, 18 hydrology, 20 biomes, 25 land capacity, 99 land use).
- **WorldGenerationPipeline**: Discovers, validates and runs stages.
- **StageContractValidator (internal)**: Rejects blind second writers of a layer.
- **PluginLoadContext (internal)**: Isolated load context for out-of-tree stages (see `docs/PLUGINS.md`).

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Discovery has AOT `DynamicDependency` hooks into stage types of higher-ranked slices; these are listed waivers in `ArchitectureTests`.
