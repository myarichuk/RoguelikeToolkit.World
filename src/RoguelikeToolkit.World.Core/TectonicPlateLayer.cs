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
    private readonly WorldDataStore _store;
    private int _seedCount;
    private readonly int _seed;
    private readonly ArenaAllocator _arena;

    public WorldDataStore Store => _store;

    public int SeedCount
    {
        get => _seedCount;
        set => _seedCount = value;
    }

    public TectonicPlateLayer(WorldDataStore store, int seedCount, int seed = 42, ArenaAllocator? arena = null)
    {
        _store = store;
        _seedCount = seedCount;
        _seed = seed;
        _arena = arena ?? new ArenaAllocator();
    }


    public TectonicPlate GetValue(GeoCoord coord)
    {
        int index = _store.GetTileIndex(coord);
        return _store.GetRef<TectonicPlate>(index);
    }

    public void Dispose()
    {
        // Don't dispose WorldDataStore here since it's centrally managed
    }
}
