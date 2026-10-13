namespace RoguelikeToolkit.World.Core;

/// <summary>Address of one planet-scale hex tile.</summary>
public readonly record struct PlanetHex(int WorldSeed, int TileIndex);
