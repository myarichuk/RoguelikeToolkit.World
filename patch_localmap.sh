#!/bin/bash
cat << 'INNER_EOF' > src/RoguelikeToolkit.World.Core/LocalMapLayer.cs
using System;
using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core;

public class LocalMapLayer : IMapLayer<LocalMapInfo>, IDisposable
{
    private readonly WorldDataStore _store;
    private readonly int _seed;
    private readonly TectonicPlateLayer _tectonicLayer;

    public WorldDataStore Store => _store;

    public LocalMapLayer(WorldDataStore store, int seed, TectonicPlateLayer tectonicLayer)
    {
        _store = store;
        _seed = seed;
        _tectonicLayer = tectonicLayer;
    }

    public void Generate()
    {
        var span = _store.GetSpan<LocalMapInfo>();

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
            span[i] = new LocalMapInfo
            {
                Seed = NextRandom(),
                Biome = BiomeType.Ocean,
                DangerLevel = 0
            };
        }
    }

    public LocalMapInfo GetValue(GeoCoord coord)
    {
        int index = _store.GetTileIndex(coord);
        return _store.GetRef<LocalMapInfo>(index);
    }

    public void Dispose()
    {
        // Don't dispose WorldDataStore here since it's centrally managed
    }
}
INNER_EOF
