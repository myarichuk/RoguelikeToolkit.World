using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace RoguelikeToolkit.World.Core;

public enum CrustType : byte
{
    Continental = 0,
    Oceanic = 1
}

public enum PlateBoundaryType : byte
{
    None = 0,
    Convergent = 1,
    Divergent = 2,
    Transform = 3
}

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
    // v2 tectonics: crust kind (oceanic subducts under continental) and the
    // per-tile boundary classification derived from neighbor drift.
    public CrustType Crust;
    public PlateBoundaryType Boundary;
    // v3 tectonics: per-tile continentality (plates carry both continents and
    // oceans, like Earth), distance to the nearest plate boundary in neighbor
    // rings, the nearest boundary's type, and the signed orogeny driver
    // (positive = uplift belts, negative = trenches/rifts) already decayed by
    // distance so elevation and deposits read it directly.
    public float Continentality;
    public float BoundaryDistance;
    public PlateBoundaryType NearestBoundary;
    public float Orogeny;
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
