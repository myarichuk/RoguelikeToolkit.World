namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>Address of one local tile inside a region cell.</summary>
public readonly record struct LocalRef(int WorldSeed, int WorldTileIndex, int RegionCellIndex, int LocalTileIndex);
