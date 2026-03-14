using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace RoguelikeToolkit.World.Core;

public struct TectonicPlate
{
    public int Id;
    public double Elevation;
    public double DriftSpeed;
}

public class TectonicPlateOverlay : IMapOverlay<TectonicPlate>, IDisposable
{
    private readonly HexSphereStore<TectonicPlate> _store;
    private readonly int _seedCount;
    private readonly int _seed;
    private readonly ArenaAllocator _arena;

    public HexSphereStore<TectonicPlate> Store => _store;

    public TectonicPlateOverlay(int size, int seedCount, int seed = 42, ArenaAllocator? arena = null)
    {
        _store = new HexSphereStore<TectonicPlate>(size);
        _seedCount = seedCount;
        _seed = seed;
        _arena = arena ?? new ArenaAllocator();
    }

    public void Generate()
    {
        _arena.Reset();
        var tilesToProcess = new ArenaList<int>(_arena, _store.TileCount);

        var span = _store.GetSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = new TectonicPlate { Id = -1 };
        }

        // Use a simple seeded pseudo-random approach without allocating Random class
        uint state = (uint)_seed;
        if (state == 0) state = 1; // xorshift requires non-zero seed

        uint NextRandom(uint max)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state % max;
        }

        double NextDouble()
        {
            return (double)NextRandom(1000000) / 1000000.0;
        }

        // Seed random plates
        for (int i = 0; i < _seedCount; i++)
        {
            int tileIndex = (int)NextRandom((uint)_store.TileCount);
            if (span[tileIndex].Id == -1)
            {
                span[tileIndex] = new TectonicPlate
                {
                    Id = i + 1,
                    Elevation = NextDouble(),
                    DriftSpeed = NextDouble()
                };
                tilesToProcess.Add(tileIndex);
            }
        }

        // Flood fill using ArenaAllocator for zero GC
        Span<int> neighbors = stackalloc int[6];
        int head = 0;

        while (head < tilesToProcess.Length)
        {
            int currentTile = tilesToProcess[head++];
            var currentPlate = span[currentTile];

            int neighborCount = _store.GetAdjacent(currentTile, neighbors);

            for (int i = 0; i < neighborCount; i++)
            {
                int neighborIndex = neighbors[i];
                if (span[neighborIndex].Id == -1)
                {
                    span[neighborIndex] = currentPlate; // copy plate info
                    tilesToProcess.Add(neighborIndex);
                }
            }
        }
    }

    public TectonicPlate GetValue(GeoCoord coord)
    {
        int index = _store.GetTileIndex(coord);
        return _store[index];
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
