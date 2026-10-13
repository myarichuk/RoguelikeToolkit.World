# Hydrology

Flow routing, rivers, lakes, canyons and the mountain-range catalog.

Namespace `RoguelikeToolkit.World.Core.Hydrology`, layer rank 3 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **HydrologyStage**: Stage order 18; `partial`, catalog building lives in `.Catalogs.cs`.
- **Hydrography, LakeSolver (internal)**: Static algorithm sets for routing and lake filling.
- **HydrologyInfo, HydrologyLayer, Lake**: Per-tile data and lake model.
- **CanyonAnalysis, RangeCatalogBuilder**: Post-passes over finished hydrology.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Produces data that `Features` reads and uses its river water-source types: listed upward waivers.
