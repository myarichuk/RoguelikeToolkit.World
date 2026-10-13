using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

internal sealed class RegionSurface(RegionHandle region) : ITierSurface
{
    public int CellCount => region.Cells.Length;
    public float GetElevation(int cell) => region.Cells[cell].Elevation;
    public BiomeType GetBiome(int cell) => region.Cells[cell].Biome;
    public bool IsWater(int cell) => region.Cells[cell].Elevation < 0f;
    public bool IsRiverChannel(int cell) => region.Cells[cell].IsRiver;
    public byte GetDanger(int cell) => 0;
    public float GetMoisture(int cell) => region.Cells[cell].Moisture;
    public int GetAdjacent(int cell, Span<int> neighbors) => region.GetAdjacent(cell, neighbors);
}
