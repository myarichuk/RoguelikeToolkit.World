# Storage

The planet field store: dense, blittable per-tile layers, optionally memory-mapped, plus the file format.

Namespace `RoguelikeToolkit.World.Core.Storage`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **WorldDataStore**: Owns layer allocation, the file header/versioning and persistence; geometry is delegated to `Topology.TileGrid`.
- **StoreLayer<T>, IMapLayer<T>**: A typed dense layer over the store.
- **UserEditLayer, UserEditInfo, UserEditFlags**: Sparse-per-tile user edit overlay.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- `UserEditInfo` references `Biomes`, `Climate` and `Settlement` types: listed upward waivers.
- Layer element types must be `unmanaged` blittable structs; their persisted id is `StableTypeId`, so renaming one changes the id. File format is documented in `docs/STORAGE.md`.
