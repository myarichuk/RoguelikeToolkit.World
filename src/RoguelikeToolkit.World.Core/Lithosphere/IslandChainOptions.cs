using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Tuning for <see cref="IslandChainDetector"/>.</summary>
public sealed class IslandChainOptions
{
    /// <summary>
    /// A land tile is "thin" when no land tile with coast depth &gt;= this lies within
    /// <see cref="DeepReach"/> rings. Depth 1 is a tile touching water, so 4 means "at
    /// least three tiles from any shore": a ribbon is therefore at most ~6 tiles wide.
    /// </summary>
    public int DeepDepth { get; set; } = 4;

    /// <summary>Rings searched for an interior (deep) tile before a land tile still counts as thin.</summary>
    public int DeepReach { get; set; } = 2;

    /// <summary>Shortest thin run, in rings end to end, that counts as a chain; shorter ones are ordinary islets.</summary>
    public int MinLength { get; set; } = 6;

    /// <summary>
    /// A chain tile must be oceanic crust this many rings or fewer from a plate boundary. Natural thin land
    /// (barrier spits, the coastal strip of a continent) fails it and is never touched.
    /// </summary>
    public int BoundaryRings { get; set; } = 2;

    /// <summary>Bead (surviving island) length range along a broken chain, in rings.</summary>
    public int MinBead { get; set; } = 2;

    /// <summary>Longest surviving bead, in rings.</summary>
    public int MaxBead { get; set; } = 5;

    /// <summary>Strait width range between beads, in rings.</summary>
    public int MinGap { get; set; } = 1;

    /// <summary>Widest strait between beads, in rings.</summary>
    public int MaxGap { get; set; } = 3;

    /// <summary>
    /// Grids with fewer tiles than this are left alone (default: size 5 and up). On a coarse grid a tile is
    /// 400+ km across, so a ribbon one or two tiles wide is already the smallest land feature the grid can
    /// express and cutting it only deletes land (size 3 loses a tenth of it, polar tundra included).
    /// </summary>
    public int MinTileCount { get; set; } = 8000;

    /// <summary>Height a drowned strait tile is pushed down to, below sea level.</summary>
    public double GapDepth { get; set; } = 0.06;
}
