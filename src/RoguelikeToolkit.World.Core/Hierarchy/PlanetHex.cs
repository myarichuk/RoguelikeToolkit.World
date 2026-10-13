namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>Address of one planet-scale hex tile.</summary>
public readonly record struct PlanetHex(int WorldSeed, int TileIndex);
