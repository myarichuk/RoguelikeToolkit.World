using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Elevation;

/// <summary>Store-backed layer exposing the per-tile <see cref="ElevationInfo"/> field written by <see cref="ElevationGenerationStage"/>.</summary>
public class ElevationLayer : StoreLayer<ElevationInfo>
{
    /// <summary>Creates the layer over <paramref name="store"/>.</summary>
    /// <param name="store">Store the layer lives in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ElevationLayer(WorldDataStore store) : base(store) { }
}
