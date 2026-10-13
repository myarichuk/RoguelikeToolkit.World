namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>
/// Discriminated address of any map in the hierarchy. Unused levels use -1.
/// </summary>
public readonly record struct MapAddress(MapTier Tier, int WorldSeed, int WorldTileIndex, int RegionCellIndex, int LocalTileIndex)
{
    /// <summary>Address of a planet tile; the region and local indices are -1.</summary>
    public static MapAddress ForPlanet(int worldSeed, int worldTileIndex)
        => new(MapTier.Planet, worldSeed, worldTileIndex, -1, -1);

    /// <summary>Address of a region map; <paramref name="regionCellIndex"/> may be -1 to name the region as a whole rather than one of its cells.</summary>
    public static MapAddress ForRegion(int worldSeed, int worldTileIndex, int regionCellIndex)
        => new(MapTier.Region, worldSeed, worldTileIndex, regionCellIndex, -1);

    /// <summary>Address of a local map: a tile within a region cell of a planet tile.</summary>
    public static MapAddress ForLocal(int worldSeed, int worldTileIndex, int regionCellIndex, int localTileIndex)
        => new(MapTier.Local, worldSeed, worldTileIndex, regionCellIndex, localTileIndex);

    /// <summary>The planet hex this address lies in, whatever its tier.</summary>
    public PlanetHex ToPlanet() => new(WorldSeed, WorldTileIndex);

    /// <summary>The region cell this address names. Throws <see cref="InvalidOperationException"/> for a planet-tier address; a local address yields its containing region cell.</summary>
    public RegionRef ToRegion() => Tier == MapTier.Planet
        ? throw new InvalidOperationException("Planet address has no region cell.")
        : new RegionRef(WorldSeed, WorldTileIndex, RegionCellIndex);

    /// <summary>The local tile this address names. Throws <see cref="InvalidOperationException"/> unless the tier is <see cref="MapTier.Local"/>.</summary>
    public LocalRef ToLocal() => Tier != MapTier.Local
        ? throw new InvalidOperationException("Only a local address converts to a LocalRef.")
        : new LocalRef(WorldSeed, WorldTileIndex, RegionCellIndex, LocalTileIndex);
}
