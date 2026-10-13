using System;
using System.Collections.Generic;

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
