using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public sealed class CitySiteScore
{
    public int TileIndex { get; init; }
    public GeoCoord Coord { get; init; }
    public double Score { get; init; }
    public BiomeType Biome { get; init; }
    public float Elevation { get; init; }
    public string Reasons { get; init; } = string.Empty;
}
