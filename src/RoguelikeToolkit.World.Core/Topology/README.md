# Topology

The geometry of the planet and of derived maps: icosphere generation, per-size tile grid, hex grids, and map addresses.

Namespace `RoguelikeToolkit.World.Core.Topology`, layer rank 1 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **IcosphereGenerator**: Builds the subdivided icosahedron (tiles = 10*4^size + 2).
- **TileGrid (internal)**: Immutable per-size geometry (centres, tile vectors, CSR adjacency, lat/lon bucket index) with a bounded cache; `WorldDataStore` delegates to it.
- **WorldMap**: Planet-level map view.
- **IHexMap<TTile>, OffsetGrid, CubeCoord**: Flat-top hex grid shapes used by region and local maps.
- **MapAddress, MapTier, PlanetHex, RegionRef, LocalRef**: Addresses of a map at each tier.
- **MapBounds**: Centre, radius and edge ring of a materialised map.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Depends on `Primitives` only (waivers aside).
