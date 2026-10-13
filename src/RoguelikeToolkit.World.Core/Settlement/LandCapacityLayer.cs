using System;

namespace RoguelikeToolkit.World.Core;

public class LandCapacityLayer : StoreLayer<LandCapacityInfo>
{
    public LandCapacityLayer(WorldDataStore store) : base(store) { }
}
