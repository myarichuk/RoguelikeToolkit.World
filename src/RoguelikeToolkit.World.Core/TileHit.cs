using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core;

/// <summary>One tile hit from a spatial query.</summary>
public struct TileHit
{
    public int TileIndex;
    public GeoCoord Coord;
    public float Elevation;
    public BiomeType Biome;
    public double DistanceKm;
}
