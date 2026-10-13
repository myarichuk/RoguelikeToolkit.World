using RoguelikeToolkit.World.Core.Biomes;

namespace RoguelikeToolkit.World.Core.Hierarchy;

/// <summary>One tile of a local map: terrain, climate and biome sampled at local scale.</summary>
public struct LocalTile
{
    /// <summary>Row-major index of this tile within its <see cref="LocalMapHandle"/>.</summary>
    public int Index;
    /// <summary>Surface height in the same units as the planet elevation layer, where 0 is sea level.</summary>
    public float Height;
    /// <summary>Biome classified from this tile's height, temperature and precipitation.</summary>
    public BiomeType Biome;
    /// <summary>Danger level from the biome classifier; raised to at least 1 on threaded river tiles.</summary>
    public byte Danger;
    /// <summary>Whether the tile is below sea level (<see cref="Height"/> &lt; 0). Carved river reaches are flagged through <see cref="WaterDepth"/>, not this field.</summary>
    public bool IsWater;
    /// <summary>Normalized temperature in [0, 1]: the parent mean, cooled with height above sea level, plus jitter.</summary>
    public float Temperature;
    /// <summary>Normalized precipitation in [0, 1]: the parent mean plus jitter.</summary>
    public float Precipitation;
    /// <summary>Depth of standing or flowing water in height units; 0 when dry. Values at or above <see cref="RegionMaps.LocalRiverMark"/> mark a river reach.</summary>
    public float WaterDepth;
    /// <summary>Dominant biome of the parent context this tile was derived under; the same for every tile of a map.</summary>
    public BiomeType ParentBiome;
}
