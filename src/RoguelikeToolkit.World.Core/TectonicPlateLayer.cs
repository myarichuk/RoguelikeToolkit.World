using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace RoguelikeToolkit.World.Core;

public struct TectonicPlate
{
    public int Id;
    public double Elevation;
    public double DriftSpeed;
    // Unit drift direction (used for boundary convergence). Kept as components
    // to keep the struct blittable for the data store.
    public double DriftX;
    public double DriftY;
    public double DriftZ;
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
        _arena = arena ?? ArenaDefaults.Create();
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
