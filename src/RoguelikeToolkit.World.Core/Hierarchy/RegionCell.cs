using RoguelikeToolkit.World.Core.Biomes;

namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>One cell of a region map: coarse terrain, biome and river state at region scale.</summary>
public struct RegionCell
{
    /// <summary>Row-major index of this cell within its <see cref="RegionHandle"/>.</summary>
    public int CellIndex;
    /// <summary>Per-cell seed derived from the region seed and <see cref="CellIndex"/>; deterministic.</summary>
    public uint Seed;
    /// <summary>Surface height in planet elevation units, where 0 is sea level.</summary>
    public float Elevation;
    /// <summary>Coarse biome: ocean below sea level, mountain above 0.55 elevation, otherwise the parent's dominant biome (ocean parents yield plains).</summary>
    public BiomeType Biome;
    /// <summary>Normalized moisture in [0, 1]: the parent mean plus jitter.</summary>
    public float Moisture;
    /// <summary>Whether a threaded river channel crosses this cell.</summary>
    public bool IsRiver;
    /// <summary>Depth of standing or flowing water in height units; 0 when dry.</summary>
    public float WaterDepth;
    /// <summary>Local discharge in cell-count units (1 per cell, routed downhill).</summary>
    public float Flow;
}
