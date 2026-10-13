using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>Store layer exposing <see cref="LandUseInfo"/> per tile.</summary>
public class LandUseLayer : StoreLayer<LandUseInfo>
{
    /// <summary>Creates the layer over <paramref name="store"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public LandUseLayer(WorldDataStore store) : base(store) { }
}
