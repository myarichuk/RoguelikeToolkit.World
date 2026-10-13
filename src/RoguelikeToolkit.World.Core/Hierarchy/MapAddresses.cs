namespace RoguelikeToolkit.World.Core;

/// <summary>Tier of a map in the planet -&gt; region -&gt; local hierarchy.</summary>
public enum MapTier : byte
{
    Planet = 0,
    Region = 1,
    Local = 2,
}

/// <summary>Address of one planet-scale hex tile.</summary>
public readonly record struct PlanetHex(int WorldSeed, int TileIndex);

/// <summary>Address of one region cell inside a planet hex.</summary>
public readonly record struct RegionRef(int WorldSeed, int WorldTileIndex, int RegionCellIndex);

/// <summary>Address of one local tile inside a region cell.</summary>
public readonly record struct LocalRef(int WorldSeed, int WorldTileIndex, int RegionCellIndex, int LocalTileIndex);

/// <summary>
/// Discriminated address of any map in the hierarchy. Unused levels use -1.
/// </summary>
public readonly record struct MapAddress(MapTier Tier, int WorldSeed, int WorldTileIndex, int RegionCellIndex, int LocalTileIndex)
{
    public static MapAddress ForPlanet(int worldSeed, int worldTileIndex)
        => new(MapTier.Planet, worldSeed, worldTileIndex, -1, -1);

    public static MapAddress ForRegion(int worldSeed, int worldTileIndex, int regionCellIndex)
        => new(MapTier.Region, worldSeed, worldTileIndex, regionCellIndex, -1);

    public static MapAddress ForLocal(int worldSeed, int worldTileIndex, int regionCellIndex, int localTileIndex)
        => new(MapTier.Local, worldSeed, worldTileIndex, regionCellIndex, localTileIndex);

    public PlanetHex ToPlanet() => new(WorldSeed, WorldTileIndex);

    public RegionRef ToRegion() => Tier == MapTier.Planet
        ? throw new InvalidOperationException("Planet address has no region cell.")
        : new RegionRef(WorldSeed, WorldTileIndex, RegionCellIndex);

    public LocalRef ToLocal() => Tier != MapTier.Local
        ? throw new InvalidOperationException("Only a local address converts to a LocalRef.")
        : new LocalRef(WorldSeed, WorldTileIndex, RegionCellIndex, LocalTileIndex);
}

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
