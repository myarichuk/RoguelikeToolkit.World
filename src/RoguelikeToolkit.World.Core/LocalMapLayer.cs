using System;
using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core;

public class LocalMapLayer : IMapLayer<LocalMapInfo>, IDisposable
{
    private readonly HexSphereStore<LocalMapInfo> _store;
    private readonly int _seed;
    private readonly TectonicPlateLayer _tectonicLayer; // Example of a dependency to derive biomes

    public HexSphereStore<LocalMapInfo> Store => _store;

    public LocalMapLayer(int size, int seed, TectonicPlateLayer tectonicLayer)
    {
        _store = new HexSphereStore<LocalMapInfo>(size);
        _seed = seed;
        _tectonicLayer = tectonicLayer;
    }

    public void Generate()
    {
        var span = _store.GetSpan();
        var tectonicSpan = _tectonicLayer.Store.GetSpan();

        uint state = (uint)_seed;
        if (state == 0) state = 1;

        uint NextRandom()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        for (int i = 0; i < span.Length; i++)
        {
            var plate = tectonicSpan[i];

            // Simple heuristic to derive biome and danger from elevation (0..1)
            BiomeType biome = BiomeType.Ocean;
            byte danger = 1;

            if (plate.Elevation < 0.3)
            {
                biome = BiomeType.Ocean;
                danger = (byte)(NextRandom() % 3 + 1); // 1-3
            }
            else if (plate.Elevation > 0.8)
            {
                biome = BiomeType.Mountain;
                danger = (byte)(NextRandom() % 5 + 5); // 5-9
            }
            else if (plate.Elevation < 0.4)
            {
                biome = BiomeType.Swamp;
                danger = (byte)(NextRandom() % 4 + 3); // 3-6
            }
            else if (plate.Elevation > 0.6)
            {
                biome = (NextRandom() % 2 == 0) ? BiomeType.Forest : BiomeType.Plains;
                danger = (byte)(NextRandom() % 3 + 2); // 2-4
            }
            else
            {
                biome = (NextRandom() % 2 == 0) ? BiomeType.Desert : BiomeType.Jungle;
                danger = (byte)(NextRandom() % 4 + 4); // 4-7
            }

            span[i] = new LocalMapInfo
            {
                Seed = NextRandom(),
                Biome = biome,
                DangerLevel = danger
            };
        }
    }

    public LocalMapInfo GetValue(GeoCoord coord)
    {
        int index = _store.GetTileIndex(coord);
        return _store[index];
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
