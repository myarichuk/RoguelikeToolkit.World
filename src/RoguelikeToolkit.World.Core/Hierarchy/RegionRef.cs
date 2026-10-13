namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>Address of one region cell inside a planet hex.</summary>
public readonly record struct RegionRef(int WorldSeed, int WorldTileIndex, int RegionCellIndex);
