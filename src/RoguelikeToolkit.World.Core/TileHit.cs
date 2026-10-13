using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core;

/// <summary>One tile hit from a spatial query.</summary>
public struct TileHit
{
    /// <summary>Planet tile index.</summary>
    public int TileIndex;
    /// <summary>Centre of the tile.</summary>
    public GeoCoord Coord;
    /// <summary>Surface height of the tile (<see cref="Elevation.ElevationInfo.Height"/>), or 0 when the world has no elevation layer.</summary>
    public float Elevation;
    /// <summary>Generated biome of the tile, or <see cref="BiomeType.Plains"/> when the world has no biome layer.</summary>
    public BiomeType Biome;
    /// <summary>Great-circle distance in kilometres from the query centre to the tile centre, on a sphere of radius <see cref="World.EarthRadiusKm"/>.</summary>
    public double DistanceKm;
}
