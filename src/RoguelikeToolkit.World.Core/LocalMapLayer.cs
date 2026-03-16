using System;
using RoguelikeToolkit.World.Geometry;
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
