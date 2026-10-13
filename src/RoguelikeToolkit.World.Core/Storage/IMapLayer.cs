using RoguelikeToolkit.World.Core.Primitives;

namespace RoguelikeToolkit.World.Core.Storage;

/// <summary>A typed, coordinate-addressed view over one per-tile layer of a <see cref="WorldDataStore"/>.</summary>
/// <typeparam name="T">The blittable per-tile struct the layer holds.</typeparam>
public interface IMapLayer<T> where T : unmanaged
{
    /// <summary>The store whose memory backs this layer.</summary>
    WorldDataStore Store { get; }
    /// <summary>The layer value of the tile whose centre is nearest to <paramref name="coord"/>.</summary>
    T GetValue(GeoCoord coord);
}
