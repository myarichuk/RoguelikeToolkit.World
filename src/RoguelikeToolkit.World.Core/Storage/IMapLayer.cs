using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Storage;

public interface IMapLayer<T> where T : unmanaged
{
    WorldDataStore Store { get; }
    T GetValue(GeoCoord coord);
}
