using RoguelikeToolkit.World.Core.Biomes;
using RoguelikeToolkit.World.Core.Hierarchy;

namespace RoguelikeToolkit.World.Core.Sites;

internal sealed class LocalSurface(LocalMapHandle local) : ITierSurface
{
    public int CellCount => local.Tiles.Length;
    public float GetElevation(int cell) => local.Tiles[cell].Height;
    public BiomeType GetBiome(int cell) => local.Tiles[cell].Biome;
    public bool IsWater(int cell) => local.Tiles[cell].IsWater;
    public bool IsRiverChannel(int cell) => local.Tiles[cell].WaterDepth >= RegionMaps.LocalRiverMark;
    public byte GetDanger(int cell) => local.Tiles[cell].Danger;
    public float GetMoisture(int cell) => local.Tiles[cell].Precipitation;
    public int GetAdjacent(int cell, Span<int> neighbors) => local.GetAdjacent(cell, neighbors);
}
