using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public struct RegionCell
{
    public int CellIndex;
    public uint Seed;
    public float Elevation;
    public BiomeType Biome;
    public float Moisture;
    public bool IsRiver;
    public float WaterDepth;
    /// <summary>Local discharge in cell-count units (1 per cell, routed downhill).</summary>
    public float Flow;
}
