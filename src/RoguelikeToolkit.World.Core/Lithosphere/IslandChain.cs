using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// A thin, elongated run of land tracing a plate boundary through the open ocean:
/// the unnatural "string of land" an oceanic-oceanic collision paints when its
/// uplift apron is only a tile or two wide but is continuous along the whole border.
/// </summary>
public sealed class IslandChain
{
    internal IslandChain(int anchor, int[] tiles, int[] positions, int length)
    {
        Anchor = anchor;
        TileArray = tiles;
        PositionArray = positions;
        Length = length;
    }

    /// <summary>Tile at one end of the chain (the start of the position axis); stable per chain.</summary>
    public int Anchor { get; }

    /// <summary>The land tiles of the chain.</summary>
    public IReadOnlyList<int> Tiles => TileArray;

    /// <summary>Ring distance of each entry of <see cref="Tiles"/> from <see cref="Anchor"/>.</summary>
    public IReadOnlyList<int> Positions => PositionArray;

    /// <summary>Rings from end to end.</summary>
    public int Length { get; }

    internal int[] TileArray { get; }
    internal int[] PositionArray { get; }
}
