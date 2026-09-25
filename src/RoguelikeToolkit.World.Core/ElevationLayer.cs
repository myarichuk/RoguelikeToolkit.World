using System;

namespace RoguelikeToolkit.World.Core;

public class ElevationLayer : IMapLayer<ElevationInfo>, IDisposable
{
    private readonly WorldDataStore _store;

    public WorldDataStore Store => _store;

    public ElevationLayer(WorldDataStore store)
    {
        _store = store;
    }

    public ElevationInfo GetValue(GeoCoord coord)
    {
        int index = _store.GetTileIndex(coord);
        return _store.GetRef<ElevationInfo>(index);
    }

    public void Dispose()
    {
        // Don't dispose WorldDataStore here since it's centrally managed
    }
}
