using RoguelikeToolkit.World.Core.Biomes;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>Filter for city-site search. Null/empty means "no constraint".</summary>
public sealed class CitySiteFilter
{
    public float MinElevation { get; init; } = 0.01f;
    public float MaxElevation { get; init; } = 0.6f;
    public HashSet<BiomeType>? AllowedBiomes { get; init; }
    public byte MaxDanger { get; init; } = 3;
    public bool RequireFreshWater { get; init; } = true;
    public double FreshWaterRadiusKm { get; init; } = 600.0;
    public int TopN { get; init; } = 10;
}
