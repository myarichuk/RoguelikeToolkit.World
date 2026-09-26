using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Dense per-tile climate field. Temperature is ~[0..1] (0 polar, 1 equatorial
/// before lapse-rate correction), Precipitation is ~[0..1], wind is a unit-ish
/// 3D vector stored as components to stay blittable.
/// </summary>
public struct ClimateInfo
{
    public float Temperature;
    public float Precipitation;
    public float WindX;
    public float WindY;
    public float WindZ;
}

public class ClimateLayer : IMapLayer<ClimateInfo>, IDisposable
{
    private readonly WorldDataStore _store;

    public WorldDataStore Store => _store;

    public ClimateLayer(WorldDataStore store)
    {
        _store = store;
    }

    public ClimateInfo GetValue(GeoCoord coord)
    {
        int index = _store.GetTileIndex(coord);
        return _store.GetRef<ClimateInfo>(index);
    }

    public void Dispose()
    {
    }
}
