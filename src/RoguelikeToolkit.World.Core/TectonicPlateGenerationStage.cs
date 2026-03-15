using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace RoguelikeToolkit.World.Core;

[WorldGeneratorStage(10)]
public class TectonicPlateGenerationStage : IWorldGeneratorStage, IDisposable
{
    public int SeedCount { get; set; } = 12;
    public int Seed { get; set; } = 42;

    private readonly ArenaAllocator _arena;

    public TectonicPlateGenerationStage()
    {
        _arena = new ArenaAllocator();
    }

    public TectonicPlateGenerationStage(int seedCount, int seed = 42, ArenaAllocator? arena = null)
    {
        SeedCount = seedCount;
        Seed = seed;
        _arena = arena ?? new ArenaAllocator();
    }

    public void Execute(WorldMap map)
    {
        _arena.Reset();
        var store = map.DataStore;
        var span = store.GetSpan<TectonicPlate>();

        for (int i = 0; i < span.Length; i++)
        {
            span[i] = new TectonicPlate { Id = -1 };
        }

        uint state = (uint)Seed;
        if (state == 0) state = 1;

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

        var seeds = new ArenaList<Vector3D>(_arena, SeedCount);
        var seedElevations = new ArenaList<double>(_arena, SeedCount);
        var seedDriftSpeeds = new ArenaList<double>(_arena, SeedCount);

        for (int i = 0; i < SeedCount; i++)
        {
            double lat = NextDouble() * 180.0 - 90.0;
            double lon = NextDouble() * 360.0 - 180.0;
            seeds.Add(Vector3D.FromGeoCoord(new GeoCoord(lat, lon)));
            seedElevations.Add(NextDouble());
            seedDriftSpeeds.Add(NextDouble());
        }

        ReadOnlySpan<Vector3D> tilePositions = store.GetTileVectors();

        int iterations = 3;

        for (int iter = 0; iter < iterations; iter++)
        {
            for (int i = 0; i < store.TileCount; i++)
            {
                Vector3D pos = tilePositions[i];
                int bestSeed = -1;
                double maxDot = -2.0;

                for (int s = 0; s < SeedCount; s++)
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

            var centroids = new ArenaList<Vector3D>(_arena, SeedCount);
            var centroidCounts = new ArenaList<int>(_arena, SeedCount);
            for (int s = 0; s < SeedCount; s++)
            {
                centroids.Add(new Vector3D(0, 0, 0));
                centroidCounts.Add(0);
            }

            for (int i = 0; i < store.TileCount; i++)
            {
                int s = span[i].Id - 1;
                centroids[s] += tilePositions[i];
                centroidCounts[s]++;
            }

            for (int s = 0; s < SeedCount; s++)
            {
                if (centroidCounts[s] > 0)
                {
                    seeds[s] = (centroids[s] / centroidCounts[s]).Normalize();
                }
            }
        }
    }

    public void Dispose()
    {
        _arena.Dispose();
    }
}
