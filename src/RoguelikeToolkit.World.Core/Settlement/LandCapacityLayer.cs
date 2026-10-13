using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Settlement;

/// <summary>Store layer exposing <see cref="LandCapacityInfo"/> per tile.</summary>
public class LandCapacityLayer : StoreLayer<LandCapacityInfo>
{
    /// <summary>Creates the layer over <paramref name="store"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public LandCapacityLayer(WorldDataStore store) : base(store) { }
}
