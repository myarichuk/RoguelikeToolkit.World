using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Hydrology;

/// <summary>Store-backed layer exposing the per-tile <see cref="HydrologyInfo"/> field written by <see cref="HydrologyStage"/>.</summary>
public class HydrologyLayer : StoreLayer<HydrologyInfo>
{
    /// <summary>Creates the layer over <paramref name="store"/>.</summary>
    /// <param name="store">Store the layer lives in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public HydrologyLayer(WorldDataStore store) : base(store) { }
}
