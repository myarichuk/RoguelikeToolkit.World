using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

/// <summary>Read-only per-cell view of one materialized tier map; removes the region/local branch from every accessor.</summary>
internal interface ITierSurface
{
    int CellCount { get; }
    float GetElevation(int cell);
    BiomeType GetBiome(int cell);
    bool IsWater(int cell);
    bool IsRiverChannel(int cell);
    byte GetDanger(int cell);
    float GetMoisture(int cell);
    int GetAdjacent(int cell, Span<int> neighbors);
}
