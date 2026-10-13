namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Pure hex -&gt; seed derivations, one independent stream per tier.
/// Distinct per-tier salts keep planet/region/local streams uncorrelated even
/// for the same tile index. All functions are pure in their inputs and
/// independent of iteration order (backed by <see cref="Rng"/>).
/// </summary>
public static class MapSeeds
{
    // ASCII "MAP1".."MAP4": distinct per tier, unrelated to RegionMaps salts.
    public const int RegionSalt = 0x4D415031;
    public const int RegionCellSalt = 0x4D415032;
    public const int LocalSalt = 0x4D415033;
    public const int LocalTileSalt = 0x4D415034;

    // Each tier is its own stream keyed by (worldSeed, tier salt); the address
    // components are folded in one Derive step at a time. Every step passes
    // through the SplitMix finalizer, so (tile, cell) pairs cannot cancel each
    // other the way a linear a*K1 + b*K2 pre-mix can (which also collided
    // (worldSeed ^ salt) across tiers of different worlds).

    /// <summary>Seed of the region map rooted at a planet hex.</summary>
    public static uint DeriveRegionSeed(int worldSeed, int worldTileIndex)
        => Rng.Create(worldSeed, RegionSalt).Derive(worldTileIndex).NextUInt();

    /// <summary>Seed of one region cell (parent stream for a local map).</summary>
    public static uint DeriveRegionCellSeed(int worldSeed, int worldTileIndex, int regionCellIndex)
        => Rng.Create(worldSeed, RegionCellSalt).Derive(worldTileIndex).Derive(regionCellIndex).NextUInt();

    /// <summary>Seed of the local map rooted at a region cell.</summary>
    public static uint DeriveLocalSeed(int worldSeed, int worldTileIndex, int regionCellIndex)
        => Rng.Create(worldSeed, LocalSalt).Derive(worldTileIndex).Derive(regionCellIndex).NextUInt();

    /// <summary>Seed of one local tile (leaf detail stream).</summary>
    public static uint DeriveLocalTileSeed(int worldSeed, int worldTileIndex, int regionCellIndex, int localTileIndex)
        => Rng.Create(worldSeed, LocalTileSalt).Derive(worldTileIndex).Derive(regionCellIndex).Derive(localTileIndex).NextUInt();
}
