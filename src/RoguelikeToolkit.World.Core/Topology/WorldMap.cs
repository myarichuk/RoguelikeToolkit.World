using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Topology;

/// <summary>A planet's tile store plus the typed layer views registered on it. Disposing it disposes the layers and the store.</summary>
public class WorldMap : IDisposable
{
    private readonly int _size;
    private readonly Dictionary<Type, object> _layers = new();

    /// <summary>The backing store holding every layer's per-tile data.</summary>
    public WorldDataStore DataStore { get; }

    /// <summary>Creates the map and an unallocated store of the given size (see <see cref="WorldDataStore(int, string)"/> for <paramref name="filePath"/>); the caller registers layers and then allocates <see cref="DataStore"/>.</summary>
    public WorldMap(int size, string? filePath = null)
    {
        _size = size;
        DataStore = new WorldDataStore(size, filePath);
    }

    /// <summary>Registers <typeparamref name="T"/> in the store and keeps <paramref name="layer"/> as its view, replacing any earlier view of the same type. Must happen before the store is allocated; throws <see cref="InvalidOperationException"/> afterwards.</summary>
    public void RegisterLayer<T>(IMapLayer<T> layer) where T : unmanaged
    {
        // Store first: it throws after Allocate(), and the map must not keep a
        // layer the store does not have.
        DataStore.RegisterLayer<T>();
        _layers[typeof(T)] = layer;
    }

    /// <summary>The view registered for <typeparamref name="T"/> through <see cref="RegisterLayer{T}"/>, or null if there is none (a layer registered directly on <see cref="DataStore"/> has no view here).</summary>
    public IMapLayer<T>? GetLayer<T>() where T : unmanaged
    {
        if (_layers.TryGetValue(typeof(T), out var layer))
        {
            return (IMapLayer<T>)layer;
        }
        return null;
    }

    /// <summary>Disposes every registered layer view, forgets them, then disposes <see cref="DataStore"/>; spans taken from the store become invalid.</summary>
    public void Dispose()
    {
        foreach (var layerObj in _layers.Values)
        {
            if (layerObj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _layers.Clear();
        DataStore.Dispose();
    }
}
