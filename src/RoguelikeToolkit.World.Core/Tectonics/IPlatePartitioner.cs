using RoguelikeToolkit.World.Core.Primitives;
using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Tectonics;

/// <summary>
/// Strategy for assigning tiles to tectonic plate seeds. The previous
/// implementation was a Lloyd-relaxed spherical Voronoi
/// (<see cref="VoronoiPlatePartitioner"/>): exact, but its great-circle
/// bisectors read as unnaturally straight plate borders. The default is now
/// <see cref="NoisyFloodFillPlatePartitioner"/>, which grows plates at
/// per-plate speeds through a fractally warped space so borders come out tortuous.
/// </summary>
public interface IPlatePartitioner
{
    /// <summary>
    /// Assigns every tile to a seed. Writes a 0-based seed ordinal per tile
    /// into <paramref name="plateIds"/> (length must equal
    /// <see cref="WorldDataStore.TileCount"/>). Must be deterministic: a pure
    /// function of the store topology, seed positions, and
    /// <paramref name="seed"/>.
    /// </summary>
    void Partition(
        WorldDataStore store,
        ReadOnlySpan<Vector3D> positions,
        ReadOnlySpan<Vector3D> seeds,
        Span<int> plateIds,
        int seed);
}
