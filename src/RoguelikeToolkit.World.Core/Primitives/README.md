# Primitives

Allocation-free building blocks every other slice may use: deterministic RNG and noise, vector/coordinate structs, and the persisted type-id hash.

Namespace `RoguelikeToolkit.World.Core.Primitives`, layer rank 0 (see `docs/ARCHITECTURE.md` and `ArchitectureTests`).

## Contents

- **Rng**: Seeded sub-stream generator. Streams are keyed by seed + salt, never by iteration order (the determinism contract).
- **SphereNoise**: Noise sampled on the unit sphere.
- **DetMath**: Deterministic math helpers.
- **Vector2D / Vector3D / GeoCoord**: Value types for planar, spatial and lat/lon coordinates.
- **TileOrdering**: Index orderings (single primitive sort) used by the flow-accumulation and routing passes.
- **StableTypeId (internal)**: Namespace-independent FNV hash of a type, used in persisted store and entity headers.

## Rules

- Namespace equals folder; one top-level type per file (`ArchitectureTests`).
- May depend on slices of equal or lower rank only; new upward edges fail the build.
- Depends on nothing; `ArchitectureTests` fails if a `Primitives` file gains a slice `using`.
- Anything here is on a hot path or feeds the golden hashes: change behaviour only together with a deliberate golden update.
