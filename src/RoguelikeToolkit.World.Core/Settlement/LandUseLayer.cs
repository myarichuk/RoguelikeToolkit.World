using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Settlement;

public class LandUseLayer : StoreLayer<LandUseInfo>
{
    public LandUseLayer(WorldDataStore store) : base(store) { }
}
