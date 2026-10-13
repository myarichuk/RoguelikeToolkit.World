using RoguelikeToolkit.World.Core.Biomes;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>Filter for city-site search. Null/empty means "no constraint".</summary>
public sealed class CitySiteFilter
{
    /// <summary>Lowest <c>ElevationInfo.Height</c> a candidate may have (inclusive).</summary>
    public float MinElevation { get; init; } = 0.01f;
    /// <summary>Highest <c>ElevationInfo.Height</c> a candidate may have (inclusive).</summary>
    public float MaxElevation { get; init; } = 0.6f;
    /// <summary>Biomes a candidate may have; null or empty allows any. Ocean and glacier tiles are always excluded.</summary>
    public HashSet<BiomeType>? AllowedBiomes { get; init; }
    /// <summary>Highest effective danger level allowed, after history modifiers and tile overrides.</summary>
    public byte MaxDanger { get; init; } = 3;
    /// <summary>When true, candidates need water on the tile, on a neighbour or within <see cref="FreshWaterRadiusKm"/>. The scorer counts rivers, lakes and sea alike as water.</summary>
    public bool RequireFreshWater { get; init; } = true;
    /// <summary>Great-circle search radius for water, in km on a sphere of <see cref="World.EarthRadiusKm"/>. NaN or zero or less checks only the tile and its neighbours; half the circumference or more accepts anywhere once the world has any water.</summary>
    public double FreshWaterRadiusKm { get; init; } = 600.0;
    /// <summary>Maximum number of sites returned; a negative value is treated as 0.</summary>
    public int TopN { get; init; } = 10;
}
