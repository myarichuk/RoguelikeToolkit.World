using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Climate;

/// <summary>Store-backed layer exposing the per-tile <see cref="ClimateInfo"/> field written by <see cref="ClimateStage"/>.</summary>
public class ClimateLayer : StoreLayer<ClimateInfo>
{
    /// <summary>Creates the layer over <paramref name="store"/>.</summary>
    /// <param name="store">Store the layer lives in.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is <see langword="null"/>.</exception>
    public ClimateLayer(WorldDataStore store) : base(store) { }
}
