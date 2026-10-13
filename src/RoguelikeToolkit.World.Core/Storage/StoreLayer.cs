using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Topology;

namespace RoguelikeToolkit.World.Core.Storage;

/// <summary>
/// Coordinate-addressed view over one dense layer of a <see cref="WorldDataStore"/>.
/// All field layers are the same thing (a blittable struct per tile read through
/// the store's canonical topology); the concrete layer types only name it.
/// The store is owned by the <see cref="WorldMap"/>, never disposed here.
/// </summary>
public class StoreLayer<T> : IMapLayer<T>, IDisposable where T : unmanaged
{
    /// <summary>The store this layer reads; it is not owned by the layer.</summary>
    public WorldDataStore Store { get; }

    /// <summary>Wraps the layer of type <typeparamref name="T"/> in <paramref name="store"/>. The layer must still be registered with the store (and the store allocated) before <see cref="GetValue"/> is used. Throws <see cref="ArgumentNullException"/> for a null store.</summary>
    public StoreLayer(WorldDataStore store)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>A copy of the value of the tile nearest to <paramref name="coord"/>, so writing to it does not change the store. Throws <see cref="ArgumentOutOfRangeException"/> for a non-finite coordinate, <see cref="InvalidOperationException"/> if the store is not allocated and <see cref="ArgumentException"/> if <typeparamref name="T"/> is not registered.</summary>
    public T GetValue(GeoCoord coord) => Store.GetRef<T>(Store.GetTileIndex(coord));

    /// <summary>Does nothing: the layer owns no resources, and the store is released by its owner (<see cref="WorldMap"/>).</summary>
    public void Dispose()
    {
        // Nothing to release: the WorldDataStore is centrally managed.
    }
}
