using System;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// Coordinate-addressed view over one dense layer of a <see cref="WorldDataStore"/>.
/// All field layers are the same thing (a blittable struct per tile read through
/// the store's canonical topology); the concrete layer types only name it.
/// The store is owned by the <see cref="WorldMap"/>, never disposed here.
/// </summary>
public class StoreLayer<T> : IMapLayer<T>, IDisposable where T : unmanaged
{
    public WorldDataStore Store { get; }

    public StoreLayer(WorldDataStore store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public T GetValue(GeoCoord coord) => Store.GetRef<T>(Store.GetTileIndex(coord));

    public void Dispose()
    {
        // Nothing to release: the WorldDataStore is centrally managed.
    }
}
