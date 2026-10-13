namespace RoguelikeToolkit.World.Core.Hierarchy;

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
