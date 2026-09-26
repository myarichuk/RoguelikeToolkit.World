using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>Lazy hierarchical detail: one world hex expands to a region grid.</summary>
public sealed class RegionHandle
{
    public int WorldTileIndex { get; }
    public uint Seed { get; }
    public int Size { get; }
    public RegionCell[] Cells { get; }

    internal RegionHandle(int worldTileIndex, uint seed, int size, RegionCell[] cells)
    {
        WorldTileIndex = worldTileIndex;
        Seed = seed;
        Size = size;
        Cells = cells;
    }

    public LocalMapHandle GetLocal(int cellIndex, int localSize = 16)
    {
        if ((uint)cellIndex >= (uint)Cells.Length) throw new IndexOutOfRangeException();
        return RegionMaps.DeriveLocal(WorldTileIndex, cellIndex, Cells[cellIndex].Seed, localSize);
    }
}

public struct RegionCell
{
    public int CellIndex;
    public uint Seed;
    public float Elevation;
    public BiomeType Biome;
    public float Moisture;
}

/// <summary>Leaf detail: one region cell expands to a local tile grid.</summary>
public sealed class LocalMapHandle
{
    public int WorldTileIndex { get; }
    public int RegionCellIndex { get; }
    public uint Seed { get; }
    public int Size { get; }
    public LocalTile[] Tiles { get; }

    internal LocalMapHandle(int worldTileIndex, int regionCellIndex, uint seed, int size, LocalTile[] tiles)
    {
        WorldTileIndex = worldTileIndex;
        RegionCellIndex = regionCellIndex;
        Seed = seed;
        Size = size;
        Tiles = tiles;
    }
}

public struct LocalTile
{
    public int Index;
    public float Height;
    public BiomeType Biome;
    public byte Danger;
    public bool IsWater;
}

/// <summary>
/// Deterministic lazy derivation for region/local maps. Pure functions of
/// (worldSeed, tileIndex, cellIndex); no storage, reproducible on demand.
/// </summary>
public static class RegionMaps
{
    public const int DefaultRegionSize = 8;
    public const int DefaultLocalSize = 16;

    public static RegionHandle DeriveRegion(int worldSeed, int worldTileIndex, float baseElevation, BiomeType baseBiome, float baseMoisture, int size = DefaultRegionSize)
    {
        int count = size * size;
        var cells = new RegionCell[count];
        for (int c = 0; c < count; c++)
        {
            // Independent sub-stream per (tile, cell): order-independent.
            var r = Rng.Create(worldSeed ^ 0x5245474e, worldTileIndex * 7919 + c);
            float jitter = (float)(r.NextDouble() * 2.0 - 1.0);
            uint seed = Rng.DeriveTileSeed(worldSeed, worldTileIndex * 131 + c);
            cells[c] = new RegionCell
            {
                CellIndex = c,
                Seed = seed,
                Elevation = baseElevation + jitter * 0.08f,
                Biome = baseBiome,
                Moisture = Math.Clamp(baseMoisture + (float)(r.NextDouble() - 0.5) * 0.1f, 0f, 1f)
            };
        }
        return new RegionHandle(worldTileIndex, Rng.DeriveTileSeed(worldSeed, worldTileIndex), size, cells);
    }

    public static LocalMapHandle DeriveLocal(int worldTileIndex, int regionCellIndex, uint regionCellSeed, int size = DefaultLocalSize)
    {
        int count = size * size;
        var tiles = new LocalTile[count];
        var r0 = new Rng(regionCellSeed);
        // Base height/mix drawn once per region cell so tiles correlate locally.
        double baseH = r0.NextDouble() * 2.0 - 1.0;
        for (int t = 0; t < count; t++)
        {
            var r = new Rng(regionCellSeed + (uint)t * 2654435761u);
            float h = (float)(baseH * 0.7 + (r.NextDouble() * 2.0 - 1.0) * 0.3);
            bool water = h < 0f;
            tiles[t] = new LocalTile
            {
                Index = t,
                Height = h,
                Biome = water ? BiomeType.Ocean : (h > 0.5f ? BiomeType.Mountain : BiomeType.Plains),
                Danger = (byte)r.NextUInt(3),
                IsWater = water
            };
        }
        return new LocalMapHandle(worldTileIndex, regionCellIndex, regionCellSeed, size, tiles);
    }
}
