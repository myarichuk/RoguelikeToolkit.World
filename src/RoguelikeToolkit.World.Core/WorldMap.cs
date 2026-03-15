using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core;

public class WorldMap : IDisposable
{
    private readonly int _size;
    private readonly Dictionary<Type, object> _layers = new();

    public WorldDataStore DataStore { get; }

    public WorldMap(int size)
    {
        _size = size;
        DataStore = new WorldDataStore(size);
    }

    public void RegisterLayer<T>(IMapLayer<T> layer) where T : unmanaged
    {
        _layers[typeof(T)] = layer;
        DataStore.RegisterLayer<T>();
    }

    public IMapLayer<T>? GetLayer<T>() where T : unmanaged
    {
        if (_layers.TryGetValue(typeof(T), out var layer))
        {
            return (IMapLayer<T>)layer;
        }
        return null;
    }

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
