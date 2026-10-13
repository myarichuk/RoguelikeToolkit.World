using System;

namespace RoguelikeToolkit.World.Core;

public class LandUseLayer : StoreLayer<LandUseInfo>
{
    public LandUseLayer(WorldDataStore store) : base(store) { }
}
