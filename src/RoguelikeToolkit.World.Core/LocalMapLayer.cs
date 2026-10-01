using System;
using SharpArena.Allocators;

namespace RoguelikeToolkit.World.Core;

public class LocalMapLayer : IMapLayer<LocalMapInfo>, IDisposable
{
    private readonly WorldDataStore _store;
    private readonly int _seed;
    // No per-layer state: biome derivation reads the store directly.
    // The tectonic-layer constructor argument is accepted for source compatibility only.

    public WorldDataStore Store => _store;

    public LocalMapLayer(WorldDataStore store, int seed, TectonicPlateLayer tectonicLayer)
    {
        _store = store;
        _seed = seed;
        _ = tectonicLayer;
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
