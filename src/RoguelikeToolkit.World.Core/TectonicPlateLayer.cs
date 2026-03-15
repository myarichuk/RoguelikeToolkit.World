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

public class TectonicPlateLayer : IMapLayer<TectonicPlate>, IDisposable
{
    private readonly HexSphereStore<TectonicPlate> _store;
    private int _seedCount;
    private readonly int _seed;
    private readonly ArenaAllocator _arena;

    public HexSphereStore<TectonicPlate> Store => _store;

    public int SeedCount
    {
        get => _seedCount;
        set => _seedCount = value;
    }

    public TectonicPlateLayer(int size, int seedCount, int seed = 42, ArenaAllocator? arena = null)
    {
        _store = new HexSphereStore<TectonicPlate>(size);
        _seedCount = seedCount;
        _seed = seed;
        _arena = arena ?? new ArenaAllocator();
    }

    public void Generate()
    {
        _arena.Reset();
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

        // Seed random plates using Voronoi seeds
        var seeds = new ArenaList<Vector3D>(_arena, _seedCount);
        var seedElevations = new ArenaList<double>(_arena, _seedCount);
        var seedDriftSpeeds = new ArenaList<double>(_arena, _seedCount);

        for (int i = 0; i < _seedCount; i++)
        {
            double lat = NextDouble() * 180.0 - 90.0;
            double lon = NextDouble() * 360.0 - 180.0;
            seeds.Add(Vector3D.FromGeoCoord(new GeoCoord(lat, lon)));
            seedElevations.Add(NextDouble());
            seedDriftSpeeds.Add(NextDouble());
        }

        // Precompute tile positions
        var tilePositions = new ArenaList<Vector3D>(_arena, _store.TileCount);
        for (int i = 0; i < _store.TileCount; i++)
        {
            tilePositions.Add(Vector3D.FromGeoCoord(_store.GetGeoCoord(i)));
        }

        int iterations = 3; // Lloyd relaxation iterations

        for (int iter = 0; iter < iterations; iter++)
        {
            // Assign tiles to nearest seed
            for (int i = 0; i < _store.TileCount; i++)
            {
                Vector3D pos = tilePositions[i];
                int bestSeed = -1;
                double maxDot = -2.0;

                for (int s = 0; s < _seedCount; s++)
                {
                    double d = Vector3D.Dot(pos, seeds[s]);
                    if (d > maxDot)
                    {
                        maxDot = d;
                        bestSeed = s;
                    }
                }

                span[i] = new TectonicPlate
                {
                    Id = bestSeed + 1,
                    Elevation = seedElevations[bestSeed],
                    DriftSpeed = seedDriftSpeeds[bestSeed]
                };
            }

            // Recalculate centroids
            var centroids = new ArenaList<Vector3D>(_arena, _seedCount);
            var centroidCounts = new ArenaList<int>(_arena, _seedCount);
            for (int s = 0; s < _seedCount; s++)
            {
                centroids.Add(new Vector3D(0, 0, 0));
                centroidCounts.Add(0);
            }

            for (int i = 0; i < _store.TileCount; i++)
            {
                int s = span[i].Id - 1;
                centroids[s] += tilePositions[i];
                centroidCounts[s]++;
            }

            for (int s = 0; s < _seedCount; s++)
            {
                if (centroidCounts[s] > 0)
                {
                    seeds[s] = (centroids[s] / centroidCounts[s]).Normalize();
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
