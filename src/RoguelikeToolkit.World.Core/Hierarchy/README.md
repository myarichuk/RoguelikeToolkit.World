# Hierarchy

Storageless derivation of region and local maps from the planet: pure functions of (seed, address).

Namespace `RoguelikeToolkit.World.Core.Hierarchy`, layer rank 4 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **RegionMaps**: Entry point for deriving region and local grids.
- **RegionHandle, LocalMapHandle, GridMapHandle<TTile>**: Materialised maps that know their parent and address.
- **ParentContext, TerrainOrientation**: Inherited scalars plus gradient/strike/flow direction.
- **ChildGridBuilder (internal)**: The derivation itself.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Parents know nothing about children. `RegionHandle` and `LocalMapHandle` run the site injector pipeline, so they reach into `Sites`: listed waivers.
