# Features

Sparse feature catalogs (rivers, water bodies, ranges) and the per-tile aggregate.

Namespace `RoguelikeToolkit.World.Core.Features`, layer rank 4 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **RiverCatalog, WaterBodyCatalog, RangeCatalog**: Managed catalogs with tile-id lists.
- **TileFeatures, TileFeatureInfo**: Aggregated view of what is on a tile.
- **GeoWkt**: WKT export.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- `TileFeatures` is the per-hex aggregator and joins `Sites` and `Integration`: listed upward waivers.
- Field layers (dense) live in `Storage`; features (sparse) live here. That split is load-bearing.
