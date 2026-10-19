namespace RoguelikeToolkit.World.Core.History;

/// <summary>One tile changing hands: from <see cref="Turn"/> on, <see cref="Tile"/> belongs to <see cref="Owner"/> (-1 unclaimed).</summary>
public readonly record struct TerritoryChange(int Turn, int Tile, int Owner);
