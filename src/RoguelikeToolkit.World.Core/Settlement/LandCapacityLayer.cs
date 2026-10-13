using RoguelikeToolkit.World.Core.Storage;

namespace RoguelikeToolkit.World.Core.Settlement;

public class LandCapacityLayer : StoreLayer<LandCapacityInfo>
{
    public LandCapacityLayer(WorldDataStore store) : base(store) { }
}
